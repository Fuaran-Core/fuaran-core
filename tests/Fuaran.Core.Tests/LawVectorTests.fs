module Fuaran.Core.Tests.LawVectorTests

open System
open System.IO
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  The exported law vectors this repository owns — the `capabilityLaws`
//  vectors (Phase 235) — checked the way a host checks them, and the corpus
//  copy of them held to what this kit renders.
//
//  `LawVectorExport` renders them; nothing here writes into the shared corpus.
//  Emitting is an explicit command (`--emit-laws <dir>`), because the corpus is
//  a separate repository and a suite that wrote into it on every run would
//  dirty a shared clone.
//
//  The `transformLaws` reference vectors and their checks — the sample the law
//  certifies, every vector true of the reference evaluator, the committed file
//  and the corpus copy current, and Phase 216's two readings of a stale copy —
//  left this repository with the evaluator they are answers of, in Phase 258
//  (DECISIONS.md D66). They are the compute repository's now:
//  https://github.com/Fuaran-Core/fuaran-core-compute.
// ---------------------------------------------------------------------------

let private field (name: string) (el: JVal) : JVal option =
    match el with
    | JObj ms -> ms |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
    | _ -> None

let private str (name: string) (el: JVal) : string option =
    match field name el with
    | Some(JStr s) -> Some s
    | _ -> None

// ---------------------------------------------------------------------------
//  Reading a corpus copy against this kit's render — the helpers Phase 216 wrote for the
//  transform vectors' copy, kept for the capability copy's reading below. `kitVersion` is DERIVED
//  from `<Version>`, so a version move restales a copy without any vector changing, and a report
//  must tell that apart from a copy whose vectors differ.
// ---------------------------------------------------------------------------

/// The rendered member carrying the derived stamp.
[<Literal>]
let private stampMember = "kitVersion"

let private emitHere = "dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws"

let private emitCopy =
    "dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws <corpus dir>"

/// The registry's own normalisation, not a second one beside it: `OwnedConformance.fingerprint` IS
/// `String.concat "\n"` of these lines, so the reading below refines the equality
/// `roadmapctl copies` applies rather than inventing a neighbouring one that could drift from it.
let private fingerprintLines = OwnedConformance.fingerprintLines

// Ordinal throughout, for the reason `OwnedConformance.fingerprintLines` records: the
// culture-sensitive overload of `StartsWith` is not the question anyone means to ask of a
// rendered JSON member.
let private isStampLine (line: string) =
    line.TrimStart().StartsWith("\"" + stampMember + "\"", StringComparison.Ordinal)

/// The stamp as the file itself declares it — read back through the parser rather than off the
/// line, so a report never quotes a version the document does not actually carry.
let private stampOf (text: string) : string option =
    match Json.parse text with
    | Ok doc -> str stampMember doc
    | Error _ -> None

/// A readable window onto one long line, centred on `col`. Centred rather than truncated because
/// a vector line is a whole wire string: two of them shown from the left look identical for a
/// hundred characters and the reader learns nothing about where they parted.
let private window (col: int) (line: string) : string =
    let start = max 0 (col - 40)
    let len = min 110 (line.Length - start)

    (if start > 0 then "… " else "")
    + line.Substring(start, len)
    + (if start + len < line.Length then " …" else "")

/// The first character position at which two lines part.
let private firstDivergence (a: string) (b: string) : int =
    let shared = min a.Length b.Length

    match Seq.tryFindIndex (fun i -> a[i] <> b[i]) (seq { 0 .. shared - 1 }) with
    | Some i -> i
    | None -> shared

let private banner (heading: string) (body: string list) : string =
    let rule = String.replicate 78 "="

    String.concat
        "\n"
        ([ ""; rule; "  " + heading; rule ]
         @ [ for l in body -> "  " + l ]
         @ [ rule; "" ])

// ---------------------------------------------------------------------------
//  Phase 235 — the capabilityLaws vectors, checked the way a host checks them.
// ---------------------------------------------------------------------------
//  Moved here from the UI tier, which rendered them against its own Core pin. Each vector is read
//  back through the public codecs a host has — the capability declaration through
//  `CapabilityCodec.decode`, the args as plain `{addr, value}` pairs — and its recorded answer is
//  recomputed rather than trusted, so a perturbed `expected` is named by its id.

let private jstrOf (v: JVal) : string option =
    match v with
    | JStr s -> Some s
    | _ -> None

let private argsOf (v: JVal option) : (string * string) list option =
    match v with
    | Some(JArr items) ->
        let pairs =
            items
            |> List.map (fun a ->
                match str "addr" a, str "value" a with
                | Some addr, Some value -> Some(addr, value)
                | _ -> None)

        if List.forall Option.isSome pairs then
            Some(List.choose id pairs)
        else
            None
    | _ -> None

/// The verdict members `validateArgs` answers with, in the file's host-neutral words.
let private capabilityVerdict (r: Result<unit, InvokeError>) : (string * JVal) list =
    match r with
    | Ok() -> [ "verdict", JStr "accept" ]
    | Error(ArgOutOfSpace(addr, _, _)) -> [ "verdict", JStr "reject"; "error", JStr "argOutOfSpace"; "addr", JStr addr ]
    | Error(UnknownArg(addr, _)) -> [ "verdict", JStr "reject"; "error", JStr "unknownArg"; "addr", JStr addr ]
    | Error _ -> [ "verdict", JStr "reject"; "error", JStr "unexpected" ]

let private checkCapabilityVector (v: JVal) : string option =
    let id = str "id" v |> Option.defaultValue "<no id>"
    let input = field "input" v |> Option.defaultValue (JObj [])

    let expected =
        match field "expected" v with
        | Some(JObj ms) -> ms
        | _ -> []

    let decl (name: string) =
        match str name input with
        | None -> Error(sprintf "%s: input.%s missing" id name)
        | Some d ->
            match CapabilityCodec.decode d with
            | Ok c -> Ok(d, c)
            | Error m -> Error(sprintf "%s: input.%s did not decode (%s)" id name m)

    let differs (actual: (string * JVal) list) =
        if actual = expected then
            None
        else
            Some(sprintf "%s: this kit answers %A but the vector records %A" id actual expected)

    match str "case" v, argsOf (field "args" input) with
    | Some "validateArgs", Some args ->
        match decl "capability" with
        | Error m -> Some m
        | Ok(_, c) -> differs (capabilityVerdict (Capability.validateArgs c args))
    | Some "invocationKey", Some args ->
        match decl "capability" with
        | Error m -> Some m
        | Ok(_, c) ->
            // `capturedValue` is the sample's own draw, not something the kit computes from the
            // inputs; it is carried through and checked against the draw separately.
            let captured = expected |> List.filter (fun (k, _) -> k = "capturedValue")

            differs (
                [ "key", JStr(Capability.invocationKey c args)
                  "determinismTag", JStr(Capability.determinismTag c) ]
                @ captured
            )
    | Some "declarationRoundTrip", _ ->
        match decl "declaration" with
        | Error m -> Some m
        | Ok(d, c) ->
            let back = CapabilityCodec.encode c

            if back <> d then
                Some(sprintf "%s: decode-then-encode did not return the input bytes" id)
            else
                differs [ "declaration", JStr back ]
    | Some "registryEnumerate", _ ->
        match field "declarations" input with
        | Some(JArr ds) ->
            let decoded =
                ds |> List.map (fun d -> jstrOf d |> Option.map CapabilityCodec.decode)

            match
                decoded
                |> List.fold
                    (fun acc d ->
                        match acc, d with
                        | Ok r, Some(Ok c) -> Registry.register c r |> Result.mapError (sprintf "%A")
                        | Error e, _ -> Error e
                        | _, Some(Error m) -> Error m
                        | _, None -> Error "a declaration is not a string")
                    (Ok Registry.empty)
            with
            | Error m -> Some(sprintf "%s: the declarations did not register (%s)" id m)
            | Ok r -> differs [ "ids", JArr(Registry.enumerate r |> List.map (fun c -> JStr c.Id)) ]
        | _ -> Some(sprintf "%s: input.declarations missing" id)
    | Some other, _ -> Some(sprintf "%s: unknown case `%s`" id other)
    | None, _ -> Some(sprintf "%s: no case" id)

let private capabilityVectorsOf (json: string) : Result<JVal list, string> =
    match Json.parse json with
    | Error m -> Error("the capability vector file did not parse: " + m)
    | Ok doc ->
        match field "vectors" doc with
        | Some(JArr items) -> Ok items
        | _ -> Error "the capability vector file carries no `vectors` array"

/// Report the reading through the channel its severity earns. Printed either way: a finding that
/// only a failure would have shown is a finding the ordinary local run does not make.
let private announce (text: string) (fatal: bool) =
    printfn "%s" text
    Console.Out.Flush()

    if fatal then
        failtest text

// ---------------------------------------------------------------------------
//  Phase 235 — the corpus copy of capability-laws.json, pinned byte for byte.
// ---------------------------------------------------------------------------
//  The move carried the renderer over unchanged, and this is the pin that says so: the corpus copy
//  (written by the UI tier's exporter at its Core pin) must be what THIS renderer produces, line for
//  line and byte for byte — with exactly one recorded exception. Phase 225 made `invocationKey`
//  injective, which changed every key VALUE; the copy is re-synced with the TS and Go ports at the
//  UI tier's Core pin raise (fuaran#1860), and until then it legitimately carries the old keys under
//  the old stamp. So a differing line is admitted only if it is the `kitVersion` line or an
//  `invocationKey` vector's line that becomes identical once its `key` value is put back. Anything
//  else — a verdict, a declaration, the description, a vector added or dropped — is a real
//  divergence and is reported as one. Once the copy is re-synced the reading is simply `Fresh`, and
//  the exception admits nothing.

type private CapabilityCopyReading =
    | CapabilityFresh
    | CapabilityCopyMissing
    /// Identical but for the stamp and the invocation keys Phase 225 moved: `keys` lines differ.
    | KeyLag of keys: int * copyStamp: string * kitStamp: string
    | CapabilityDiffers of detail: string

let private keyPattern =
    System.Text.RegularExpressions.Regex(
        "\"key\": \"[^\"]*\"",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    )

let private classifyCapabilityCopy (copyText: string) (kitText: string) : CapabilityCopyReading =
    let copy = fingerprintLines copyText
    let kit = fingerprintLines kitText

    if copy = kit then
        CapabilityFresh
    elif copy.Length <> kit.Length then
        CapabilityDiffers(sprintf "the corpus copy has %d lines, this kit renders %d" copy.Length kit.Length)
    else
        let explained (i: int) =
            if isStampLine copy[i] && isStampLine kit[i] then
                Some false
            elif copy[i].Contains "\"case\": \"invocationKey\"" then
                match keyPattern.Match copy[i] with
                | m when m.Success && keyPattern.Replace(kit[i], m.Value.Replace("$", "$$"), 1) = copy[i] -> Some true
                | _ -> None
            else
                None

        let differing =
            [ for i in 0 .. copy.Length - 1 do
                  if copy[i] <> kit[i] then
                      yield i ]

        match differing |> List.tryFind (fun i -> (explained i).IsNone) with
        | Some i ->
            let col = firstDivergence copy[i] kit[i]

            CapabilityDiffers(
                sprintf
                    "line %d parts at character %d, and it is neither the stamp nor an invocation key —\n    corpus copy: %s\n    this kit:    %s"
                    (i + 1)
                    (col + 1)
                    (window col copy[i])
                    (window col kit[i])
            )
        | None ->
            let keys =
                differing |> List.filter (fun i -> explained i = Some true) |> List.length

            KeyLag(keys, stampOf copyText |> Option.defaultValue "?", stampOf kitText |> Option.defaultValue "?")

[<Tests>]
let tests =
    testList
        "LawVectors"
        [

          testCase "a fingerprint drops a BOM and NOTHING else - the equality has no hole in its first character"
          <| fun _ ->
              // Found by taking the reading from the registry's own normalisation rather than from
              // a copy of it: `StartsWith(string)` compares by the current culture, under which
              // U+FEFF is IGNORABLE, so the obvious spelling answered true for every text and
              // quietly removed the first character of any document that had no BOM. Both sides of
              // a comparison lost the same character, so nothing was ever reported wrongly - but
              // the one equality this repository's published copies are held to was blind to their
              // first byte, and could not tell an empty document from a crash.
              Expect.equal (OwnedConformance.fingerprint "{ \"a\": 1 }") "{ \"a\": 1 }" "no BOM, nothing dropped"

              Expect.equal
                  (OwnedConformance.fingerprint "\uFEFF{ \"a\": 1 }")
                  "{ \"a\": 1 }"
                  "a leading BOM is dropped, and only it"

              Expect.equal (OwnedConformance.fingerprint "") "" "an empty document fingerprints rather than throwing"
              Expect.equal (OwnedConformance.fingerprint "\n\n  \n") "" "a document of blank lines fingerprints empty"

              Expect.notEqual
                  (OwnedConformance.fingerprint "{ \"a\": 1 }")
                  (OwnedConformance.fingerprint "[ \"a\", 1 ]")
                  "two documents differing only in their first character are not the same copy"

              Expect.equal
                  (String.concat "\n" (fingerprintLines (LawVectorExport.Capabilities.render ())))
                  (OwnedConformance.fingerprint (LawVectorExport.Capabilities.render ()))
                  "the reading's lines ARE the fingerprint's, by construction"

          testCase "presence decides whether the copy is compared; the ask decides only what a finding costs"
          <| fun _ ->
              // The four quadrants, hermetically: a synthesised corpus root and a directory that is
              // not one, so nothing here depends on whether this machine happens to hold the corpus.
              let scratch =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-216-" + Guid.NewGuid().ToString("N"))

              let corpus = Path.Combine(scratch, "corpus")
              let notCorpus = Path.Combine(scratch, "not-a-corpus")

              try
                  Directory.CreateDirectory(Path.Combine(corpus, LawVectorExport.familyDirName))
                  |> ignore

                  Directory.CreateDirectory notCorpus |> ignore

                  File.WriteAllText(Path.Combine(corpus, "manifest.json"), "{ \"schema\": \"s\", \"idl\": \"i\" }")

                  let ask = Some "1"
                  let dont = None

                  let at (askValue: string option) (dir: string) =
                      SiblingCorpus.freshnessWith askValue (Some dir) LawVectorExport.familyDirName scratch

                  match at dont corpus with
                  | SiblingCorpus.Compare(root, fatal) ->
                      Expect.equal root corpus "a present corpus is compared even when the leg was not asked for"
                      Expect.isFalse fatal "and a finding against it is not fatal there"
                  | other -> failtestf "an unasked-for run with a corpus present read as %A" other

                  match at ask corpus with
                  | SiblingCorpus.Compare(_, fatal) -> Expect.isTrue fatal "the ask is what makes a finding fatal"
                  | other -> failtestf "an asked-for run with a corpus present read as %A" other

                  match at dont notCorpus with
                  | SiblingCorpus.NotChecked(why, fatal) ->
                      Expect.isFalse fatal "no corpus and no ask is reported, not failed"
                      Expect.stringContains why "NOT CHECKED" "and it says so in those words"
                  | other -> failtestf "an unasked-for run with no corpus read as %A" other

                  match at ask notCorpus with
                  | SiblingCorpus.NotChecked(_, fatal) ->
                      Expect.isTrue fatal "asked for and absent still FAILS — D31 is unchanged"
                  | other -> failtestf "an asked-for run with no corpus read as %A" other
              finally
                  try
                      Directory.Delete(scratch, true)
                  with _ ->
                      ()

          // ---- Phase 235: capabilityLaws, emitted here rather than by the UI tier ----------------

          testCase
              "the exported capability sample is one capabilityLaws certifies, and each draw gets the verdict the law demands"
          <| fun _ ->
              // The law over the exported seed: the sample is a sample of a PASSING run.
              for r in
                  Conformance.capabilityLaws LawVectorExport.Capabilities.seed LawVectorExport.Capabilities.iterations do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              // And each computed expectation is the one the law demands, asserted directly rather than
              // through the renderer, so a renderer that recorded the wrong class could not pass.
              for d in LawVectorExport.Capabilities.draws () do
                  Expect.equal
                      (Capability.validateArgs d.Cap [ "h0", string d.Lo ])
                      (Ok())
                      (sprintf "iteration %d: the in-space arg is accepted" d.Iteration)

                  match Capability.validateArgs d.Cap [ "h0", string (d.Hi + 1) ] with
                  | Error(ArgOutOfSpace _) -> ()
                  | other -> failtestf "iteration %d: out-of-space was not ArgOutOfSpace (%A)" d.Iteration other

                  match Capability.validateArgs d.Cap [ "nope", string d.Lo ] with
                  | Error(UnknownArg _) -> ()
                  | other -> failtestf "iteration %d: an unknown arg was not UnknownArg (%A)" d.Iteration other

              Expect.isFalse
                  ((LawVectorExport.Capabilities.render ()).Contains "\"unexpected\"")
                  "a vector carried a refusal outside the two the law distinguishes"

          testCase "every rendered capability vector reads back through the public codecs and is true of this kit"
          <| fun _ ->
              match capabilityVectorsOf (LawVectorExport.Capabilities.render ()) with
              | Error m -> failtest m
              | Ok vectors ->
                  Expect.equal
                      (List.length vectors)
                      (6 * LawVectorExport.Capabilities.iterations)
                      "six vectors per declared iteration"

                  let failures = vectors |> List.choose checkCapabilityVector
                  Expect.isEmpty failures (sprintf "%A" failures)

                  // `capturedValue` is the draw, which the kit does not compute from the inputs — so it
                  // is held to the draw here.
                  let captured =
                      vectors
                      |> List.choose (fun v ->
                          match str "case" v, field "expected" v with
                          | Some "invocationKey", Some e ->
                              match field "capturedValue" e with
                              | Some(JInt n) -> Some n
                              | _ -> None
                          | _ -> None)

                  Expect.equal
                      captured
                      (LawVectorExport.Capabilities.draws () |> List.map (fun d -> d.Realized))
                      "each invocation-key vector carries its iteration's drawn capture value"

          testCase "the capability checker names a perturbed vector — the oracle leg can go red"
          <| fun _ ->
              let kit = LawVectorExport.Capabilities.render ()

              let perturbed =
                  match kit.IndexOf("\"verdict\": \"accept\"", StringComparison.Ordinal) with
                  | -1 -> failtest "the rendered file carries no accept verdict to perturb"
                  | i -> kit.Substring(0, i) + "\"verdict\": \"reject\"" + kit.Substring(i + 19)

              match capabilityVectorsOf perturbed with
              | Error m -> failtest m
              | Ok vectors ->
                  let failures = vectors |> List.choose checkCapabilityVector
                  Expect.equal (List.length failures) 1 "exactly the perturbed vector is named"
                  Expect.stringContains (List.head failures) "capability-0-accept" "and by its id"

          testCase "the rendered capability artefact is LF-only and byte-stable across renders"
          <| fun _ ->
              let once = LawVectorExport.Capabilities.render ()
              Expect.equal once (LawVectorExport.Capabilities.render ()) "two renders produce the same bytes"
              Expect.isFalse (once.Contains "\r") "no CR may reach a corpus byte-compared across three OSes"

          testCase "the committed conformance/laws/capability-laws.json is the one this kit renders"
          <| fun _ ->
              let path = LawVectorExport.capabilityPath (OwnedConformance.root ())

              if not (File.Exists path) then
                  failtestf
                      "this repository carries no %s at '%s' — re-run `--emit-laws` (no argument writes into conformance/) and commit the result"
                      LawVectorExport.Capabilities.fileName
                      path
              else
                  let committed = File.ReadAllText path

                  match capabilityVectorsOf committed with
                  | Error m -> failtest ("the committed capability vectors did not read: " + m)
                  | Ok vectors ->
                      let failures = vectors |> List.choose checkCapabilityVector

                      Expect.isEmpty
                          failures
                          (sprintf "the committed capability vectors disagree with this kit: %A" failures)

                  Expect.equal
                      committed
                      (LawVectorExport.Capabilities.render ())
                      "the committed conformance/laws/capability-laws.json is not what this kit renders — re-run `--emit-laws` (no argument) and commit conformance/"

          testCase
              "the corpus copy of laws/capability-laws.json is this renderer's output byte for byte, but for the invocation keys fuaran#1860 re-syncs"
          <| fun _ ->
              match SiblingCorpus.freshness LawVectorExport.familyDirName with
              | SiblingCorpus.NotChecked(why, true) -> failtest why
              | SiblingCorpus.NotChecked(why, false) ->
                  printfn "%s" (banner "CAPABILITY CORPUS COPY NOT CHECKED" [ why ])
                  Console.Out.Flush()
                  skiptest why
              | SiblingCorpus.Compare(root, fatal) ->
                  let copy = LawVectorExport.capabilityPath root

                  let reading =
                      if File.Exists copy then
                          classifyCapabilityCopy (File.ReadAllText copy) (LawVectorExport.Capabilities.render ())
                      else
                          CapabilityCopyMissing

                  match reading with
                  | CapabilityFresh -> ()
                  | KeyLag(keys, copyStamp, kitStamp) ->
                      // The recorded lag, never fatal: the copy is the file five hosts certify against,
                      // and re-syncing it ahead of the TS and Go ports would redden them. It is still
                      // SAID, on every run that can see it.
                      printfn
                          "%s"
                          (banner
                              "CAPABILITY CORPUS COPY LAGS — the invocation keys Phase 225 moved (fuaran#1860 re-syncs it)"
                              [ sprintf "copy             %s" copy
                                sprintf "its kitVersion   %s" copyStamp
                                sprintf "this kit renders %s" kitStamp
                                sprintf "%d invocation-key lines differ in their `key` value and in nothing else;" keys
                                "every other line is byte-identical to this renderer's output."
                                "The re-sync lands with the TS and Go ports at the UI tier's Core pin raise:"
                                "    " + emitCopy ])

                      Console.Out.Flush()
                  | CapabilityCopyMissing ->
                      announce
                          (banner "CAPABILITY CORPUS COPY MISSING" [ sprintf "expected at  %s" copy; "    " + emitCopy ])
                          fatal
                  | CapabilityDiffers detail ->
                      announce
                          (banner
                              "CAPABILITY CORPUS COPY DIFFERS — beyond the recorded invocation-key lag"
                              [ sprintf "copy  %s" copy
                                ""
                                detail
                                ""
                                "    " + emitHere
                                "    " + emitCopy ])
                          fatal

          testCase "the capability copy reading admits the key lag and nothing else"
          <| fun _ ->
              // The go-red both ways, hermetically: a copy whose invocation keys (and stamp) moved reads
              // as the lag; the same copy with one verdict perturbed as well reads as a divergence.
              let kit = LawVectorExport.Capabilities.render ()

              let keysMoved =
                  keyPattern
                      .Replace(kit, (fun (m: System.Text.RegularExpressions.Match) -> m.Value.Replace("#", "#00")))
                      .Replace("\"kitVersion\": \"", "\"kitVersion\": \"old-")

              Expect.equal (classifyCapabilityCopy kit kit) CapabilityFresh "an unperturbed copy is fresh"

              match classifyCapabilityCopy keysMoved kit with
              | KeyLag(keys, _, _) ->
                  Expect.equal keys LawVectorExport.Capabilities.iterations "one moved key per iteration"
              | other -> failtestf "a key-and-stamp-only copy read as %A" other

              let alsoVerdict =
                  match keysMoved.IndexOf("\"verdict\": \"accept\"", StringComparison.Ordinal) with
                  | -1 -> failtest "no accept verdict to perturb"
                  | i ->
                      keysMoved.Substring(0, i)
                      + "\"verdict\": \"reject\""
                      + keysMoved.Substring(i + 19)

              match classifyCapabilityCopy alsoVerdict kit with
              | CapabilityDiffers _ -> ()
              | other -> failtestf "a perturbed verdict hidden behind the key lag read as %A" other

              // A key moved on a line that is NOT an invocation-key vector is not the lag either.
              let declarationMoved =
                  kit.Replace("\"id\": \"capability-0-accept\"", "\"id\": \"capability-0-accepted\"")

              match classifyCapabilityCopy declarationMoved kit with
              | CapabilityDiffers _ -> ()
              | other -> failtestf "a moved vector id read as %A" other ]
