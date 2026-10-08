namespace Fuaran.Core

// ============================================================================
//  The encoding-profile vectors (Phase 360).
//
//  A content-addressed store keys its ids on rendered bytes, so it names the rendering its ids
//  were computed under — an `EncodingProfile` — and renders through `Json.renderWith`. A profile is
//  a PROMISE about bytes: what `V1` renders today it renders on every later release. This family is
//  what makes the promise checkable. For a fixed set of values it pins, byte for byte, what each
//  profile renders, and for the pre-images `Fuaran.Core.OpStream` and `Fuaran.Core.OpStream.Dag`
//  build themselves (the actor, the chain payload, the DAG node id, the capture) what each profile
//  folds in.
//
//  The `V1` column is not derived from this package's code. It was measured: the published
//  `0.30.0` binaries rendered every value and every pre-image below, and these are their bytes
//  (DECISIONS.md D120, records the probe). The `V2` column is the live renderer's
//  bytes, so a rewrite of that path for speed is held to them too.
//
//  Drawn from every escaping case — every control character U+0000–U+001F, the quotation mark,
//  the backslash, a plain-text control, an escaped member KEY, a multi-line text field — plus the
//  cases where the profiles must agree: number layout, member order, booleans, empty containers,
//  nesting. A lone surrogate is deliberately absent: both profiles write it through raw, and a raw
//  lone surrogate cannot be carried in the committed UTF-8 file this table is rendered into.
//
//  It sits beside the law kit as `StringEscapeVectors` does — a fixed vector corpus, so the one run
//  is the whole sample. FSharp.Core only, Fable-clean.
// ============================================================================

/// The encoding-profile conformance vectors + their runner (Phase 360).
module EncodingProfileVectors =

    open VectorKit

    /// One value with the exact bytes each profile renders it as.
    type Vector =
        {
            /// The case label, prefixed to every outcome the runner reports for this vector.
            Name: string
            /// The value rendered.
            Value: JVal
            /// `Json.renderWith EncodingProfile.V1` of `Value` — the bytes `0.30.0`'s `Json.render` wrote.
            V1: string
            /// `Json.renderWith EncodingProfile.V2` of `Value` — `Json.render` since `0.33.0`.
            V2: string
        }

    /// One actor with the exact pre-image each profile folds it into the chain and the DAG as.
    type ActorVector =
        {
            /// The case label.
            Name: string
            /// The actor.
            Actor: Actor
            /// `OpStream.encodeActorWith V1` of `Actor` — `0.30.0`'s `Actor.encode`.
            V1: string
            /// `OpStream.encodeActorWith V2` of `Actor` — `Actor.encode` since `0.33.0`.
            V2: string
        }

    /// Every value the table renders, oldest escaping rule first: the 32 control characters (the
    /// three `V1` spells short, and the 29 the profiles agree on), then the composite cases.
    let vectors: Vector list =
        [ for c in 0..0x1F do
              let v2 = sprintf "\\u%04x" c

              let v1 =
                  match c with
                  | 0x0A -> "\\n"
                  | 0x0D -> "\\r"
                  | 0x09 -> "\\t"
                  | _ -> v2

              { Name = sprintf "U+%04X in a string" c
                Value = JStr(string (char c))
                V1 = quoted v1
                V2 = quoted v2 }
          { Name = "quote"
            Value = JStr "\""
            V1 = "\"\\\"\""
            V2 = "\"\\\"\"" }
          { Name = "backslash"
            Value = JStr "\\"
            V1 = "\"\\\\\""
            V2 = "\"\\\\\"" }
          { Name = "plain text passes through"
            Value = JStr "a /bé\U0001F642"
            V1 = "\"a /bé\U0001F642\""
            V2 = "\"a /bé\U0001F642\"" }
          { Name = "control characters in a member key"
            Value = JObj [ "k\n\r\t", JStr "v" ]
            V1 = """{"k\n\r\t":"v"}"""
            V2 = """{"k\u000a\u000d\u0009":"v"}""" }
          { Name = "a multi-line text field"
            Value = JObj [ "kind", JStr "note"; "text", JStr "line one\nline two\r\n\tindented" ]
            V1 = """{"kind":"note","text":"line one\nline two\r\n\tindented"}"""
            V2 = """{"kind":"note","text":"line one\u000aline two\u000d\u000a\u0009indented"}""" }
          { Name = "authored member order"
            Value = JObj [ "b", JInt 1; "a", JInt 2 ]
            V1 = """{"b":1,"a":2}"""
            V2 = """{"b":1,"a":2}""" }
          { Name = "integers"
            Value = JArr [ JInt 0; JInt -7; JInt 2147483647; JInt -2147483648 ]
            V1 = "[0,-7,2147483647,-2147483648]"
            V2 = "[0,-7,2147483647,-2147483648]" }
          { Name = "floats"
            Value =
              JArr
                  [ JFloat 0.1
                    JFloat -0.0
                    JFloat 1e21
                    JFloat 5e-324
                    JFloat 123.456
                    JFloat 9007199254740993.0
                    JFloat 2.0 ]
            V1 = "[0.1,-0,1E+21,5E-324,123.456,9007199254740992,2]"
            V2 = "[0.1,-0,1E+21,5E-324,123.456,9007199254740992,2]" }
          { Name = "booleans and empty containers"
            Value = JArr [ JBool true; JBool false; JArr []; JObj [] ]
            V1 = "[true,false,[],{}]"
            V2 = "[true,false,[],{}]" }
          { Name = "nesting"
            Value = JObj [ "a", JArr [ JObj [ "b", JArr [ JStr "c\n" ] ] ] ]
            V1 = """{"a":[{"b":["c\n"]}]}"""
            V2 = """{"a":[{"b":["c\u000a"]}]}""" } ]

    /// The actors the table folds into the pre-images: one with no control character, a human with a
    /// line feed, an agent with a line feed, a tab and a carriage return across its three fields, and
    /// a human with a NUL (which both profiles spell `\u0000`).
    let actorVectors: ActorVector list =
        [ { Name = "human, plain"
            Actor = Human "alice"
            V1 = """{"kind":"human","id":"alice"}"""
            V2 = """{"kind":"human","id":"alice"}""" }
          { Name = "human with LF"
            Actor = Human "a\nb"
            V1 = """{"kind":"human","id":"a\nb"}"""
            V2 = """{"kind":"human","id":"a\u000ab"}""" }
          { Name = "agent with LF, TAB and CR"
            Actor = Agent("m\n", "1\t", "id\r")
            V1 = """{"kind":"agent","model":"m\n","version":"1\t","id":"id\r"}"""
            V2 = """{"kind":"agent","model":"m\u000a","version":"1\u0009","id":"id\u000d"}""" }
          { Name = "human with NUL"
            Actor = Human "\u0000"
            V1 = """{"kind":"human","id":"\u0000"}"""
            V2 = """{"kind":"human","id":"\u0000"}""" } ]

    /// The two profile pairs, `Wire`'s and `OpStream`'s, in table order.
    let private profilePairs: (EncodingProfile * OpStream.EncodingProfile) list =
        [ EncodingProfile.V1, OpStream.EncodingProfile.V1
          EncodingProfile.V2, OpStream.EncodingProfile.V2 ]

    /// The column of a vector a profile reads.
    let private column (p: EncodingProfile) (v1: string) (v2: string) : string =
        match p with
        | EncodingProfile.V1 -> v1
        | EncodingProfile.V2 -> v2

    /// A hash function that hashes nothing: the id it mints IS the pre-image, so a check can read the
    /// bytes a node id or a capture hash is computed over.
    let private reveal: HashFn = fun prev payload -> prev + "#" + payload

    /// The genesis node's pre-image for one actor and the empty op, under `reveal`.
    let private nodePreimage (actor: string) : string = "#" + actor + "|{}"

    /// The capture the table pins: effect `read<TAB>line`, determinism `io<LF>wall`, value `"v"`.
    let private captureEff = "read\tline"

    /// The capture's determinism tag (a line feed inside it).
    let private captureDet = "io\nwall"

    /// The capture's pre-image under each profile, as the published `0.30.0` binary hashed it for `V1`.
    let private capturePreimage (p: EncodingProfile) : string =
        column
            p
            """{"capture":true,"seq":0,"eff":"read\tline","det":"io\nwall","value":"v"}"""
            """{"capture":true,"seq":0,"eff":"read\u0009line","det":"io\u000awall","value":"v"}"""

    /// `true` when some string or member key inside `v` carries a line feed, carriage return or tab —
    /// the three characters the profiles spell differently.
    let rec private carriesShortForm (v: JVal) : bool =
        let shortIn (s: string) =
            s.IndexOf '\n' >= 0 || s.IndexOf '\r' >= 0 || s.IndexOf '\t' >= 0

        match v with
        | JStr s -> shortIn s
        | JArr xs -> List.exists carriesShortForm xs
        | JObj fields -> fields |> List.exists (fun (k, x) -> shortIn k || carriesShortForm x)
        | _ -> false

    /// Every check one value vector makes, named.
    let runVector (v: Vector) : Corpus.Outcome list =
        [ for p in EncodingProfile.all do
              let tag = v.Name + " / " + EncodingProfile.name p
              expect (tag + " / Json.renderWith") "Json.renderWith" (column p v.V1 v.V2) (Json.renderWith p v.Value)
              // Phase 379: the canonical writer IS the profile's renderer, guarded or not
              expect (tag + " / Canonical.write") "Canonical.write" (column p v.V1 v.V2) (Canonical.write p v.Value)

              expect
                  (tag + " / Canonical.tryWrite")
                  "Canonical.tryWrite"
                  (column p v.V1 v.V2)
                  (match Canonical.tryWrite p v.Value with
                   | Ok t -> t
                   | Error e -> "refused: " + e)

              // the positive control: where the columns differ, the OTHER profile moves a byte of
              // this profile's text and does not accept it as canonical
              if v.V1 <> v.V2 then
                  let other =
                      match p with
                      | EncodingProfile.V1 -> EncodingProfile.V2
                      | EncodingProfile.V2 -> EncodingProfile.V1

                  let name =
                      tag
                      + " / positive control: "
                      + EncodingProfile.name other
                      + " moves a byte and refuses this text as canonical"

                  let mine = column p v.V1 v.V2

                  if
                      Canonical.write other v.Value <> mine
                      && Canonical.isCanonical p mine
                      && not (Canonical.isCanonical other mine)
                  then
                      pass name
                  else
                      fail
                          name
                          (sprintf
                              "%s rendered %s over %s"
                              (EncodingProfile.name other)
                              (Canonical.write other v.Value)
                              mine)

              match v.Value with
              | JStr s ->
                  let body = column p v.V1 v.V2

                  expect
                      (tag + " / Json.escapeWith")
                      "Json.escapeWith"
                      (body.Substring(1, body.Length - 2))
                      (Json.escapeWith p s)
              | _ -> ()
          // V2 IS the live renderer
          expect (v.Name + " / Json.render is V2") "Json.render" v.V2 (Json.render v.Value)
          // both spellings read back as one value
          (let name = v.Name + " / Json.parse reads both columns as one value"

           match Json.parse v.V1, Json.parse v.V2 with
           | Ok a, Ok b when a = b -> pass name
           | a, b -> fail name (sprintf "V1 read %A, V2 read %A" a b))
          // the columns differ exactly where a string carries one of the three characters
          (let name =
              v.Name
              + " / the columns differ exactly where a line feed, carriage return or tab is spelled"

           if (v.V1 <> v.V2) = carriesShortForm v.Value then
               pass name
           else
               fail name (sprintf "columns differ: %b; carries LF/CR/TAB: %b" (v.V1 <> v.V2) (carriesShortForm v.Value))) ]

    /// Every check one actor vector makes: the actor's encoding, the chain payload `configFor` builds
    /// around it, and the node id `Dag.nodeIdWith` folds it into, under each profile.
    let private runActor (a: ActorVector) : Corpus.Outcome list =
        [ for wp, op in profilePairs do
              let tag = a.Name + " / " + EncodingProfile.name wp
              let expected = column wp a.V1 a.V2

              expect
                  (tag + " / OpStream.encodeActorWith")
                  "encodeActorWith"
                  expected
                  (OpStream.encodeActorWith op a.Actor)

              expect
                  (tag + " / OpStream.configFor payload")
                  "configFor.Payload"
                  (payload expected)
                  ((OpStream.configFor op).Payload 0 a.Actor "{}")

              expect
                  (tag + " / Dag.nodeIdWith pre-image")
                  "nodeIdWith"
                  (nodePreimage expected)
                  (Dag.nodeIdWith op reveal (fun () -> "{}") [] a.Actor ())
          expect (a.Name + " / Actor.encode is V2") "Actor.encode" a.V2 (Actor.encode a.Actor) ]

    /// The checks no single vector owns: the two profile types name the same profiles; the DAG's
    /// parents are sorted into a profiled id exactly as into an unprofiled one; and the capture
    /// pre-image each profile hashes.
    let private runShared () : Corpus.Outcome list =
        [ expect
              "profile names: Wire and OpStream spell the same profiles alike"
              "the profile names"
              (EncodingProfile.all |> List.map EncodingProfile.name |> String.concat ",")
              (OpStream.profiles |> List.map OpStream.profileName |> String.concat ",")
          expect
              "profile names: the current profile is the same on both sides"
              "the current profile"
              (EncodingProfile.name EncodingProfile.current)
              (OpStream.profileName OpStream.currentProfile)
          (let name = "profile names: every name parses back to its profile, on both sides"

           let wireOk =
               EncodingProfile.all
               |> List.forall (fun p -> EncodingProfile.tryParse (EncodingProfile.name p) = Some p)

           let streamOk =
               OpStream.profiles
               |> List.forall (fun p -> OpStream.tryProfile (OpStream.profileName p) = Some p)

           let refuses =
               [ "V1"; "v3"; ""; " v1" ]
               |> List.forall (fun s -> EncodingProfile.tryParse s = None && OpStream.tryProfile s = None)

           if wireOk && streamOk && refuses then
               pass name
           else
               fail
                   name
                   (sprintf "Wire round-trips: %b; OpStream round-trips: %b; others refused: %b" wireOk streamOk refuses))
          for _, op in profilePairs do
              expect
                  ("merge pre-image / " + OpStream.profileName op)
                  "nodeIdWith"
                  ("p1,p2" + nodePreimage """{"kind":"human","id":"alice"}""")
                  (Dag.nodeIdWith op reveal (fun () -> "{}") [ "p2"; "p1" ] (Human "alice") ())
          for wp, op in profilePairs do
              let expected = capturePreimage wp

              let capture: EffectCapture =
                  { Seq = 0
                    Eff = captureEff
                    Determinism = captureDet
                    Value = "\"v\""
                    PrevHash = ""
                    Hash = "#" + expected }

              let name = "capture pre-image / " + OpStream.profileName op

              match OpStream.firstCaptureBreakEncoding op (OpStream.configFor op) reveal [ capture ] with
              | None -> pass name
              | Some b -> fail name (sprintf "the capture did not recompute: %A" b) ]

    /// Run the whole family.
    let run () : Corpus.Outcome list =
        (vectors |> List.collect runVector)
        @ (actorVectors |> List.collect runActor)
        @ runShared ()

    /// The whole family as a single verdict — `Ok` or the first failing check, named.
    let check () : Result<unit, string> =
        match run () |> List.filter (fun o -> not o.Passed) with
        | [] -> Ok()
        | first :: _ -> Error(first.Name + ": " + first.Detail)

    /// `lines`, over a table the caller names — the rendering `lawsWith`'s format law holds.
    let private linesOf (vs: Vector list) (actors: ActorVector list) : string list =
        [ for v in vs -> v.Name + "\t" + v.V1 + "\t" + v.V2 ]
        @ [ for a in actors -> "actor: " + a.Name + "\t" + a.V1 + "\t" + a.V2 ]
        @ [ "capture: effect TAB, determinism LF\t"
            + capturePreimage EncodingProfile.V1
            + "\t"
            + capturePreimage EncodingProfile.V2 ]

    /// The table as lines a host in another language can diff: `name<TAB>V1<TAB>V2` per value
    /// vector, then `actor: name<TAB>V1<TAB>V2` per actor, then `capture<TAB>V1<TAB>V2` — the bytes
    /// each profile renders or folds, as the TABLE states them. Deterministic; no seed.
    let lines () : string list = linesOf vectors actorVectors

    /// The family as `LawResult`s over a table the caller hands it (Phase 390; `laws ()` is this over
    /// the committed `vectors` and `actorVectors`): one law per value vector, per actor vector and for
    /// the shared checks, green exactly when every check it makes passes; one law on the FORMAT `lines`
    /// renders (three tab-separated fields, no field carrying a tab or a line break); and the corpus
    /// law, whose evidence is one assertion per vector evaluated, so a run handed no vectors is red by
    /// name (`encoding profile: the corpus evaluated at least one vector`). The shared checks run on
    /// every call — they are the profiles' own, not the table's — and are not counted as a vector.
    let lawsWith (vs: Vector list) (actors: ActorVector list) : LawResult list =
        let corpus = corpusCell "encoding profile"

        let format =
            LawKit.LawCell "lines () renders the table as name, V1 and V2, tab-separated, in table order, one line each"

        let rendered = linesOf vs actors

        let wellFormed (l: string) =
            l.Split('\t').Length = 3 && not (l.Contains "\n") && not (l.Contains "\r")

        format.Check(
            rendered.Length = vs.Length + actors.Length + 1
            && List.forall wellFormed rendered,
            fun () ->
                match rendered |> List.tryFind (fun l -> not (wellFormed l)) with
                | Some l -> sprintf "a line is not three tab-separated fields: %A" l
                | None -> sprintf "lines () rendered %d lines" rendered.Length
        )

        let perVector =
            (vs
             |> List.map (fun v -> verdict corpus ("encoding profile: " + v.Name) (runVector v)))
            @ (actors
               |> List.map (fun a -> verdict corpus ("encoding profile: actor " + a.Name) (runActor a)))

        let shared =
            let cell = LawKit.LawCell "encoding profile: the shared checks"

            match runShared () |> List.tryFind (fun o -> not o.Passed) with
            | Some o -> cell.Check(false, (fun () -> o.Name + ": " + o.Detail))
            | None -> cell.Saw()

            cell.Result

        perVector @ [ shared; format.Result; corpus.Result ]

    /// The family as `LawResult`s, on the `StringEscapeVectors.laws` precedent — `lawsWith vectors
    /// actorVectors`. The committed rendering is `conformance/encoding/encoding-profiles.json`.
    let laws () : LawResult list = lawsWith vectors actorVectors

    /// The stored-codec family (Phase 379) — the one a content-addressed consumer runs against ITS
    /// OWN persisted corpus of canonical texts, `StoredIdentity`'s posture for a store written
    /// through a `Codec<'T>` rather than an op stream. `declared` is the profile's canonical name,
    /// the string the store persists beside its data; `stored` is every text it holds, walked whole.
    /// A content id over a text recomputes exactly when the text does, so the laws are stated over
    /// the bytes and hold for whatever digest the consumer keys on them:
    ///   - the declaration names a profile this Core knows;
    ///   - every stored text reads through the codec with no defect;
    ///   - every stored text IS the codec's canonical text, under the declared profile, of the value
    ///     it reads as — byte for byte, through the guarded writer — so every id recomputes;
    ///   - the positive control: re-rendering each value under every OTHER profile moves its bytes
    ///     exactly when it carries a line feed, carriage return or tab — the one respect the
    ///     profiles differ in (D120) — so a store that carries one is shown to depend on its
    ///     declaration, and a store that carries none is shown not to. A profile added later states
    ///     its own difference here.
    let storedCodecLaws (declared: string) (codec: Codec<'T>) (stored: string list) : LawResult list =
        let known =
            LawKit.LawCell "stored codec: the store declares an encoding profile this Core knows"

        let reads =
            LawKit.LawCell "stored codec: every stored text reads through the codec with no defect"

        let recomputes =
            LawKit.LawCell
                "stored codec: every stored text is the codec's canonical text of its value under the declared profile, byte for byte"

        let control =
            LawKit.LawCell
                "stored codec: re-rendering under every other profile moves exactly the texts whose value carries a character the profiles spell differently"

        let profile = EncodingProfile.tryParse declared

        known.Check(
            profile.IsSome,
            fun () ->
                sprintf
                    "the store declares %A, which names no profile (known: %s)"
                    declared
                    (EncodingProfile.all |> List.map EncodingProfile.name |> String.concat ", ")
        )

        match profile with
        | None ->
            for cell in [ reads; recomputes; control ] do
                cell.Fail "not evaluated: the declaration names no profile"
        | Some p ->
            let decoded = stored |> List.mapi (fun i t -> i, t, Codec.read codec t)

            let firstDefect =
                decoded
                |> List.tryPick (fun (i, _, r) ->
                    match r with
                    | Ok _ -> None
                    | Error(e :: _) -> Some(sprintf "stored text %d: %s" i (DecodeError.render e))
                    | Error [] -> Some(sprintf "stored text %d: refused with no defect" i))

            reads.Check(firstDefect.IsNone, (fun () -> Option.defaultValue "" firstDefect))

            let values =
                decoded
                |> List.choose (fun (i, t, r) ->
                    match r with
                    | Ok v -> Some(i, t, v)
                    | Error _ -> None)

            let stray =
                values
                |> List.tryPick (fun (i, t, v) ->
                    match
                        codec.Write v
                        |> Result.mapError CodecDeclarationFault.describe
                        |> Result.bind (Canonical.tryWrite p)
                    with
                    | Ok w when w = t -> None
                    | Ok w -> Some(sprintf "stored text %d is %s; its value's canonical text is %s" i t w)
                    | Error e -> Some(sprintf "stored text %d has no canonical text: %s" i e))

            recomputes.Check(stray.IsNone, (fun () -> Option.defaultValue "" stray))

            let wrong =
                [ for i, _, v in values do
                      // A value the codec refuses to write is the recompute law's finding, above.
                      for j in Option.toList (Result.toOption (codec.Write v)) do
                          for o in EncodingProfile.all do
                              if o <> p then
                                  let moved = Canonical.write o j <> Canonical.write p j

                                  if moved <> carriesShortForm j then
                                      sprintf
                                          "stored text %d: under %s its bytes %s, but its value %s a line feed, carriage return or tab"
                                          i
                                          (EncodingProfile.name o)
                                          (if moved then "move" else "do not move")
                                          (if moved then "carries no" else "carries") ]
                |> List.tryHead

            control.Check(wrong.IsNone, (fun () -> Option.defaultValue "" wrong))

        [ known.Result
          reads.Result
          recomputes.Result
          control.Result
          VectorKit.storeCell "stored codec: " "text" (List.length stored) ]
