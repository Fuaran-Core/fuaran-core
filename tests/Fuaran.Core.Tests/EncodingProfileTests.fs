module Fuaran.Core.Tests.EncodingProfileTests

// Phase 360 — content-addressed stores pin a frozen canonical encoding.
//
// The three stores below were WRITTEN BY `0.30.0`: the published `Fuaran.Core.Wire`,
// `Fuaran.Core.OpStream` and `Fuaran.Core.OpStream.Dag` 0.30.0 binaries, driven by a probe script
// whose op encoder is `Json.render` — the shape a downstream content-addressed store takes. Their
// payload strings carry a newline, a tab or a carriage return, so after Phase 287 (0.33.0) moved the
// spelling of those three characters, the stored ids no longer recompute under the current renderer.
// That is the counter-example to Phase 287's "no known store holds one", kept here as the regression.

open Expecto
open Fuaran.Core

/// A linear store of three records, as `OpStream.toJsonl` 0.30.0 wrote it (FNV-1a default hash).
let internal linear030 =
    """
{"seq":0,"actor":{"kind":"human","id":"alice"},"op":{"kind":"note","text":"line one\nline two"},"prevHash":"","hash":"412a642d"}
{"seq":1,"actor":{"kind":"agent","model":"model\tx","version":"1","id":"bot\n"},"op":{"kind":"note","text":"tab\there","n":1.5},"prevHash":"412a642d","hash":"a0d951dd"}
{"seq":2,"actor":{"kind":"human","id":"bob"},"op":{"kind":"plain","text":"no control character"},"prevHash":"a0d951dd","hash":"026eeb1d"}"""

/// A four-node DAG (a genesis, two forks, their merge), as `Dag.toJsonl` 0.30.0 wrote it.
let internal dag030 =
    """{"node":true,"id":"0485eaa5","parents":["adb56926","27fcbaf5"],"actor":{"kind":"human","id":"alice"},"op":{"kind":"merge","why":"both\r\nsides"}}
{"node":true,"id":"27fcbaf5","parents":["f0ef0556"],"actor":{"kind":"human","id":"bob"},"op":{"kind":"plain","text":"no control character"}}
{"node":true,"id":"adb56926","parents":["f0ef0556"],"actor":{"kind":"agent","model":"model\tx","version":"1","id":"bot\n"},"op":{"kind":"note","text":"tab\there","n":1.5}}
{"node":true,"id":"f0ef0556","parents":[],"actor":{"kind":"human","id":"alice"},"op":{"kind":"note","text":"line one\nline two"}}"""

/// The merge node: the DAG's one head.
let internal dagHead030 = "0485eaa5"

/// A capture log of two records, as `OpStream.captureToJsonl` 0.30.0 wrote it.
let internal captures030 =
    """{"capture":true,"seq":0,"eff":"read\tline","det":"io\nwall","value":"line\n","prevHash":"","hash":"fbdf4255"}
{"capture":true,"seq":1,"eff":"clock","det":"wallclock","value":"t","prevHash":"fbdf4255","hash":"f05ff4c0"}"""

/// The domain the probe wrote with: the op IS a `JVal`, encoded by `Json.render` — today's `V2`.
let internal jvalWitness: StreamWitness<JVal, unit, string> =
    { Apply = fun _ s -> Ok s
      Encode = Json.render
      Decode = Json.parse }

/// The same domain pinned to `V1`: the op encoded by `Json.renderWith EncodingProfile.V1`.
let internal v1Witness: StreamWitness<JVal, unit, string> =
    { jvalWitness with
        Encode = Json.renderWith EncodingProfile.V1 }

let private h = OpStream.defaultHash
let private pV1 = OpStream.EncodingProfile.V1
let private pV2 = OpStream.EncodingProfile.V2

let private ok (r: Result<'a, string>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "did not load: %s" e

let private linear () =
    OpStream.fromJsonl jvalWitness (linear030.Trim()) |> ok

let private dag () =
    Dag.fromJsonl jvalWitness (dag030.Trim()) |> ok

let private captures () =
    OpStream.captureFromJsonl (captures030.Trim()) |> ok

/// The committed rendering of `EncodingProfileVectors.lines`: the table a host in another language,
/// or a later rewrite of the live renderer, diffs against. Written by `--emit-encoding`, held to a
/// fresh render below.
module EncodingCorpus =

    let familyDirName = "encoding"
    let fileName = "encoding-profiles.json"

    let private description =
        "Encoding-profile vectors (Phase 360). A content-addressed store names the rendering its ids were computed under: v1 is the rendering of 0.30.0 through 0.32.0, which spells line feed, carriage return and tab as the short escapes; v2 is the rendering since 0.33.0, which spells every control character U+0000-U+001F as a lower-case backslash-u escape. Each entry of `lines` is one line of the table, three fields separated by one TAB (U+0009), no field carrying a TAB or a line break: for each value its name, its v1 rendering and its v2 rendering (the value is what either rendering parses to); then for each actor `actor: <name>`, its v1 and its v2 encoding as folded into a chain payload and a DAG node id; then the capture pre-image (effect read<TAB>line, determinism io<LF>wall, value \"v\") under each. The v1 column was measured against the published 0.30.0 binaries. A host that reproduces a store's ids under a declared profile renders the same column, byte for byte."

    /// The file, rendered: a header, then one JSON string per line of the table.
    let render () : string =
        let lines = EncodingProfileVectors.lines ()

        let body =
            lines
            |> List.mapi (fun i l ->
                "    \""
                + Json.escape l
                + "\""
                + (if i < List.length lines - 1 then ",\n" else "\n"))
            |> String.concat ""

        "{\n  \"family\": \"encodingProfile\",\n  \"description\": \""
        + Json.escape description
        + "\",\n  \"format\": \"name<TAB>v1<TAB>v2\",\n  \"lines\": [\n"
        + body
        + "  ]\n}\n"

    let path (dir: string) =
        System.IO.Path.Combine(dir, familyDirName, fileName)

    let write (dir: string) : unit =
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, familyDirName))
        |> ignore

        System.IO.File.WriteAllText(path dir, render ())

let private allGreen (what: string) (laws: LawResult list) =
    match laws |> List.tryFind (fun l -> not l.Passed) with
    | Some l -> failtestf "%s: %s — %A" what l.Law l.Counterexample
    | None -> ()

let private redLaws (laws: LawResult list) : string list =
    laws |> List.filter (fun l -> not l.Passed) |> List.map (fun l -> l.Law)

[<Tests>]
let tests =
    testList
        "EncodingProfile"
        [ // ---- the regression: written red first, against the published 0.30.0 stores ----
          testCase
              "regression: the 0.30.0 linear store verifies under the profile it was written under, and not under the current one"
          <| fun _ ->
              let recs = linear ()
              Expect.equal recs.Length 3 "three records"

              Expect.isFalse
                  (OpStream.verifyChain h jvalWitness recs)
                  "the current renderer moved the stored bytes (the Phase 287 counter-example)"

              Expect.isFalse
                  (OpStream.verifyChainWith OpStream.legacyEscapeConfig h jvalWitness recs)
                  "the one-witness legacy config reaches the actor, never the op's own bytes"

              Expect.isTrue
                  (OpStream.verifyChainWith (OpStream.configFor pV1) h v1Witness recs)
                  "under V1 — config and encoder — every stored hash recomputes"

          testCase
              "regression: the 0.30.0 DAG verifies under the profile it was written under, and not under the current one"
          <| fun _ ->
              let d = dag ()
              Expect.equal (Dag.heads d) [ dagHead030 ] "one head"
              Expect.isFalse (Dag.verifyDag h jvalWitness d) "the current renderer moved the stored ids"
              Expect.isTrue (Dag.verifyDagWith pV1 h v1Witness d) "under V1 every stored id recomputes"
              Expect.isNone (Dag.firstBreakWith pV1 h v1Witness d) "and nothing breaks"

              for n in d.Nodes |> Map.toList |> List.map snd do
                  Expect.equal
                      (Dag.nodeIdWith pV1 h v1Witness.Encode n.Parents n.Actor n.Op)
                      n.Id
                      "nodeIdWith V1 mints the stored id"

          testCase
              "regression: the 0.30.0 capture log verifies under the profile it was written under, and not under the current one"
          <| fun _ ->
              let caps = captures ()
              Expect.isFalse (OpStream.verifyCaptures h caps) "the current spelling moved the stored hashes"

              Expect.isNone
                  (OpStream.firstCaptureBreakEncoding pV1 OpStream.canonicalConfig h caps)
                  "under V1 every stored capture hash recomputes"

          // ---- the profile surface ----
          testCase "V2 is the current default on every surface"
          <| fun _ ->
              Expect.equal EncodingProfile.current EncodingProfile.V2 "Wire's current"
              Expect.equal OpStream.currentProfile pV2 "OpStream's current"
              let recs = linear ()
              let d = dag ()

              for r in recs do
                  Expect.equal
                      ((OpStream.configFor pV2).Payload r.Seq r.Actor "{}")
                      (OpStream.canonicalConfig.Payload r.Seq r.Actor "{}")
                      "configFor V2 is canonicalConfig"

                  Expect.equal (OpStream.encodeActorWith pV2 r.Actor) (Actor.encode r.Actor) "encodeActorWith V2"

              for n in d.Nodes |> Map.toList |> List.map snd do
                  Expect.equal
                      (Dag.nodeIdWith pV2 h jvalWitness.Encode n.Parents n.Actor n.Op)
                      (Dag.nodeId h jvalWitness.Encode n.Parents n.Actor n.Op)
                      "nodeIdWith V2 is nodeId"

              Expect.equal (Dag.firstBreakWith pV2 h jvalWitness d) (Dag.firstBreak h jvalWitness d) "firstBreakWith V2"

              for v in EncodingProfileVectors.vectors do
                  Expect.equal
                      (Json.renderWith EncodingProfile.V2 v.Value)
                      (Json.render v.Value)
                      "renderWith V2 is render"

          testCase "V1 renders a value of any nesting depth (the iterative writer, not a recursive one)"
          <| fun _ ->
              let deep = List.fold (fun acc _ -> JArr [ acc ]) (JStr "\n") [ 1..20000 ]
              let text = Json.renderWith EncodingProfile.V1 deep
              Expect.equal text.Length (20000 * 2 + 4) "every bracket and the short-escaped string"
              Expect.stringContains text "\"\\n\"" "the line feed spelled short"

          testCase "the encoding-profile vectors are green, and they measured something"
          <| fun _ ->
              match EncodingProfileVectors.check () with
              | Ok() -> ()
              | Error e -> failtestf "EncodingProfileVectors: %s" e

              Expect.isGreaterThan (EncodingProfileVectors.run ()).Length 250 "every vector through every check"
              allGreen "EncodingProfileVectors.laws" (EncodingProfileVectors.laws ())

          testCase "the vector family can go red: a row claiming V1 spells a line feed as V2 does is refused"
          <| fun _ ->
              let row: EncodingProfileVectors.Vector =
                  { Name = "probe"
                    Value = JStr "\n"
                    V1 = "\"\\u000a\""
                    V2 = "\"\\u000a\"" }

              Expect.isTrue
                  (EncodingProfileVectors.runVector row |> List.exists (fun o -> not o.Passed))
                  "a V1 column that is not 0.30.0's bytes is caught"

          testCase "the committed conformance/encoding/ file is what this kit renders, and parses back to the lines"
          <| fun _ ->
              let file = EncodingCorpus.path (OwnedConformance.root ())

              Expect.isTrue
                  (System.IO.File.Exists file)
                  (sprintf "%s exists — run `--emit-encoding` (no argument) and commit conformance/" file)

              let committed = (System.IO.File.ReadAllText file).Replace("\r\n", "\n")

              Expect.equal
                  committed
                  (EncodingCorpus.render ())
                  "the committed encoding file is not what this kit renders — re-run `--emit-encoding` (no argument) and commit conformance/"

              match Json.parse committed with
              | Ok(JObj members) ->
                  match members |> List.tryFind (fun (k, _) -> k = "lines") with
                  | Some(_, JArr items) ->
                      Expect.equal
                          (items
                           |> List.map (function
                               | JStr s -> s
                               | other -> failtestf "a line is not a string: %A" other))
                          (EncodingProfileVectors.lines ())
                          "the file's lines read back as the table"
                  | other -> failtestf "no lines array: %A" other
              | other -> failtestf "the encoding file does not parse as an object: %A" other

          // ---- the two-witness rehash ----
          testCase "linear: the two-witness rehash migrates the 0.30.0 store to V2, maps every hash, and comes back"
          <| fun _ ->
              let recs = linear ()

              match
                  OpStream.rehashEncoding (OpStream.configFor pV1) v1Witness OpStream.canonicalConfig jvalWitness h recs
              with
              | Error b -> failtestf "refused: %A" b
              | Ok(migrated, ids) ->
                  Expect.isTrue
                      (OpStream.verifyChain h jvalWitness migrated)
                      "the result verifies under the current profile"

                  Expect.equal (Map.count ids) 3 "every stored hash is mapped"

                  Expect.equal
                      (recs |> List.map (fun r -> ids.[r.Hash]))
                      (migrated |> List.map (fun r -> r.Hash))
                      "the map names each record's new hash"

                  Expect.isFalse
                      (recs |> List.exists (fun r -> r.Hash = ids.[r.Hash]))
                      "every record follows a moved one, so every hash moved"

                  match
                      OpStream.rehashEncoding
                          OpStream.canonicalConfig
                          jvalWitness
                          (OpStream.configFor pV1)
                          v1Witness
                          h
                          migrated
                  with
                  | Error b -> failtestf "the rehash back refused: %A" b
                  | Ok(back, _) -> Expect.equal back recs "the rehash back is the source again"

          testCase "linear: the two-witness rehash refuses a source that does not verify under the profile it names"
          <| fun _ ->
              match
                  OpStream.rehashEncoding
                      OpStream.canonicalConfig
                      jvalWitness
                      (OpStream.configFor pV1)
                      v1Witness
                      h
                      (linear ())
              with
              | Error b ->
                  Expect.equal b.Index 0 "the first record"
                  Expect.equal b.Reason ChainBreakReason.HashMismatch "named as a hash mismatch"
              | Ok _ -> failtest "a V1 store named as V2 was re-blessed"

          testCase
              "DAG: the two-witness rehash migrates the 0.30.0 DAG to V2, maps every id, carries lanes, and comes back"
          <| fun _ ->
              let d = dag ()

              match Dag.rehashEncoding pV1 v1Witness pV2 jvalWitness h d with
              | Error f -> failtestf "refused: %A" f
              | Ok(migrated, ids) ->
                  Expect.isTrue (Dag.verifyDag h jvalWitness migrated) "the result verifies under the current profile"
                  Expect.equal (Map.count ids) 4 "every node is mapped"
                  Expect.equal (Dag.heads migrated) [ ids.[dagHead030] ] "the head is the old head's new id"

                  match Dag.rehashEncoding pV2 jvalWitness pV1 v1Witness h migrated with
                  | Error f -> failtestf "the rehash back refused: %A" f
                  | Ok(back, _) -> Expect.equal back d "the rehash back is the source again"

              let loaded: Dag.Loaded<JVal> =
                  { Dag = d
                    LaneOf = d.Nodes |> Map.map (fun id _ -> if id = dagHead030 then "merge" else "main") }

              match Dag.rehashEncoding pV1 v1Witness pV2 jvalWitness h loaded.Dag with
              | Error f -> failtestf "lanes refused: %A" f
              | Ok(d', ids) ->
                  let laneOf =
                      loaded.LaneOf |> Map.toList |> List.map (fun (k, l) -> ids.[k], l) |> Map.ofList

                  Expect.equal laneOf.[ids.[dagHead030]] "merge" "the head's lane moves with its id"

                  Expect.isTrue
                      (Dag.verifyLanes h jvalWitness ({ Dag = d'; LaneOf = laneOf }: Dag.Loaded<JVal>)
                       |> Result.isOk)
                      "the re-keyed lane store verifies"

          testCase "DAG: the two-witness rehash refuses a source that does not verify under the profile it names"
          <| fun _ ->
              match Dag.rehashEncoding pV2 jvalWitness pV1 v1Witness h (dag ()) with
              | Error(Dag.RehashFault.Unverified b) ->
                  Expect.equal b.Reason DagBreakReason.ContentIdMismatch "named as a content-id mismatch"
              | other -> failtestf "a V1 DAG named as V2 was not refused as unverified: %A" other

          testCase "captures: the rehash migrates the 0.30.0 log to V2 and comes back; a mislabelled log is refused"
          <| fun _ ->
              let caps = captures ()

              match OpStream.rehashCapturesEncoding pV1 pV2 OpStream.canonicalConfig h caps with
              | Error b -> failtestf "refused: %A" b
              | Ok(migrated, ids) ->
                  Expect.isTrue (OpStream.verifyCaptures h migrated) "the result verifies under the live walker"
                  Expect.equal (Map.count ids) 2 "both hashes mapped"

                  Expect.equal
                      (migrated |> List.map (fun c -> c.Value))
                      (caps |> List.map (fun c -> c.Value))
                      "values carried byte for byte"

                  match OpStream.rehashCapturesEncoding pV2 pV1 OpStream.canonicalConfig h migrated with
                  | Error b -> failtestf "the rehash back refused: %A" b
                  | Ok(back, _) -> Expect.equal back caps "the rehash back is the source again"

              Expect.isError
                  (OpStream.rehashCapturesEncoding pV2 pV1 OpStream.canonicalConfig h caps)
                  "a V1 log named as V2 is refused"

          // ---- the stored-identity family, at the 0.30.0 stores ----
          testCase "stored identity: the 0.30.0 stores, declared v1, are green on every law"
          <| fun _ ->
              allGreen "linearLaws" (StoredIdentity.linearLaws "v1" h v1Witness jvalWitness (linear ()))
              allGreen "dagLaws" (StoredIdentity.dagLaws "v1" h v1Witness jvalWitness (dag ()))
              allGreen "captureLaws" (StoredIdentity.captureLaws "v1" h (captures ()))

          testCase "stored identity: a store declaring the wrong profile, or none, is red"
          <| fun _ ->
              Expect.contains
                  (redLaws (StoredIdentity.linearLaws "v2" h jvalWitness jvalWitness (linear ())))
                  "every stored record hash recomputes under the declared profile"
                  "a V1 store declared v2 does not recompute"

              Expect.contains
                  (redLaws (StoredIdentity.dagLaws "v2" h jvalWitness jvalWitness (dag ())))
                  "every stored node id recomputes under the declared profile"
                  "a V1 DAG declared v2 does not recompute"

              Expect.contains
                  (redLaws (StoredIdentity.captureLaws "v2" h (captures ())))
                  "every stored capture hash recomputes under the declared profile"
                  "a V1 capture log declared v2 does not recompute"

              Expect.contains
                  (redLaws (StoredIdentity.captureLaws "V1" h (captures ())))
                  "the store declares an encoding profile this Core knows"
                  "an unknown declaration is refused by name"

          testCase "stored identity: a store written under the current profile is green declared v2"
          <| fun _ ->
              match
                  OpStream.rehashEncoding
                      (OpStream.configFor pV1)
                      v1Witness
                      OpStream.canonicalConfig
                      jvalWitness
                      h
                      (linear ())
              with
              | Error b -> failtestf "refused: %A" b
              | Ok(migrated, _) ->
                  allGreen "linearLaws v2" (StoredIdentity.linearLaws "v2" h jvalWitness jvalWitness migrated) ]
