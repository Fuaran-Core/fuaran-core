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
//  Since Phase 520 every value vector pins the two CROSS-HOST renderings too (`Rendering.CrossHost`,
//  `Canon.render` until then, and `Rendering.Declared`, `Canon.renderOrdered`): a column per named
//  rendering, measured from the `0.38.1` binaries' `Canon.render` / `Canon.renderOrdered` before the
//  fold into `Canonical`, so the rendering contract is held to the bytes every host hashes. The
//  committed `lines` table stays the two store profiles' (`name<TAB>V1<TAB>V2`).
//
//  It sits beside the law kit as `StringEscapeVectors` does — a fixed vector corpus, so the one run
//  is the whole sample. FSharp.Core only, Fable-clean.
// ============================================================================

/// The encoding-profile conformance vectors + their runner (Phase 360).
module EncodingProfileVectors =

    open VectorKit

    /// One value with the exact bytes each named rendering writes it as.
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
            /// `Canonical.write Rendering.CrossHost` of `Value` — `Canon.render`'s bytes, unmoved since
            /// `0.30.0` (Phase 520).
            CrossHost: string
            /// `Canonical.write Rendering.Declared` of `Value` — `Canon.renderOrdered`'s bytes (Phase 520).
            Declared: string
        }

    /// One actor with the exact pre-image each profile folds it into the chain and the DAG as.
    type ActorVector =
        {
            /// The case label.
            Name: string
            /// The actor.
            Actor: Actor
            /// `StreamConfig.spellActor StreamConfig.legacyEscape` of `Actor` — `0.30.0`'s `Actor.encode`.
            V1: string
            /// `StreamConfig.spellActor StreamConfig.canonical` of `Actor` — `Actor.encode` since `0.33.0`.
            V2: string
        }

    /// Every value the table renders, oldest escaping rule first: the 32 control characters (the
    /// three `V1` spells short, and the 29 the profiles agree on), then the composite cases.
    let vectors: Vector list =
        [ for c, v2, v1 in controlCharacters do
              { Name = sprintf "U+%04X in a string" c
                Value = JStr(string (char c))
                V1 = quoted v1
                V2 = quoted v2
                CrossHost = quoted v2
                Declared = quoted v2 }
          { Name = "quote"
            Value = JStr "\""
            V1 = "\"\\\"\""
            V2 = "\"\\\"\""
            CrossHost = "\"\\\"\""
            Declared = "\"\\\"\"" }
          { Name = "backslash"
            Value = JStr "\\"
            V1 = "\"\\\\\""
            V2 = "\"\\\\\""
            CrossHost = "\"\\\\\""
            Declared = "\"\\\\\"" }
          { Name = "plain text passes through"
            Value = JStr "a /bé\U0001F642"
            V1 = "\"a /bé\U0001F642\""
            V2 = "\"a /bé\U0001F642\""
            CrossHost = "\"a /bé\U0001F642\""
            Declared = "\"a /bé\U0001F642\"" }
          { Name = "control characters in a member key"
            Value = JObj [ "k\n\r\t", JStr "v" ]
            V1 = """{"k\n\r\t":"v"}"""
            V2 = """{"k\u000a\u000d\u0009":"v"}"""
            CrossHost = """{"k\u000a\u000d\u0009":"v"}"""
            Declared = """{"k\u000a\u000d\u0009":"v"}""" }
          { Name = "a multi-line text field"
            Value = JObj [ "kind", JStr "note"; "text", JStr "line one\nline two\r\n\tindented" ]
            V1 = """{"kind":"note","text":"line one\nline two\r\n\tindented"}"""
            V2 = """{"kind":"note","text":"line one\u000aline two\u000d\u000a\u0009indented"}"""
            CrossHost = """{"kind":"note","text":"line one\u000aline two\u000d\u000a\u0009indented"}"""
            Declared = """{"kind":"note","text":"line one\u000aline two\u000d\u000a\u0009indented"}""" }
          { Name = "authored member order"
            Value = JObj [ "b", JInt 1; "a", JInt 2 ]
            V1 = """{"b":1,"a":2}"""
            V2 = """{"b":1,"a":2}"""
            CrossHost = """{"a":2,"b":1}"""
            Declared = """{"b":1,"a":2}""" }
          { Name = "integers"
            Value = JArr [ JInt 0; JInt -7; JInt 2147483647; JInt -2147483648 ]
            V1 = "[0,-7,2147483647,-2147483648]"
            V2 = "[0,-7,2147483647,-2147483648]"
            CrossHost = "[0,-7,2147483647,-2147483648]"
            Declared = "[0,-7,2147483647,-2147483648]" }
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
            V2 = "[0.1,-0,1E+21,5E-324,123.456,9007199254740992,2]"
            CrossHost = "[0.1,0,1E+21,5E-324,123.456,9007199254740992,2]"
            Declared = "[0.1,0,1E+21,5E-324,123.456,9007199254740992,2]" }
          { Name = "booleans and empty containers"
            Value = JArr [ JBool true; JBool false; JArr []; JObj [] ]
            V1 = "[true,false,[],{}]"
            V2 = "[true,false,[],{}]"
            CrossHost = "[true,false,[],{}]"
            Declared = "[true,false,[],{}]" }
          { Name = "nesting"
            Value = JObj [ "a", JArr [ JObj [ "b", JArr [ JStr "c\n" ] ] ] ]
            V1 = """{"a":[{"b":["c\n"]}]}"""
            V2 = """{"a":[{"b":["c\u000a"]}]}"""
            CrossHost = """{"a":[{"b":["c\u000a"]}]}"""
            Declared = """{"a":[{"b":["c\u000a"]}]}""" } ]

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

    /// The op stream's config each profile names (Phase 516: the op stream carries no profile type of
    /// its own; its profiles are the config constructors), in table order.
    let private configOf (p: EncodingProfile) : StreamConfig =
        match p with
        | EncodingProfile.V1 -> StreamConfig.legacyEscape
        | EncodingProfile.V2 -> StreamConfig.canonical

    /// A hash function that hashes nothing: the id it mints IS the pre-image, so a check can read the
    /// bytes a node id or a capture hash is computed over.
    let private reveal: HashFn = fun prev payload -> prev + "#" + payload

    /// A stream whose op encodes as `{}`, so a node id's pre-image is the actor's and the parents'.
    let private bareStream (cfg: StreamConfig) : Stream<unit, unit, unit> =
        OpStream.ofConfig
            cfg
            reveal
            { Apply = fun () s -> Ok s
              Encode = fun () -> "{}"
              Decode = fun _ -> Ok() }

    /// The column of a vector a profile reads.
    let private column (p: EncodingProfile) (v1: string) (v2: string) : string =
        match p with
        | EncodingProfile.V1 -> v1
        | EncodingProfile.V2 -> v2

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
              expect
                  (tag + " / Canonical.write")
                  "Canonical.write"
                  (column p v.V1 v.V2)
                  (Canonical.write (Rendering.ofProfile p) v.Value)

              expect
                  (tag + " / Canonical.tryWrite")
                  "Canonical.tryWrite"
                  (column p v.V1 v.V2)
                  (match Canonical.tryWrite (Rendering.ofProfile p) v.Value with
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
                      Canonical.write (Rendering.ofProfile other) v.Value <> mine
                      && Canonical.isCanonical (Rendering.ofProfile p) mine
                      && not (Canonical.isCanonical (Rendering.ofProfile other) mine)
                  then
                      pass name
                  else
                      fail
                          name
                          (sprintf
                              "%s rendered %s over %s"
                              (EncodingProfile.name other)
                              (Canonical.write (Rendering.ofProfile other) v.Value)
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
          // Phase 520 — the cross-host renderings, each held to its own pinned column: written, guarded
          // and recognised as canonical by the one contract the store profiles are.
          for label, r, col in
              [ "CrossHost", Rendering.CrossHost, v.CrossHost
                "Declared", Rendering.Declared, v.Declared ] do
              let tag = v.Name + " / " + label
              expect (tag + " / Canonical.write") "Canonical.write" col (Canonical.write r v.Value)

              expect
                  (tag + " / Canonical.tryWrite")
                  "Canonical.tryWrite"
                  col
                  (match Canonical.tryWrite r v.Value with
                   | Ok t -> t
                   | Error e -> "refused: " + e)

              (let name = tag + " / Canonical.isCanonical accepts its own column"

               if Canonical.isCanonical r col then
                   pass name
               else
                   fail name (sprintf "%s is not canonical under %s" col label))
          // the positive control: the cross-host rendering refuses the V2 text as canonical exactly
          // where the two columns differ — a member out of Ordinal order, or a float the canonical
          // rule spells differently — so the column is a different rendering, not a copy of V2's
          (let name =
              v.Name
              + " / positive control: CrossHost refuses the V2 text exactly where the columns differ"

           if (v.CrossHost <> v.V2) = not (Canonical.isCanonical Rendering.CrossHost v.V2) then
               pass name
           else
               fail name (sprintf "CrossHost %s; V2 %s" v.CrossHost v.V2))
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

    /// Every check one actor vector makes: the actor's spelling, the chain payload the profile's config
    /// builds around it, and the node id `Dag.nodeId` folds it into over that config, under each profile.
    let private runActor (a: ActorVector) : Corpus.Outcome list =
        [ for wp in EncodingProfile.all do
              let tag = a.Name + " / " + EncodingProfile.name wp
              let expected = column wp a.V1 a.V2
              let cfg = configOf wp

              expect (tag + " / StreamConfig.spellActor") "spellActor" expected (StreamConfig.spellActor cfg a.Actor)

              expect
                  (tag + " / StreamConfig payload")
                  "StreamConfig.Payload"
                  (payload expected)
                  (cfg.Payload 0 a.Actor "{}")

              expect
                  (tag + " / Dag.nodeId pre-image")
                  "nodeId"
                  (nodePreimage expected)
                  (Dag.nodeId (bareStream cfg) [] a.Actor ())
          expect (a.Name + " / Actor.encode is V2") "Actor.encode" a.V2 (Actor.encode a.Actor) ]

    /// The checks no single vector owns: the DAG's parents are sorted into a profiled id exactly as into
    /// an unprofiled one; the capture
    /// pre-image each profile hashes; and guard parity — the canonical writer refuses, under every
    /// profile, each value the other guarded renderers refuse (Phase 495).
    let private runShared () : Corpus.Outcome list =
        [ for wp in EncodingProfile.all do
              expect
                  ("merge pre-image / " + EncodingProfile.name wp)
                  "nodeId"
                  ("p1,p2" + nodePreimage """{"kind":"human","id":"alice"}""")
                  (Dag.nodeId (bareStream (configOf wp)) [ "p2"; "p1" ] (Human "alice") ())
          for wp in EncodingProfile.all do
              let expected = capturePreimage wp

              let capture: EffectCapture =
                  { Seq = 0
                    Eff = captureEff
                    Determinism = captureDet
                    Value = "\"v\""
                    PrevHash = ""
                    Hash = "#" + expected }

              let name = "capture pre-image / " + EncodingProfile.name wp

              match EffectCapture.firstCaptureBreak (configOf wp) reveal [ capture ] with
              | None -> pass name
              | Some b -> fail name (sprintf "the capture did not recompute: %A" b)
          // Phase 495 — guard parity. The three values no profile renders as text that parses back,
          // one per refused class; the canonical writer refuses each under every profile, as the other
          // two guarded renderers do. Not rows of the rendered table: that pins the renderings of
          // values that parse, and these have none. The repeated member is the class `tryWrite` let
          // through until Phase 495, so a store's id could be minted over text no reader accepts.
          for p in EncodingProfile.all do
              for label, value in
                  [ "a repeated member", JObj [ "a", JInt 1; "a", JInt 2 ]
                    "a non-finite float", JArr [ JFloat nan ]
                    "an ill-formed string", JStr(string (char 0xD800)) ] do
                  let name =
                      "guard parity / "
                      + EncodingProfile.name p
                      + " / "
                      + label
                      + " is refused by every guarded writer"

                  let r = Rendering.ofProfile p

                  match
                      Canonical.tryWrite r value, Json.tryRender value, Canonical.tryWrite Rendering.CrossHost value
                  with
                  | Error _, Error _, Error _ when not (Canonical.isCanonical r (Canonical.write r value)) -> pass name
                  | w, j, c -> fail name (sprintf "Canonical.tryWrite %A; Json.tryRender %A; CrossHost %A" w j c) ]

    /// One row of the family (Phase 508): a value the profiles render, or an actor they fold.
    type internal Entry =
        /// A value vector, checked by `runVector`.
        | Rendered of Vector
        /// An actor vector, checked under each profile's encoding, chain payload and node id.
        | Folded of ActorVector

    let private entries (vs: Vector list) (actors: ActorVector list) : Entry list =
        List.map Rendered vs @ List.map Folded actors

    /// The capture line `lines` ends with: owned by no vector, it is the profiles' own pre-image.
    let private captureLine =
        "capture: effect TAB, determinism LF\t"
        + capturePreimage EncodingProfile.V1
        + "\t"
        + capturePreimage EncodingProfile.V2

    /// The family over one shape (Phase 508): the value vectors then the actor vectors, each line
    /// `name<TAB>V1<TAB>V2` (`actor: name<TAB>V1<TAB>V2` for an actor). The checks no vector owns —
    /// `runShared` — and the capture line are the family's own and ride beside the generic verbs.
    let internal family: VectorFamily<Entry> =
        { Label = "encoding profile"
          Corpus = entries vectors actorVectors
          Name =
            function
            | Rendered v -> v.Name
            | Folded a -> "actor " + a.Name
          Check =
            function
            | Rendered v -> runVector v
            | Folded a -> runActor a
          Line =
            function
            | Rendered v -> v.Name + "\t" + v.V1 + "\t" + v.V2
            | Folded a -> "actor: " + a.Name + "\t" + a.V1 + "\t" + a.V2 }

    /// Run the whole family.
    let run () : Corpus.Outcome list = VectorFamily.run family @ runShared ()

    /// The whole family as a single verdict — `Ok` or the first failing check, named.
    let check () : Result<unit, string> = VectorFamily.firstFailure (run ())

    /// The table as lines a host in another language can diff: `name<TAB>V1<TAB>V2` per value
    /// vector, then `actor: name<TAB>V1<TAB>V2` per actor, then `capture<TAB>V1<TAB>V2` — the bytes
    /// each profile renders or folds, as the TABLE states them. Deterministic; no seed.
    let lines () : string list =
        VectorFamily.lines family @ [ captureLine ]

    /// The family as `LawResult`s over a table the caller hands it (Phase 390; `laws ()` is this over
    /// the committed `vectors` and `actorVectors`): one law per value vector, per actor vector and for
    /// the shared checks, green exactly when every check it makes passes; one law on the FORMAT `lines`
    /// renders (three tab-separated fields, no field carrying a tab or a line break); and the corpus
    /// law, whose evidence is one assertion per vector evaluated, so a run handed no vectors is red by
    /// name (`encoding profile: the corpus evaluated at least one vector`). The shared checks run on
    /// every call — they are the profiles' own, not the table's — and are not counted as a vector.
    /// `VectorFamily.lawsWith` over `family` since Phase 508.
    let lawsWith (vs: Vector list) (actors: ActorVector list) : LawResult list =
        let table = VectorFamily.over (entries vs actors) family

        let format =
            LawKit.LawCell "lines () renders the table as name, V1 and V2, tab-separated, in table order, one line each"

        let rendered = VectorFamily.lines table @ [ captureLine ]

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

        let shared =
            let cell = LawKit.LawCell "encoding profile: the shared checks"

            match VectorFamily.firstFailure (runShared ()) with
            | Error e -> cell.Check(false, (fun () -> e))
            | Ok() -> cell.Saw()

            cell.Result

        VectorFamily.lawsWith [ shared; format.Result ] table
        |> LawKit.labelAs "EncodingProfileVectors.lawsWith"

    /// The family as `LawResult`s, on the `StringEscapeVectors.laws` precedent — `lawsWith vectors
    /// actorVectors`. The committed rendering is `conformance/encoding/encoding-profiles.json`.
    let laws () : LawResult list =
        lawsWith vectors actorVectors |> LawKit.labelAs "EncodingProfileVectors.laws"

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
    ///
    /// Spelled `…At` since Phase 507: the family takes the domain's own `Codec<'T>`, a witness
    /// capability the base contract does not demand (D127; `Families.Takes.Codec`).
    let storedCodecLawsAt (declared: string) (codec: Codec<'T>) (stored: string list) : LawResult list =
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
                        |> Result.bind (Canonical.tryWrite (Rendering.ofProfile p))
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
                                  let moved =
                                      Canonical.write (Rendering.ofProfile o) j
                                      <> Canonical.write (Rendering.ofProfile p) j

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
        |> LawKit.labelAs "EncodingProfileVectors.storedCodecLawsAt"
