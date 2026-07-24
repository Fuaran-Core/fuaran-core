module Fuaran.Core.Tests.AiSurfaceTests

// Phase 59 — the generic AI-surface seam, exercised over a tiny in-test reference
// domain (a note list): the catalogue, read-tool dispatch, the pattern bank's
// deterministic fast-path, the proposal lifecycle, and `Conformance.aiSurfaceLaws`
// self-proven green. Plus the open-core boundary (GP6): an empty witness yields
// ZERO content — every read tool, pattern, and policy rule comes from the domain.

open Expecto
open Fuaran.Core

// ---- the reference domain: a note list ----

type private NoteState = { Notes: (string * string) list }

type private NoteOp =
    | AddNote of id: string * text: string
    | RemoveNote of id: string

type private NoteRej =
    | DuplicateId of id: string * known: string list
    | NoSuchNote of id: string * known: string list

let private applyNote (op: NoteOp) (s: NoteState) : Result<NoteState, NoteRej> =
    let known = s.Notes |> List.map fst

    match op with
    | AddNote(id, text) ->
        if List.contains id known then
            Error(DuplicateId(id, known))
        else
            Ok { Notes = s.Notes @ [ id, text ] }
    | RemoveNote id ->
        if List.contains id known then
            Ok { Notes = s.Notes |> List.filter (fun (i, _) -> i <> id) }
        else
            Error(NoSuchNote(id, known))

let private explainNote (rej: NoteRej) : RejectionGuidance =
    match rej with
    | DuplicateId(id, known) ->
        { Message = "note id '" + id + "' already exists"
          Alternatives = known |> List.map (fun k -> "existing id: " + k) }
    | NoSuchNote(id, known) ->
        { Message = "no note with id '" + id + "'"
          Alternatives = known |> List.map (fun k -> "existing id: " + k) }

let private emitAddNote (args: (string * string) list) : Result<NoteOp list, string> =
    // the canonical text arg; the conformance kit's synthetic intents use "value".
    match
        args
        |> List.tryPick (fun (k, v) -> if k = "text" || k = "value" then Some v else None)
    with
    | Some text -> Ok [ AddNote("n-" + text, text) ]
    | None -> Error "add-note needs a 'text' arg"

let private witness: AiSurfaceWitness<NoteState, NoteOp, NoteRej> =
    { ReadTools =
        [ { Name = "listNotes"
            Description = "Every note id in order"
            Run = fun s -> JArr(s.Notes |> List.map (fst >> JStr)) }
          { Name = "noteCount"
            Description = "How many notes"
            Run = fun s -> JInt(List.length s.Notes) } ]
      OpKinds =
        [ { Kind = "addNote"
            Description = "Append a note"
            Schema = Json.kindObj "signature" [ "holes", JArr [ JStr "id"; JStr "text" ] ] }
          { Kind = "removeNote"
            Description = "Remove a note by id"
            Schema = Json.kindObj "signature" [ "holes", JArr [ JStr "id" ] ] } ]
      KindOfOp =
        function
        | AddNote _ -> "addNote"
        | RemoveNote _ -> "removeNote"
      Patterns =
        [ { Name = "add-note"
            Title = "Add a note"
            PromptAnchors = [ "add a note {text}"; "note that {text}" ]
            Emit = emitAddNote } ]
      Decide = fun _ _ -> Allow
      Apply = applyNote
      Explain = explainNote }

let private state0 = { Notes = [ "n1", "hello" ] }

/// A shape-only empty witness — the GP6 boundary probe: the core must supply
/// zero read tools, zero op kinds, zero patterns, and no guidance content.
let private emptyWitness: AiSurfaceWitness<unit, string, string> =
    { ReadTools = []
      OpKinds = []
      KindOfOp = id
      Patterns = []
      Decide = fun _ _ -> Allow
      Apply = fun _ () -> Ok()
      Explain = fun m -> { Message = m; Alternatives = [] } }

// ---- the op generator for the laws (covers both kinds + a rejection path) ----

let private genNoteOp (rng: ConfRng.T) : NoteOp * ConfRng.T =
    let k, r1 = ConfRng.intBelow 3 rng

    match k with
    | 0 ->
        let v, r2 = ConfRng.intBelow 1000 r1
        AddNote("n" + string v, "text " + string v), r2
    | 1 -> RemoveNote "n1", r1
    | _ -> RemoveNote "missing", r1

[<Tests>]
let tests =
    testList
        "AiSurface"
        [ testCase "catalogueJson is valid canonical JSON carrying tools + ops + patterns"
          <| fun _ ->
              let json = AiSurface.catalogueJson witness

              match Json.parse json with
              | Error e -> failtestf "catalogueJson did not parse: %s" e
              | Ok(JObj fields) ->
                  Expect.equal (List.tryHead fields) (Some("kind", JStr "aiSurface")) "kind-tagged envelope"

                  for name in [ "readTools"; "ops"; "patterns" ] do
                      Expect.isSome (fields |> List.tryFind (fst >> (=) name)) (name + " present")

                  Expect.stringContains json "listNotes" "read tool named"
                  Expect.stringContains json "addNote" "op kind named"
                  Expect.stringContains json "add-note" "pattern named"
              | Ok other -> failtestf "expected an object, got %A" other

          testCase "runTool dispatches by name and is default-deny with alternatives"
          <| fun _ ->
              Expect.equal (AiSurface.runTool witness "noteCount" state0) (Ok(JInt 1)) "known tool runs"

              match AiSurface.runTool witness "outline" state0 with
              | Error msg ->
                  Expect.stringContains msg "outline" "names the unknown tool"
                  Expect.stringContains msg "listNotes" "enumerates the available tools"
              | Ok _ -> failtest "an unknown tool name was not refused"

          testCase "matchesAnchor: literal segments in order, wildcards free, case-insensitive"
          <| fun _ ->
              Expect.isTrue
                  (PatternBank.matchesAnchor "add a note {text}" "please ADD a note buy milk")
                  "wildcard + case"

              Expect.isTrue
                  (PatternBank.matchesAnchor "look up {key} against" "look up VAT against the table")
                  "mid wildcard"

              Expect.isFalse (PatternBank.matchesAnchor "add a note {text}" "remove a note") "unmatched literal"

              Expect.isFalse
                  (PatternBank.matchesAnchor "{a} before {b} after" "after before")
                  "segments must be in order"

          testCase "resolve: a matched intent emits ops; an unmatched one falls through as None"
          <| fun _ ->
              let intent =
                  { Text = "note that milk is out"
                    Args = [ "text", "milk is out" ] }

              match PatternBank.resolve witness intent with
              | Some(Ok [ AddNote("n-milk is out", "milk is out") ]) -> ()
              | other -> failtestf "expected the add-note emission, got %A" other

              Expect.isNone
                  (PatternBank.resolve
                      witness
                      { Text = "recalculate everything"
                        Args = [] })
                  "no anchor ⇒ fall through to the model"

          testCase "resolve: bank order decides — the first matching pattern wins"
          <| fun _ ->
              let general =
                  { Name = "general"
                    Title = "General add"
                    PromptAnchors = [ "add {x}" ]
                    Emit = fun _ -> Ok [ AddNote("general", "g") ] }

              let specific =
                  { Name = "specific"
                    Title = "Specific add"
                    PromptAnchors = [ "add a note {x}" ]
                    Emit = fun _ -> Ok [ AddNote("specific", "s") ] }

              let w2 =
                  { witness with
                      Patterns = [ specific; general ] }

              match PatternBank.resolve w2 { Text = "add a note here"; Args = [] } with
              | Some(Ok [ AddNote("specific", _) ]) -> ()
              | other -> failtestf "expected the first-listed (specific) pattern, got %A" other

          testCase "submit: Allow applies; Deny refuses; NeedsApproval parks whole"
          <| fun _ ->
              match Proposals.submit witness "agent" "t0" None [ AddNote("n2", "b") ] Proposals.Queue.empty state0 with
              | Proposals.SubmitApplied s -> Expect.equal (List.map fst s.Notes) [ "n1"; "n2" ] "applied in order"
              | other -> failtestf "expected SubmitApplied, got %A" other

              let deny =
                  { witness with
                      Decide = fun _ _ -> Deny "read-only region" }

              match Proposals.submit deny "agent" "t0" None [ AddNote("n2", "b") ] Proposals.Queue.empty state0 with
              | Proposals.SubmitDenied "read-only region" -> ()
              | other -> failtestf "expected SubmitDenied, got %A" other

              let gated =
                  { witness with
                      Decide =
                          fun _ op ->
                              match op with
                              | RemoveNote _ -> NeedsApproval
                              | _ -> Allow }

              // a mixed sequence parks as a unit — approval covers what the agent proposed.
              match
                  Proposals.submit
                      gated
                      "agent"
                      "t0"
                      (Some "tidy")
                      [ AddNote("n2", "b"); RemoveNote "n1" ]
                      Proposals.Queue.empty
                      state0
              with
              | Proposals.SubmitProposed(q, id) ->
                  Expect.equal (Proposals.Queue.pending q |> List.map (fun p -> p.Id)) [ id ] "parked pending"

                  match Proposals.approve gated "reviewer" "t1" id q state0 with
                  | Ok(q2, s) ->
                      Expect.equal (List.map fst s.Notes) [ "n2" ] "both ops applied on approval"

                      match (q2.Proposals |> List.exactlyOne).Status with
                      | Proposals.Approved("reviewer", "t1") -> ()
                      | other -> failtestf "expected dual-attributed approval, got %A" other
                  | Error e -> failtestf "approval failed: %A" e
              | other -> failtestf "expected SubmitProposed, got %A" other

          testCase "approve: a stale proposal surfaces OpNoLongerApplies and stays pending"
          <| fun _ ->
              let q, id =
                  Proposals.propose "agent" "t0" None [ RemoveNote "gone" ] Proposals.Queue.empty

              match Proposals.approve witness "reviewer" "t1" id q state0 with
              | Error(Proposals.OpNoLongerApplies(pid, NoSuchNote("gone", _))) ->
                  Expect.equal pid id "names the proposal"

                  Expect.equal
                      (Proposals.Queue.pending q |> List.length)
                      1
                      "stays pending — repair-or-reject is the approver's call"
              | other -> failtestf "expected OpNoLongerApplies, got %A" other

          testCase "reject records the reason; unknown ids are refused enumerating the pending ones"
          <| fun _ ->
              let q, id =
                  Proposals.propose "agent" "t0" None [ AddNote("n2", "b") ] Proposals.Queue.empty

              match Proposals.reject "reviewer" "t1" "not now" id q with
              | Ok q2 ->
                  match (q2.Proposals |> List.exactlyOne).Status with
                  | Proposals.Rejected("reviewer", "t1", "not now") -> ()
                  | other -> failtestf "expected the recorded rejection, got %A" other
              | Error e -> failtestf "reject failed: %A" e

              match Proposals.approve witness "reviewer" "t1" 99 q state0 with
              | Error(Proposals.UnknownProposal(99, pending)) -> Expect.equal pending [ id ] "pending ids enumerated"
              | other -> failtestf "expected UnknownProposal, got %A" other

          testCase "explainRejection renders the message + enumerated alternatives"
          <| fun _ ->
              let text = Proposals.explainRejection witness (NoSuchNote("n9", [ "n1"; "n2" ]))
              Expect.stringContains text "no note with id 'n9'" "the message"
              Expect.stringContains text "Alternatives:" "the alternatives header"
              Expect.stringContains text "- existing id: n1" "each alternative enumerated"

          testCase "open-core boundary (GP6): an empty witness yields zero content"
          <| fun _ ->
              Expect.equal
                  (AiSurface.catalogueJson emptyWitness)
                  "{\"kind\":\"aiSurface\",\"readTools\":[],\"ops\":[],\"patterns\":[]}"
                  "the core supplies no default read tool, op kind, or pattern"

              Expect.isNone
                  (PatternBank.resolve emptyWitness { Text = "anything at all"; Args = [] })
                  "the core supplies no pattern content"

              match AiSurface.runTool emptyWitness "anything" () with
              | Error _ -> ()
              | Ok _ -> failtest "the core supplied a read tool the domain did not"

              Expect.equal
                  (Proposals.renderGuidance { Message = "m"; Alternatives = [] })
                  "m"
                  "the core invents no alternatives — the domain enumerates them"

          testCase "aiSurfaceLaws certify the reference witness green (seed-replayable)"
          <| fun _ ->
              let results = Conformance.aiSurfaceLaws witness genNoteOp state0 1234 200

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample) ]
