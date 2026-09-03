namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 700 — the IDL diff classifier + host-strand report.
//
// The stability classification rules are already written and mechanical-shaped
// but hand-applied: `STABILITY.md` (adding a `NodeKind` case = minor; removal /
// `$type` rename = a major wire event) and `VOCABULARY.md` §4 (addition = an
// additive `core@1.(x+1)` profile minor; the host-lag commitment). This module
// applies them to a pair of `idl.json` revisions and emits the **host-strand
// report** — the `WIRE_FORMAT.md` §11 obligation set per host class, derived from
// the §11.0 roster. The charter's §1.3 cost table, computed instead of remembered.
//
// **Advisory, never authoritative.** Nothing here edits a file, bumps a version,
// or gates a build. Its output is text for a phase author to read, argue with,
// and paste a corrected version of. That is deliberate: a classifier that
// auto-applied would make the hand-declared classification unfalsifiable, and the
// retroactive validation (docs/idl-diff-retroactive-validation.md) depends on the
// two being independently produced so a disagreement is visible.
//
// **The input is the ARTIFACT, not the `Idl` record.** `Artifact.render` is a
// lossy-by-design projection: it elides what is not contract (authored ordering
// of sorted collections) and flags what is host-surface rather than wire
// (`hostSurface`). Diffing the artifact therefore diffs exactly the published
// contract, and works across revisions whose F# vocabulary no longer compiles —
// which is the whole point of having committed the artifact.
// ---------------------------------------------------------------------------

module Diff =

    // -----------------------------------------------------------------------
    // Snapshot — a name-keyed reading of one `idl.json` revision.
    // -----------------------------------------------------------------------

    /// One field of a kind / op / union case / record / the node envelope.
    ///
    /// Three canonical strings rather than one, because they answer three
    /// different questions and conflating them is how a host-surface edit gets
    /// reported as a wire break:
    ///
    /// - `TypeWire` is the field's structural wire type with `hostSurface`
    ///   stripped. A change here is observable by a third-party codec.
    /// - `TypeHost` is the `hostSurface` block alone (empty for every type that
    ///   carries none). A change here is a generated-declaration change and is
    ///   invisible on the wire.
    /// - `Opt` is the optionality class INCLUDING an `omitDefault`'s value,
    ///   which is wire-visible: moving the identity default moves the bytes of
    ///   every document that was sitting on it.
    type FieldSnap =
        {
            Name: string
            Label: string
            /// The type object's own `$type` — `str` / `list` / `hosted` / `json` …
            /// Carried separately because three of them (`hosted`, `json`,
            /// `opaque`) are ERASED slots whose admitted values the artifact
            /// deliberately does not state, which is a classification boundary.
            TypeTag: string
            TypeWire: string
            TypeHost: string
            OptClass: string
            Opt: string
            /// The declared annotation set (Phase 113), canonically rendered — `""`
            /// when the field declares none, which is what every revision predating
            /// the key reads as.
            ///
            /// A FOURTH string beside the three above, for the same reason they are
            /// three: an annotation is neither wire nor host-surface. It changes no
            /// encoding in either direction and no generated SIGNATURE either — it
            /// adds a doc comment and an `Obsolete` attribute — so folding it into
            /// `TypeHost` would report a retirement marking as a signature move.
            Annotations: string
        }

    /// What a field belongs to. `ONodeEnvelope` is the per-node field set beside
    /// `id` / `kind` (`WIRE_FORMAT.md` §3.1) — it has no name of its own.
    type Owner =
        | OKind of tag: string
        | OOp of tag: string
        | OUnionCase of union: string * case: string
        | ORecord of name: string
        | ONodeEnvelope

        member this.Describe =
            match this with
            | OKind t -> sprintf "kind %s" t
            | OOp t -> sprintf "op %s" t
            | OUnionCase(u, c) -> sprintf "union %s.%s" u c
            | ORecord n -> sprintf "record %s" n
            | ONodeEnvelope -> "node envelope"

        /// Sort key — stable across runs, and groups an owner's changes together.
        member this.Key =
            match this with
            | ONodeEnvelope -> "0:"
            | OKind t -> "1:" + t
            | OOp t -> "2:" + t
            | OUnionCase(u, c) -> "3:" + u + "." + c
            | ORecord n -> "4:" + n

    /// One union case — its fields, and (Phase 113) its own annotation set, which
    /// belongs to the CASE rather than to any of its fields.
    type CaseSnap =
        { Fields: FieldSnap list
          Annotations: string }

    type UnionSnap =
        { Params: string list
          Cases: Map<string, CaseSnap>
          CaseOrder: string list
          TransparentCase: string option }

    type EnumSnap =
        {
            WireCases: string list
            HostCases: string list
            /// Per-case annotation sets (Phase 119), keyed by the WIRE string the artifact
            /// keys them on and canonically rendered — a case that says nothing is absent
            /// from the map, which is also how a revision predating the key reads.
            CaseAnnotations: Map<string, string>
        }

    /// One `idl.json` revision, keyed for lookup. Collections the artifact sorts
    /// are read into maps (order carries nothing); field lists keep their
    /// authored order, which the artifact preserves deliberately.
    type Snapshot =
        {
            Version: int
            Kinds: Map<string, FieldSnap list>
            KindCategory: Map<string, string>
            /// A kind's OWN annotation set (Phase 119), canonically rendered — `""` when
            /// it declares none. A map beside [[Kinds]] rather than a field on it, for
            /// the reason [[KindCategory]] is one: the diff walk pairs field LISTS by
            /// tag, and a per-tag fact that is not a field belongs beside that walk
            /// rather than inside it.
            KindAnnotations: Map<string, string>
            Ops: Map<string, FieldSnap list>
            /// A tree-op's own annotation set (Phase 119) — the same slot as
            /// [[KindAnnotations]], read from the `ops` collection. Ops are `IdlKind`s,
            /// so they are annotatable on identical terms.
            OpAnnotations: Map<string, string>
            Unions: Map<string, UnionSnap>
            Enums: Map<string, EnumSnap>
            Records: Map<string, FieldSnap list>
            /// `(kind, field) -> canonical value` — the smart-constructor defaults
            /// (`IdlDefault`), NOT the wire-visible `omitDefault` optionality.
            Defaults: Map<string * string, string>
            NodeFields: FieldSnap list
            /// The declared wire shape (Phases 108/109), as `discriminator/envelope`
            /// — `"$type/nestedKind"` when the artifact predates the key or the
            /// vocabulary declares the default.
            Wire: string
            /// The declared hardening vocabulary (Phase 116), rendered canonically —
            /// the default block when the artifact predates the key or the vocabulary
            /// declares the default, so the two read alike, which is what they mean.
            ///
            /// Carried as ONE string rather than a member-per-field for the reason
            /// [[Wire]] is: the classifier's job here is to say the declaration moved
            /// and why that matters, and the WIRE consequence of the only wire-visible
            /// member is already reported per union as `UnionTransparencyChanged`.
            Harden: string
        }

    // -----------------------------------------------------------------------
    // Reading the artifact.
    // -----------------------------------------------------------------------

    let private field (name: string) (v: JVal) : JVal option =
        match v with
        | JObj fs -> fs |> List.tryPick (fun (k, fv) -> if k = name then Some fv else None)
        | _ -> None

    let private str (name: string) (v: JVal) : string option =
        match field name v with
        | Some(JStr s) -> Some s
        | _ -> None

    let private arr (name: string) (v: JVal) : JVal list =
        match field name v with
        | Some(JArr xs) -> xs
        | _ -> []

    /// The type object with its `hostSurface` key removed — the wire-observable
    /// part. Recursive, because a `list<fn>` hides one a level down.
    let rec private stripHostSurface (v: JVal) : JVal =
        match v with
        | JObj fs ->
            fs
            |> List.filter (fun (k, _) -> k <> "hostSurface")
            |> List.map (fun (k, fv) -> k, stripHostSurface fv)
            |> JObj
        | JArr xs -> JArr(xs |> List.map stripHostSurface)
        | scalar -> scalar

    /// Only the `hostSurface` blocks, keyed by the path they sit at — so a
    /// change to a nested closure's F# signature is still attributable.
    let rec private hostSurfaceOnly (path: string) (v: JVal) : (string * JVal) list =
        match v with
        | JObj fs ->
            [ for (k, fv) in fs do
                  if k = "hostSurface" then
                      yield path, fv
                  else
                      yield! hostSurfaceOnly (path + "/" + k) fv ]
        | JArr xs ->
            xs
            |> List.mapi (fun i x -> hostSurfaceOnly (sprintf "%s/%d" path i) x)
            |> List.concat
        | _ -> []

    /// A readable one-line rendering of a field type. Display only — every
    /// equality decision runs on the canonical strings, so an unrecognised
    /// shape here degrades to raw JSON rather than to a wrong verdict.
    let rec private typeLabel (v: JVal) : string =
        let tag = str "$type" v |> Option.defaultValue "?"

        let named () = str "name" v |> Option.defaultValue "?"

        match tag with
        | "str"
        | "int"
        | "bool"
        | "float"
        | "node"
        | "kind"
        | "op"
        | "json"
        | "closure"
        | "opaque" -> tag
        | "enum" -> "enum " + named ()
        | "record" -> "record " + named ()
        | "var" -> "'" + named ()
        | "union" ->
            match arr "args" v with
            | [] -> "union " + named ()
            | args -> sprintf "union %s<%s>" (named ()) (args |> List.map typeLabel |> String.concat ", ")
        | "list" ->
            match field "of" v with
            | Some inner -> sprintf "list<%s>" (typeLabel inner)
            | None -> "list<?>"
        | "map" ->
            match field "values" v with
            | Some inner -> sprintf "map<string, %s>" (typeLabel inner)
            | None -> "map<string, ?>"
        | "fn" ->
            match field "hostSurface" v |> Option.bind (str "fsharp") with
            | Some sg -> sprintf "fn(%s)" sg
            | None -> "fn"
        | "hosted" ->
            match field "hostSurface" v |> Option.bind (str "fsharp") with
            | Some h -> sprintf "hosted(%s)" h
            | None -> "hosted"
        | other -> other

    let private optLabel (v: JVal) : string =
        match str "$type" v with
        | Some "omitDefault" ->
            match field "default" v with
            | Some d -> "omitDefault " + Canon.render d
            | None -> "omitDefault"
        | Some t -> t
        | None -> "?"

    /// The annotation set as a canonical string — `""` when the key is absent,
    /// which is both "declares none" and "this revision predates the key". The two
    /// are deliberately indistinguishable: an artifact written before Phase 113
    /// describes a vocabulary that annotated nothing, so reading them alike is
    /// correct rather than merely convenient.
    let private readAnnotations (v: JVal) : string =
        match field "annotations" v with
        | Some a -> Canon.render a
        | None -> ""

    let private readField (v: JVal) : FieldSnap option =
        match str "name" v, field "type" v, field "optionality" v with
        | Some name, Some ty, Some opt ->
            Some
                { Name = name
                  Label = typeLabel ty
                  TypeTag = str "$type" ty |> Option.defaultValue "?"
                  TypeWire = Canon.render (stripHostSurface ty)
                  TypeHost =
                    hostSurfaceOnly "" ty
                    |> List.map (fun (p, h) -> p + "=" + Canon.render h)
                    |> String.concat "; "
                  OptClass = str "$type" opt |> Option.defaultValue "?"
                  Opt = optLabel opt
                  Annotations = readAnnotations v }
        | _ -> None

    let private readFields (owner: JVal) : FieldSnap list =
        arr "fields" owner |> List.choose readField

    let private byName (key: JVal -> string option) (project: JVal -> 'a) (xs: JVal list) : Map<string, 'a> =
        xs
        |> List.choose (fun x -> key x |> Option.map (fun k -> k, project x))
        |> Map.ofList

    /// Read one `idl.json` revision. Tolerant of keys the revision predates
    /// (`ops`, `hostCases`, `transparentCase` are all emitted conditionally) —
    /// their absence is read as empty, which is what the emitter means by it.
    let snapshot (artifact: JVal) : Result<Snapshot, string> =
        match artifact with
        | JObj _ ->
            let version =
                match field "version" artifact with
                | Some(JInt i) -> i
                | _ -> 0

            let kinds = arr "kinds" artifact
            let ops = arr "ops" artifact

            Ok
                { Version = version
                  Kinds = kinds |> byName (str "tag") readFields
                  KindCategory =
                    kinds
                    |> byName (str "tag") (fun k -> str "category" k |> Option.defaultValue "")
                  KindAnnotations = kinds |> byName (str "tag") readAnnotations
                  Ops = ops |> byName (str "tag") readFields
                  OpAnnotations = ops |> byName (str "tag") readAnnotations
                  Unions =
                    arr "unions" artifact
                    |> byName (str "name") (fun u ->
                        let cases = arr "cases" u

                        { Params =
                            match field "params" u with
                            | Some(JArr ps) ->
                                ps
                                |> List.choose (function
                                    | JStr s -> Some s
                                    | _ -> None)
                            | _ -> []
                          Cases =
                            cases
                            |> byName (str "tag") (fun c ->
                                { Fields = readFields c
                                  Annotations = readAnnotations c })
                          CaseOrder = cases |> List.choose (str "tag")
                          TransparentCase = str "transparentCase" u })
                  Enums =
                    arr "enums" artifact
                    |> byName (str "name") (fun e ->
                        let strings key =
                            arr key e
                            |> List.choose (function
                                | JStr s -> Some s
                                | _ -> None)

                        { WireCases = strings "cases"
                          HostCases = strings "hostCases"
                          CaseAnnotations =
                            match field "caseAnnotations" e with
                            | Some(JObj entries) ->
                                entries
                                |> List.map (fun (wire, block) -> wire, Canon.render block)
                                |> Map.ofList
                            | _ -> Map.empty })
                  Records = arr "records" artifact |> byName (str "name") readFields
                  Defaults =
                    arr "defaults" artifact
                    |> List.choose (fun d ->
                        match str "kind" d, str "field" d, field "value" d with
                        | Some k, Some f, Some v -> Some((k, f), Canon.render v)
                        | _ -> None)
                    |> Map.ofList
                  NodeFields = arr "nodeFields" artifact |> List.choose readField
                  Wire =
                    match field "wire" artifact with
                    | Some w ->
                        (str "discriminator" w |> Option.defaultValue "$type")
                        + "/"
                        + (str "nodeEnvelope" w |> Option.defaultValue "nestedKind")
                        + "/"
                        + (str "keyOrder" w |> Option.defaultValue "sorted")
                    | None -> "$type/nestedKind/sorted"
                  Harden =
                    match field "harden" artifact with
                    | Some h ->
                        [ "gatedKind"
                          "placeholderKind"
                          "placeholderField"
                          "textLiteralCase"
                          "textLiteralField"
                          "valueLiteralCase"
                          "valueLiteralField" ]
                        |> List.map (fun k -> str k h |> Option.defaultValue "")
                        |> String.concat "/"
                        |> fun tokens ->
                            tokens
                            + "/"
                            + (arr "transparentUnions" h
                               |> List.map (fun e ->
                                   (str "union" e |> Option.defaultValue "")
                                   + "."
                                   + (str "case" e |> Option.defaultValue ""))
                               |> String.concat ",")
                    | None -> "Custom/Markdown/text/Literal/text/Static/value/TextSource.Literal" }
        | _ -> Error "idl.json: expected a JSON object at the root"

    /// Parse + read in one step.
    let parse (text: string) : Result<Snapshot, string> = Json.parse text |> Result.bind snapshot

    // -----------------------------------------------------------------------
    // The change list.
    // -----------------------------------------------------------------------

    type Change =
        | ArtifactVersionChanged of before: int * after: int
        /// The declared wire shape moved (Phases 108/109) — `discriminator/envelope`.
        | WireShapeChanged of before: string * after: string
        /// The declared HARDENING vocabulary moved (Phase 116) — which kind the codegen
        /// trust boundary gates, what it mints in its place, which cases it sanitises,
        /// and which unions have a transparent case.
        | HardenPolicyChanged of before: string * after: string
        | KindAdded of tag: string
        | KindRemoved of tag: string
        /// Inferred, never declared — see `renamePairs`. Reported ALONGSIDE the
        /// add + remove it explains, not instead of them.
        | KindRenamed of before: string * after: string
        | KindCategoryChanged of tag: string * before: string * after: string
        | OpAdded of tag: string
        | OpRemoved of tag: string
        | UnionAdded of name: string
        | UnionRemoved of name: string
        | UnionCaseAdded of union: string * case: string
        | UnionCaseRemoved of union: string * case: string
        | UnionParamsChanged of name: string * before: string list * after: string list
        | UnionTransparencyChanged of name: string * before: string option * after: string option
        | EnumAdded of name: string
        | EnumRemoved of name: string
        | EnumCaseAdded of enumName: string * wire: string
        | EnumCaseRemoved of enumName: string * wire: string
        | EnumHostMappingChanged of name: string * before: string list * after: string list
        | RecordAdded of name: string
        | RecordRemoved of name: string
        | FieldAdded of Owner * FieldSnap
        | FieldRemoved of Owner * name: string * was: FieldSnap
        | FieldTypeChanged of Owner * name: string * before: FieldSnap * after: FieldSnap
        /// The generated DECLARATION moved; the wire did not. `TFn`'s F#
        /// signature, a `THosted` slot's codec expressions.
        | FieldHostSurfaceChanged of Owner * name: string * before: string * after: string
        | FieldOptionalityChanged of Owner * name: string * before: FieldSnap * after: FieldSnap
        /// The declared ANNOTATIONS on a field moved (Phase 113) — `""` on either
        /// side means "declared none". Never a wire event: an annotation changes no
        /// encoding in either direction.
        | FieldAnnotationsChanged of Owner * name: string * before: string * after: string
        /// The declared annotations on a union CASE moved (Phase 113).
        | UnionCaseAnnotationsChanged of union: string * case: string * before: string * after: string
        /// The declared annotations on a KIND or a tree-OP itself moved (Phase 119) —
        /// the vocabulary-growth charter's retirement half, and the one marking that can
        /// say a whole node kind is going away.
        ///
        /// Carried on [[Owner]], which is always `OKind` or `OOp` here — the same
        /// subject vocabulary [[FieldAnnotationsChanged]] already uses, so "kind X" and
        /// "op X" read alike wherever a change is described.
        | KindAnnotationsChanged of Owner * before: string * after: string
        /// The declared annotations on an ENUM CASE moved (Phase 119). `wire` is the
        /// case's wire string, which is how the artifact keys them and what a
        /// third-party reader sees.
        | EnumCaseAnnotationsChanged of enumName: string * wire: string * before: string * after: string
        | DefaultAdded of kind: string * field: string * value: string
        | DefaultRemoved of kind: string * field: string * value: string
        | DefaultChanged of kind: string * field: string * before: string * after: string

    /// The addition and removal of a field are a `FieldOptionalityChanged` seen
    /// from too far away only when the name matches; everything else pairs by
    /// name too, so field diffing is one shared walk.
    let private diffFields (owner: Owner) (before: FieldSnap list) (after: FieldSnap list) : Change list =
        let b = before |> List.map (fun f -> f.Name, f) |> Map.ofList
        let a = after |> List.map (fun f -> f.Name, f) |> Map.ofList

        [ for f in after do
              match Map.tryFind f.Name b with
              | None -> yield FieldAdded(owner, f)
              | Some old ->
                  if old.TypeWire <> f.TypeWire then
                      yield FieldTypeChanged(owner, f.Name, old, f)
                  elif old.TypeHost <> f.TypeHost then
                      yield FieldHostSurfaceChanged(owner, f.Name, old.TypeHost, f.TypeHost)

                  if old.Opt <> f.Opt then
                      yield FieldOptionalityChanged(owner, f.Name, old, f)

                  if old.Annotations <> f.Annotations then
                      yield FieldAnnotationsChanged(owner, f.Name, old.Annotations, f.Annotations)

          for f in before do
              if not (Map.containsKey f.Name a) then
                  yield FieldRemoved(owner, f.Name, f) ]

    let private diffNamed
        (added: string -> Change)
        (removed: string -> Change)
        (inner: string -> 'a -> 'a -> Change list)
        (before: Map<string, 'a>)
        (after: Map<string, 'a>)
        : Change list =
        [ for KeyValue(name, av) in after do
              match Map.tryFind name before with
              | None -> yield added name
              | Some bv -> yield! inner name bv av

          for KeyValue(name, _) in before do
              if not (Map.containsKey name after) then
                  yield removed name ]

    /// Rename inference — a removed name and an added name whose wire-observable
    /// field signature is IDENTICAL, and which pair uniquely on both sides.
    ///
    /// Deliberately conservative and deliberately additional. There is nothing in
    /// the artifact that records a rename (the wire has no identity beyond the
    /// `$type` string), so any detection is a guess about intent; a wrong guess
    /// that SUPPRESSED the add + remove would hide a breaking change behind a
    /// friendlier-sounding one. So a rename is reported beside them, and the
    /// classification of the pair is unaffected by whether the guess landed.
    let private renamePairs
        (make: string * string -> Change)
        (before: Map<string, FieldSnap list>)
        (after: Map<string, FieldSnap list>)
        : Change list =
        let sign (fs: FieldSnap list) =
            fs
            |> List.map (fun f -> f.Name + ":" + f.TypeWire + ":" + f.Opt)
            |> String.concat "|"

        let gone =
            [ for KeyValue(n, fs) in before do
                  if not (Map.containsKey n after) then
                      yield n, sign fs ]

        let fresh =
            [ for KeyValue(n, fs) in after do
                  if not (Map.containsKey n before) then
                      yield n, sign fs ]

        [ for (oldName, s) in gone do
              // Unique on both sides, and no empty-signature pairing: a
              // field-less kind matches every other field-less kind, which is a
              // coincidence, not a rename.
              if s <> "" then
                  match fresh |> List.filter (fun (_, fs) -> fs = s) with
                  | [ (newName, _) ] when (gone |> List.filter (fun (_, bs) -> bs = s) |> List.length) = 1 ->
                      yield make (oldName, newName)
                  | _ -> () ]

    /// Deterministic ordering. Sorted by a per-case rank then by the change's own
    /// key, so identical inputs produce byte-identical output regardless of map
    /// enumeration order.
    let private sortKey (c: Change) : string * string =
        let k rank key = (rank: string), (key: string)

        match c with
        | ArtifactVersionChanged _ -> k "00" ""
        | WireShapeChanged _ -> k "01" ""
        | HardenPolicyChanged _ -> k "02" ""
        | KindAdded t -> k "10" t
        | KindRemoved t -> k "11" t
        | KindRenamed(o, n) -> k "12" (o + ">" + n)
        | KindCategoryChanged(t, _, _) -> k "13" t
        | KindAnnotationsChanged(o, _, _) -> k "14" o.Key
        | OpAdded t -> k "20" t
        | OpRemoved t -> k "21" t
        | UnionAdded n -> k "30" n
        | UnionRemoved n -> k "31" n
        | UnionCaseAdded(u, c) -> k "32" (u + "." + c)
        | UnionCaseRemoved(u, c) -> k "33" (u + "." + c)
        | UnionParamsChanged(n, _, _) -> k "34" n
        | UnionTransparencyChanged(n, _, _) -> k "35" n
        | EnumAdded n -> k "40" n
        | EnumRemoved n -> k "41" n
        | EnumCaseAdded(e, w) -> k "42" (e + "." + w)
        | EnumCaseRemoved(e, w) -> k "43" (e + "." + w)
        | EnumHostMappingChanged(n, _, _) -> k "44" n
        | EnumCaseAnnotationsChanged(e, w, _, _) -> k "45" (e + "." + w)
        | RecordAdded n -> k "50" n
        | RecordRemoved n -> k "51" n
        | FieldAdded(o, f) -> k "60" (o.Key + "/" + f.Name)
        | FieldRemoved(o, n, _) -> k "61" (o.Key + "/" + n)
        | FieldTypeChanged(o, n, _, _) -> k "62" (o.Key + "/" + n)
        | FieldOptionalityChanged(o, n, _, _) -> k "63" (o.Key + "/" + n)
        | FieldHostSurfaceChanged(o, n, _, _) -> k "64" (o.Key + "/" + n)
        | FieldAnnotationsChanged(o, n, _, _) -> k "65" (o.Key + "/" + n)
        | UnionCaseAnnotationsChanged(u, c, _, _) -> k "66" (u + "." + c)
        | DefaultAdded(kd, f, _) -> k "70" (kd + "/" + f)
        | DefaultRemoved(kd, f, _) -> k "71" (kd + "/" + f)
        | DefaultChanged(kd, f, _, _) -> k "72" (kd + "/" + f)

    let changes (before: Snapshot) (after: Snapshot) : Change list =
        let unordered =
            [ if before.Version <> after.Version then
                  ArtifactVersionChanged(before.Version, after.Version)

              if before.Wire <> after.Wire then
                  WireShapeChanged(before.Wire, after.Wire)

              if before.Harden <> after.Harden then
                  HardenPolicyChanged(before.Harden, after.Harden)

              yield!
                  diffNamed KindAdded KindRemoved (fun tag b a -> diffFields (OKind tag) b a) before.Kinds after.Kinds

              yield! renamePairs KindRenamed before.Kinds after.Kinds

              for KeyValue(tag, cat) in after.KindCategory do
                  match Map.tryFind tag before.KindCategory with
                  | Some old when old <> cat -> KindCategoryChanged(tag, old, cat)
                  | _ -> ()

              // Phase 119 — a kind's own annotations, reported only for a tag both
              // revisions carry: a kind that arrived or left is already `KindAdded` /
              // `KindRemoved`, and saying it also gained annotations adds nothing.
              for KeyValue(tag, ann) in after.KindAnnotations do
                  match Map.tryFind tag before.KindAnnotations with
                  | Some old when old <> ann -> KindAnnotationsChanged(OKind tag, old, ann)
                  | _ -> ()

              yield! diffNamed OpAdded OpRemoved (fun tag b a -> diffFields (OOp tag) b a) before.Ops after.Ops

              for KeyValue(tag, ann) in after.OpAnnotations do
                  match Map.tryFind tag before.OpAnnotations with
                  | Some old when old <> ann -> KindAnnotationsChanged(OOp tag, old, ann)
                  | _ -> ()

              yield!
                  diffNamed
                      UnionAdded
                      UnionRemoved
                      (fun name b a ->
                          [ if b.Params <> a.Params then
                                UnionParamsChanged(name, b.Params, a.Params)

                            if b.TransparentCase <> a.TransparentCase then
                                UnionTransparencyChanged(name, b.TransparentCase, a.TransparentCase)

                            yield!
                                diffNamed
                                    (fun c -> UnionCaseAdded(name, c))
                                    (fun c -> UnionCaseRemoved(name, c))
                                    (fun c bf af ->
                                        [ if bf.Annotations <> af.Annotations then
                                              UnionCaseAnnotationsChanged(name, c, bf.Annotations, af.Annotations)

                                          yield! diffFields (OUnionCase(name, c)) bf.Fields af.Fields ])
                                    b.Cases
                                    a.Cases ])
                      before.Unions
                      after.Unions

              yield!
                  diffNamed
                      EnumAdded
                      EnumRemoved
                      (fun name b a ->
                          [ for w in a.WireCases do
                                if not (List.contains w b.WireCases) then
                                    EnumCaseAdded(name, w)

                            for w in b.WireCases do
                                if not (List.contains w a.WireCases) then
                                    EnumCaseRemoved(name, w)

                            if b.HostCases <> a.HostCases then
                                EnumHostMappingChanged(name, b.HostCases, a.HostCases)

                            // Phase 119 — per-case annotations, over the cases both
                            // revisions carry. A case that arrived or left is already
                            // `EnumCaseAdded` / `EnumCaseRemoved`; an absent entry on
                            // either side reads as `""`, so a first marking and a full
                            // withdrawal both surface here, which is what the classifier
                            // grades `Additive` and `HostSurfaceOnly` respectively.
                            for w in a.WireCases do
                                if List.contains w b.WireCases then
                                    let bw = b.CaseAnnotations |> Map.tryFind w |> Option.defaultValue ""
                                    let aw = a.CaseAnnotations |> Map.tryFind w |> Option.defaultValue ""

                                    if bw <> aw then
                                        EnumCaseAnnotationsChanged(name, w, bw, aw) ])
                      before.Enums
                      after.Enums

              yield!
                  diffNamed
                      RecordAdded
                      RecordRemoved
                      (fun name b a -> diffFields (ORecord name) b a)
                      before.Records
                      after.Records

              yield! diffFields ONodeEnvelope before.NodeFields after.NodeFields

              for KeyValue((kd, f), v) in after.Defaults do
                  match Map.tryFind (kd, f) before.Defaults with
                  | None -> DefaultAdded(kd, f, v)
                  | Some old when old <> v -> DefaultChanged(kd, f, old, v)
                  | Some _ -> ()

              for KeyValue((kd, f), v) in before.Defaults do
                  if not (Map.containsKey (kd, f) after.Defaults) then
                      DefaultRemoved(kd, f, v) ]

        unordered |> List.sortBy sortKey

    // -----------------------------------------------------------------------
    // Classification — `STABILITY.md` + `VOCABULARY.md` §4, applied.
    // -----------------------------------------------------------------------

    type Severity =
        /// Every previously-valid document stays valid and every
        /// previously-conformant emitter stays conformant.
        | Additive
        /// Old documents still decode, but an emitter written against the old
        /// contract now produces one that does not — the 0.2.0 /
        /// orchestration-0.1.3 lesson. Minor on paper, a break in practice.
        | BreakingForEmitters
        /// A `/v2/` major wire event (`VOCABULARY.md` §4.2): a document that was
        /// valid is not, or its bytes moved.
        | BreakingWire
        /// Not observable on the wire at all — a generated-declaration change.
        | HostSurfaceOnly
        /// **The artifact cannot decide this one.** Reserved for changes that
        /// cross an ERASED slot (`hosted` / `json` / `opaque`), whose admitted
        /// values the artifact deliberately does not state — a `THosted` slot's
        /// content "is the host codec's business, not the schema's" (Idl.fs), so
        /// nothing in `idl.json` says whether the two sides admit the same set.
        ///
        /// This case exists because the Phase 700 retroactive validation found
        /// it: the classifier called Phase 707's `liveRegion` re-model
        /// (`THosted` → `TEnum`) a breaking wire change, and it was not — the
        /// wire strings were already the enum's three and the corpus is
        /// byte-identical either side. Reporting `BREAKING` there is not
        /// conservative, it is wrong, and a classifier that cries wolf on the
        /// commonest kind of IDL tidy-up gets skimmed. Saying "I cannot see
        /// inside that slot, here is what to check" is the honest verdict and
        /// the useful one.
        | Unclassifiable

    type Classification =
        { Change: Change
          Severity: Severity
          Rationale: string
          Citation: string }

    let private describeOpt (f: FieldSnap) = f.Opt

    /// A field added to an existing owner. The optionality class decides
    /// everything: `required` is the one that breaks emitters, and it is the one
    /// most likely to be declared additive by hand.
    let private classifyFieldAdd (owner: Owner) (f: FieldSnap) =
        match f.OptClass with
        | "required" ->
            BreakingForEmitters,
            sprintf
                "a REQUIRED field added to %s — an emitter built against the previous contract omits it and now produces an invalid document. Additive for DECODERS, breaking for emitters; do not declare this `additive`."
                owner.Describe,
            "STABILITY.md wire-format section; the 0.2.0 / orchestration-0.1.3 required-field lesson"
        | "hostOnly" ->
            HostSurfaceOnly,
            sprintf
                "a host-only field added to %s — never on the wire in either direction (WIRE_FORMAT.md §9), so no document changes."
                owner.Describe,
            "WIRE_FORMAT.md §9 (wire-omitted fields by design)"
        | _ ->
            Additive,
            sprintf
                "an %s field added to %s — omitted when absent, so every existing document is byte-unchanged and stays valid."
                f.OptClass
                owner.Describe,
            "STABILITY.md: \"a new optional field that is omitted when absent\" is non-breaking"

    /// A declared annotation set moved (Phase 113). Never a wire event in either
    /// direction — the codec does not read annotations, so every document's bytes
    /// are identical either side of any change here.
    ///
    /// **Two verdicts, and the split is the point of the annotation set.** MARKING a
    /// member is `Additive`: nothing that was valid stops being valid, no emitter
    /// that conformed stops conforming, and the generated declaration gains a doc
    /// comment and a warning-grade `Obsolete` attribute that a consumer chooses what
    /// to do about. That is what lets a vocabulary retire a case across two releases
    /// — mark it in one, remove it in the next — without the MARKING itself costing
    /// a breaking bump, which is the retirement path the vocabulary-growth charter
    /// otherwise has no room for.
    ///
    /// Every other move — changing a marking, or withdrawing one — is
    /// `HostSurfaceOnly`: still nothing on the wire, but the generated declaration
    /// moved, which is a recompile event for the reference host (an `Obsolete`
    /// attribute appearing or vanishing changes which warnings a consumer sees) and
    /// invisible to every third-party codec. Withdrawing a `deprecated` is the case
    /// worth naming: an un-retirement is a plain change, not a breaking one.
    let private classifyAnnotations (subject: string) (before: string) (after: string) =
        if before = "" then
            Additive,
            sprintf
                "%s gained declared annotations. An annotation is never on the wire — the codec does not read it — so every existing document is byte-unchanged and every conformant emitter stays conformant. Marking a member is what makes a two-release retirement possible without a breaking bump for the marking itself."
                subject,
            "Idl.Annotations (Phase 113); VOCABULARY.md §4.1 (additive)"
        elif after = "" then
            HostSurfaceOnly,
            sprintf
                "%s had its declared annotations WITHDRAWN. Still nothing on the wire, but the generated declaration moved: the doc comment and any `System.Obsolete` attribute are gone, so a consumer that was being warned no longer is. A plain change — an un-retirement is not a breaking event."
                subject,
            "Idl.Annotations (Phase 113); WIRE_FORMAT.md §13 by the same argument"
        else
            HostSurfaceOnly,
            sprintf
                "%s changed its declared annotations. Nothing on the wire; the generated declaration's doc comment and `System.Obsolete` message moved. A recompile event for the reference host, invisible to every third-party codec."
                subject,
            "Idl.Annotations (Phase 113); WIRE_FORMAT.md §13 by the same argument"

    let classify (c: Change) : Classification =
        let sev, why, cite =
            match c with
            | ArtifactVersionChanged(b, a) ->
                HostSurfaceOnly,
                sprintf
                    "the artifact ENCODING version moved %d → %d. This describes the shape of idl.json itself, not the vocabulary it carries — reconcile the two revisions' encodings before trusting any other row."
                    b
                    a,
                "Artifact.version"

            | WireShapeChanged(b, a) ->
                BreakingWire,
                sprintf
                    "the declared WIRE SHAPE moved %s → %s — the discriminator key, the node-envelope nesting and/or the canonical key order relocate every tag or every byte on the wire, so every document's bytes move. A `/v2/` major event by definition."
                    b
                    a,
                "Idl.WireShape (Phases 108/109/111); VOCABULARY.md §4.2"

            | HardenPolicyChanged(b, a) ->
                HostSurfaceOnly,
                sprintf
                    "the declared HARDENING vocabulary moved %s → %s — the codegen trust boundary now gates a different kind, or mints a different placeholder, or matches a different literal case. Nothing here moves a document's bytes BY ITSELF: the one wire-visible member is the transparent-case set, whose effect is reported per union as its own row. What changes is what SCAFFOLDED source contains, so re-scaffold anything generated against the old declaration."
                    b
                    a,
                "Idl.HardenPolicy (Phase 116); STABILITY.md the IDL engine"

            | KindAdded t ->
                Additive,
                sprintf
                    "kind `%s` added — a new `$type` branch on the schema's top-level `oneOf`; every previously-valid document stays valid."
                    t,
                "VOCABULARY.md §4.1 (additive `core@1.(x+1)` profile minor); STABILITY.md (NodeKind addition = minor)"
            | KindRemoved t ->
                BreakingWire,
                sprintf
                    "kind `%s` REMOVED — retiring a `$type` discriminator invalidates every document that used it. A `/v2/` major event, and per §4.2 a thing to do before publication or not at all."
                    t,
                "VOCABULARY.md §4.2 (removal / rename = a `v2` major)"
            | KindRenamed(o, n) ->
                BreakingWire,
                sprintf
                    "INFERRED rename `%s` → `%s` (identical field signature, unique on both sides). Inference, not a declaration — the wire records no identity beyond the `$type` string. The add + remove above stand on their own; this row only explains them."
                    o
                    n,
                "VOCABULARY.md §4.2 (a `$type` rename is a breaking wire change)"
            | KindCategoryChanged(t, b, a) ->
                HostSurfaceOnly,
                sprintf
                    "kind `%s` re-categorised %s → %s. `Category` is metadata and is never serialised (Idl.IdlKind) — no document changes."
                    t
                    b
                    a,
                "Idl.IdlKind (`Category` is metadata, not serialised)"

            | OpAdded t ->
                Additive,
                sprintf "op `%s` added — a new `$type` branch on the TreeOp union; existing op streams stay valid." t,
                "WIRE_FORMAT.md §3.4; VOCABULARY.md §4.1 by the same additive argument"
            | OpRemoved t ->
                BreakingWire,
                sprintf
                    "op `%s` REMOVED — every persisted op stream carrying it becomes undecodable, and an op stream is a hash-chained archive, not a live message. Strictly worse than retiring a kind."
                    t,
                "VOCABULARY.md §4.2; STABILITY.md (op-stream wire shape)"

            | UnionAdded n ->
                Additive, sprintf "value-union `%s` introduced — reachable only from a field that also changed." n, "—"
            | UnionRemoved n ->
                BreakingWire,
                sprintf "value-union `%s` removed — every document carrying one of its cases is invalidated." n,
                "VOCABULARY.md §4.2"
            | UnionCaseAdded(u, c) ->
                Additive,
                sprintf
                    "case `%s` added to `%s` — a `$type`-discriminator family (WIRE_FORMAT.md §11), so the wire-coupling cost is IDENTICAL to a new kind's; only the confusion cost is smaller. Governed: it still cites §1.1 demand evidence and acknowledges the §11 cost."
                    c
                    u,
                "VOCABULARY.md §2 (the quiet-churn caveat); WIRE_FORMAT.md §11 (discriminator families)"
            | UnionCaseRemoved(u, c) ->
                BreakingWire,
                sprintf "case `%s` REMOVED from `%s` — a retired `$type` in a discriminator family." c u,
                "VOCABULARY.md §4.2"
            | UnionParamsChanged(n, b, a) ->
                HostSurfaceOnly,
                sprintf
                    "`%s` type parameters %A → %A — generic arity is a host-declaration property; the wire carries no type arguments."
                    n
                    b
                    a,
                "Idl.IdlUnion.Params"
            | UnionTransparencyChanged(n, b, a) ->
                BreakingWire,
                sprintf
                    "`%s` transparent case %A → %A — a transparent case encodes as a BARE value rather than a `$type`-tagged object, so this moves the bytes of every document using it."
                    n
                    b
                    a,
                "Idl.TransparentUnion; STABILITY.md wire-format section"

            | EnumAdded n -> Additive, sprintf "closed set `%s` introduced." n, "—"
            | EnumRemoved n ->
                BreakingWire, sprintf "closed set `%s` removed — its field must have changed type or gone." n, "—"
            | EnumCaseAdded(e, w) ->
                Additive,
                sprintf
                    "wire string `\"%s\"` added to closed set `%s` — additive on the wire, but a decoder that predates it REJECTS the value (`UNKNOWN_DU_CASE`), so the host-lag commitment applies exactly as it does to a kind."
                    w
                    e,
                "VOCABULARY.md §4.3 (unknown-discriminator behaviour + host-lag)"
            | EnumCaseRemoved(e, w) ->
                BreakingWire,
                sprintf
                    "wire string `\"%s\"` REMOVED from closed set `%s` — documents carrying it no longer validate."
                    w
                    e,
                "VOCABULARY.md §4.2"
            | EnumHostMappingChanged(n, _, _) ->
                HostSurfaceOnly,
                sprintf
                    "`%s` host case names changed with its wire strings unchanged — `hostCases` is a hostSurface key (WIRE_FORMAT.md §13), carrying nothing observable on the wire. A source-compat event for F# consumers, not a wire one."
                    n,
                "WIRE_FORMAT.md §13; Artifact.json (`hostCases` is hostSurface)"

            | RecordAdded n ->
                Additive,
                sprintf "non-discriminated record `%s` introduced — reachable only from a field that also changed." n,
                "—"
            | RecordRemoved n -> BreakingWire, sprintf "record `%s` removed." n, "VOCABULARY.md §4.2"

            | FieldAdded(owner, f) -> classifyFieldAdd owner f
            | FieldRemoved(owner, n, was) ->
                (match was.OptClass with
                 | "hostOnly" ->
                     HostSurfaceOnly,
                     sprintf
                         "host-only field `%s` removed from %s — never on the wire, so no document changes."
                         n
                         owner.Describe,
                     "WIRE_FORMAT.md §9"
                 | _ ->
                     BreakingWire,
                     sprintf
                         "field `%s` REMOVED from %s — a slot that was on the wire is gone. Decoders that read it break; emitters that write it produce an unknown key."
                         n
                         owner.Describe,
                     "STABILITY.md wire-format section (removal is a major event)")
            | FieldTypeChanged(owner, n, b, a) ->
                let erased t =
                    t = "hosted" || t = "json" || t = "opaque"

                if erased b.TypeTag || erased a.TypeTag then
                    Unclassifiable,
                    sprintf
                        "field `%s` on %s changed type across an ERASED slot: %s → %s. The artifact does not state what a `%s` slot admits — that is the host codec's business, by design — so nothing here can say whether the admitted value sets differ. CHECK: does every value the old side accepted still decode, and does the corpus come back byte-identical? If both, this is a modelling improvement and not a wire event; if either fails, it is BREAKING (wire)."
                        n
                        owner.Describe
                        b.Label
                        a.Label
                        (if erased b.TypeTag then b.TypeTag else a.TypeTag),
                    "Idl.THosted / TJson / TOpaque (content carried verbatim; not described by the artifact)"
                else
                    BreakingWire,
                    sprintf
                        "field `%s` on %s changed type: %s → %s. A value that decoded no longer does."
                        n
                        owner.Describe
                        b.Label
                        a.Label,
                    "STABILITY.md wire-format section"
            | FieldAnnotationsChanged(owner, n, b, a) ->
                classifyAnnotations (sprintf "field `%s` on %s" n owner.Describe) b a
            | UnionCaseAnnotationsChanged(u, c, b, a) -> classifyAnnotations (sprintf "case `%s` of `%s`" c u) b a
            // Phase 119 — the same three grades, for the same reason: a kind-level or
            // enum-case marking is no more on the wire than a field's, so marking a whole
            // node kind for retirement costs no breaking bump and the two-release
            // retirement path the charter needs is affordable.
            | KindAnnotationsChanged(owner, b, a) -> classifyAnnotations owner.Describe b a
            | EnumCaseAnnotationsChanged(e, w, b, a) ->
                classifyAnnotations (sprintf "case `\"%s\"` of enum `%s`" w e) b a

            | FieldHostSurfaceChanged(owner, n, _, _) ->
                HostSurfaceOnly,
                sprintf
                    "field `%s` on %s changed its hostSurface declaration only — the generated F#/TS signature moved, the wire did not. A recompile event for the reference host; invisible to every third-party codec."
                    n
                    owner.Describe,
                "WIRE_FORMAT.md §13 (hostSurface is host-language spec, not wire spec)"
            | FieldOptionalityChanged(owner, n, b, a) ->
                let d = owner.Describe

                (match b.OptClass, a.OptClass with
                 | "optional", "required"
                 | "omitDefault", "required" ->
                     BreakingForEmitters,
                     sprintf
                         "field `%s` on %s became REQUIRED (%s → %s) — an emitter that legitimately omitted it now produces an invalid document."
                         n
                         d
                         (describeOpt b)
                         (describeOpt a),
                     "the 0.2.0 / orchestration-0.1.3 required-field lesson"
                 | "required", _ ->
                     BreakingWire,
                     sprintf
                         "field `%s` on %s stopped being required (%s → %s) — old documents stay valid, but a consumer that relied on presence now faces absence, and the absence is not distinguishable from an old emitter's."
                         n
                         d
                         (describeOpt b)
                         (describeOpt a),
                     "STABILITY.md wire-format section"
                 | "omitDefault", "omitDefault" ->
                     BreakingWire,
                     sprintf
                         "field `%s` on %s moved its identity default (%s → %s) — omit-at-default is WIRE-VISIBLE: every document sitting on the old default changes bytes, and every document carrying the new one loses a key. The single most easily mis-declared change in this table."
                         n
                         d
                         (describeOpt b)
                         (describeOpt a),
                     "Idl.Optionality.OmitDefault (omit-at-default is wire-visible)"
                 | "hostOnly", _
                 | _, "hostOnly" ->
                     BreakingWire,
                     sprintf
                         "field `%s` on %s crossed the host-only boundary (%s → %s) — a slot appeared on, or vanished from, the wire."
                         n
                         d
                         (describeOpt b)
                         (describeOpt a),
                     "WIRE_FORMAT.md §9"
                 | _ ->
                     BreakingWire,
                     sprintf "field `%s` on %s changed optionality (%s → %s)." n d (describeOpt b) (describeOpt a),
                     "STABILITY.md wire-format section")

            | DefaultAdded(kd, f, _) ->
                Additive,
                sprintf
                    "smart-constructor default added for `%s.%s` — an AUTHORING default (Idl.IdlDefault), not the wire-visible omit-at-default. It changes what a host author gets when they say nothing; it does not change what the wire admits."
                    kd
                    f,
                "Idl.IdlDefault (applied by the generated smart constructors)"
            | DefaultRemoved(kd, f, _) ->
                BreakingForEmitters,
                sprintf
                    "smart-constructor default REMOVED for `%s.%s` — host authoring code that relied on it now emits a different document (or fails to compile). Authoring-surface break; the wire contract is unchanged."
                    kd
                    f,
                "Idl.IdlDefault"
            | DefaultChanged(kd, f, b, a) ->
                BreakingForEmitters,
                sprintf
                    "smart-constructor default for `%s.%s` moved (%s → %s) — every authoring site that omitted the field now emits a different document. The wire contract is unchanged; the emitted bytes are not."
                    kd
                    f
                    b
                    a,
                "Idl.IdlDefault"

        { Change = c
          Severity = sev
          Rationale = why
          Citation = cite }

    /// The draft `stability_impact:` value — the roadmap front-matter vocabulary
    /// is `additive` / `breaking` / `null`, so this emits one of the first two.
    let internal stabilityImpact (cs: Classification list) : string =
        if
            cs
            |> List.exists (fun c -> c.Severity = BreakingWire || c.Severity = BreakingForEmitters)
        then
            "breaking"
        elif cs |> List.exists (fun c -> c.Severity = Unclassifiable) then
            "additive   ← ONLY IF every unclassifiable row below checks out; `breaking` otherwise"
        else
            "additive"

    /// The wire-profile recommendation. `core@1.x` is the profile-id grammar
    /// (`STABILITY.md` §15 sentinel strings); `/v1/` is the schema `$id` major.
    let internal profileBump (cs: Classification list) : string =
        if cs |> List.exists (fun c -> c.Severity = BreakingWire) then
            "`/v2/` MAJOR — the schema `$id` major segment moves. VOCABULARY.md §4.2 says avoid this after publication; do it pre-launch or not at all."
        elif cs |> List.exists (fun c -> c.Severity = Unclassifiable) then
            "UNDECIDED — at least one change crosses an erased slot the artifact does not describe. `core@1.(x+1)` if the checks below pass; `/v2/` MAJOR if any of them fails."
        elif cs |> List.exists (fun c -> c.Severity = BreakingForEmitters) then
            "`core@1.(x+1)` profile MINOR on paper — but at least one change breaks EMITTERS, so the minor understates it. Treat every downstream emitter as needing a coordinated bump."
        elif cs |> List.exists (fun c -> c.Severity = Additive) then
            "`core@1.(x+1)` profile minor — the `/v1/` major segment does not move (VOCABULARY.md §4.1)."
        else
            "no wire-profile movement — every change is host-surface only."

    // -----------------------------------------------------------------------
    // The host-strand report — `WIRE_FORMAT.md` §11 obligations per host class.
    // -----------------------------------------------------------------------

    type HostRole =
        | CodecHost
        | RenderProjection

    type Host =
        { Id: string
          Language: string
          Role: HostRole }

    /// How firmly an obligation binds. `Check` exists because the honest answer
    /// to several of these is conditional, and a report that stated them as
    /// `Required` would train its reader to skim.
    type Strength =
        | Required
        | Check
        | NotBound

    type Obligation =
        { Surface: string
          Strength: Strength
          Note: string }

    /// The §11.0 roster, hand-declared.
    ///
    /// TODO(roster-anchor): `WIRE_FORMAT.md` §11.0 names
    /// `wire-format-fixtures/manifest.json` as the intended machine-readable
    /// mirror ("until that lands this table is authoritative"). It carries no
    /// `hosts` key yet — `version` / `schema` / `idl` / `description` / `kinds` /
    /// `formFieldKinds` / `fixtures`. `rosterFrom` below reads one the moment it
    /// appears, so landing the anchor retires this list without touching callers.
    let declaredRoster: Host list =
        [ { Id = "fuaran"
            Language = "F#"
            Role = CodecHost }
          { Id = "fuaran-ts"
            Language = "TypeScript"
            Role = CodecHost }
          { Id = "fuaran-py"
            Language = "Python"
            Role = CodecHost }
          { Id = "fuaran-go"
            Language = "Go"
            Role = CodecHost }
          { Id = "fuaran-rs"
            Language = "Rust"
            Role = CodecHost }
          { Id = "fuaran-swift"
            Language = "Swift"
            Role = RenderProjection }
          { Id = "fuaran-kt"
            Language = "Kotlin"
            Role = RenderProjection } ]

    /// Read the roster from a parsed `manifest.json` when it carries one; `None`
    /// when it does not, which is the current state and the reason
    /// `declaredRoster` exists.
    let internal rosterFrom (manifest: JVal) : Host list option =
        match field "hosts" manifest with
        | Some(JArr entries) when not entries.IsEmpty ->
            entries
            |> List.choose (fun e ->
                match str "id" e with
                | Some id ->
                    Some
                        { Id = id
                          Language = str "language" e |> Option.defaultValue "?"
                          Role =
                            match str "role" e with
                            | Some "render-projection" -> RenderProjection
                            | _ -> CodecHost }
                | None -> None)
            |> function
                | [] -> None
                | hs -> Some hs
        | _ -> None

    /// Whether a change touches the wire at all. A host-surface-only change
    /// obliges the reference host's recompile and nothing else in the roster.
    let private touchesWire (c: Classification) =
        match c.Severity with
        | HostSurfaceOnly -> false
        | _ -> true

    /// Does this change alter the NodeKind set? That is the one class that
    /// reaches the authoring veneers, the analyzer vocabulary, the native render
    /// arms and `manifest.kinds`.
    let private isKindSetChange (c: Change) =
        match c with
        | KindAdded _
        | KindRemoved _
        | KindRenamed _ -> true
        | _ -> false

    /// Does it alter a `$type` discriminator family OTHER than NodeKind
    /// (`FormFieldKind`, `ChartKind`, `Binding`, `Action`, `TreeOp` …)?
    let private isFamilyChange (c: Change) =
        match c with
        | UnionCaseAdded _
        | UnionCaseRemoved _
        | UnionTransparencyChanged _
        | OpAdded _
        | OpRemoved _ -> true
        | _ -> false

    let private isEnumSetChange (c: Change) =
        match c with
        | EnumCaseAdded _
        | EnumCaseRemoved _ -> true
        | _ -> false

    /// The obligation set for one change, joined against the roster.
    ///
    /// The two rows worth reading rather than skimming are the veneer rows and
    /// the native render-arm row, because both are conditional and both have
    /// been got wrong in this estate before:
    ///
    /// - Phase 801 recorded that a payload-FIELD addition binds neither the C#
    ///   `Coverage` reflection nor the VB analyzer's `Vocabulary.cs`, because
    ///   both pin `NodeKind`. §11 step 6 nonetheless speaks of "attribute rows",
    ///   so a field change is `Check`, not `NotBound` — the two authorities do
    ///   not quite agree and the phase author is the one who can settle it.
    /// - Swift's `switch` with no `default:` and Kotlin's `when` over a sealed
    ///   type are exhaustiveness ERRORS, so a case added to a family those tiers
    ///   model is a compiler-forced arm (the 745 precedent, restated by 864) —
    ///   but only for the families they actually model, which the artifact does
    ///   not record.
    let obligations (roster: Host list) (c: Classification) : Obligation list =
        let codecHosts = roster |> List.filter (fun h -> h.Role = CodecHost)
        let projections = roster |> List.filter (fun h -> h.Role = RenderProjection)
        let ch = c.Change

        if not (touchesWire c) then
            [ { Surface = "reference host (F#) regeneration"
                Strength = Required
                Note =
                  "host-surface only — regenerate the generated layer and recompile. No codec host, corpus fixture or spec row is obliged." } ]
        else
            [ for h in codecHosts do
                  if h.Id = "fuaran" then
                      { Surface = "codec: fuaran (F#, reference)"
                        Strength = Required
                        Note =
                          "IDL + `--regen-snapshots` + `sync-generated-layer.ps1`, then the policy decoder (`JsonDecode.fs`) and `SchemaGen.fs` — §11 steps 1-3." }
                  else
                      { Surface = sprintf "codec: %s (%s)" h.Id h.Language
                        Strength = Required
                        Note =
                          "encoder + decoder + schema shape, same change-set — §11 step 5; pinned to the corpus by its §11.1 leg." }

              for h in projections do
                  { Surface = sprintf "render arm: %s (%s)" h.Id h.Language
                    Strength = (if isKindSetChange ch then Required else Check)
                    Note =
                      if isKindSetChange ch then
                          "a NodeKind lacking an arm is a BUILD error in the native tier (§11.0 render projections). No codec change — the Rust core owns the codec."
                      else
                          "bound only if this tier models the changed family as a sealed type — Swift's `switch` without `default:` and Kotlin's `when` are exhaustiveness errors, so a modelled family forces an arm (the 745 precedent). Do not soften either host's default-deny to make a suite pass." }

              { Surface = "corpus: wire-format-fixtures fixture"
                Strength = Required
                Note =
                  "§11 step 4 — `--emit-corpus`, and run `Fuaran.UI.Tests` in the same session (the corpus-as-a-set assertions live only there). The corpus is its own repo: commit and PUSH it with the codec commits." }

              { Surface = "schema: schema.json"
                Strength = Required
                Note = "regenerated by the same `--emit-corpus` command; the stale-schema guard fails if it is skipped." }

              { Surface = "artifact: idl.json"
                Strength = Required
                Note =
                  "re-render the vocabulary artifact beside the vocabulary it projects; the domain's regenerate-and-byte-compare guard fails when the committed artifact and a fresh emission disagree." }

              if isKindSetChange ch then
                  { Surface = "veneer: C# fluent factory (Fuaran.UI.CSharp)"
                    Strength = Required
                    Note =
                      "§11 step 6 — a factory + options record for the kind. The coverage-vs-corpus test fires the moment step 4's fixture lands." }

                  { Surface = "veneer: VB XML-literal mapping (Fuaran.UI.VisualBasic)"
                    Strength = Required
                    Note = "§11 step 6 — an element registration driving that factory." }

                  { Surface = "analyzer: VB Vocabulary.cs"
                    Strength = Required
                    Note =
                      "§11 step 6 — the kind name + attribute rows. Mind the pin's blind spot: a kind missing from BOTH the translator and the analyzer keeps the vocabulary-pin test green." }

                  { Surface = "manifest: manifest.kinds"
                    Strength = Required
                    Note = "the machine-readable kind enumeration (§11.2) — regenerated with the corpus." }

                  { Surface = "spec: WIRE_FORMAT.md §3.2 kind table"
                    Strength = Required
                    Note = "the kind's row + its spec-record shape." }
              else
                  { Surface = "veneers + analyzer (C#/VB)"
                    Strength = Check
                    Note =
                      "Phase 801 recorded that a payload-FIELD addition binds neither the C# `Coverage` reflection nor the VB analyzer's `Vocabulary.cs` (both pin `NodeKind`); §11 step 6 nonetheless names \"attribute rows\". Settle it for this change rather than inheriting either reading." }

              if isFamilyChange ch then
                  { Surface = "spec: WIRE_FORMAT.md §11 discriminator-family list"
                    Strength = Check
                    Note =
                      "§11 enumerates the families the rule is stated over. A change that introduces a family adds a row; a change within an existing one does not." }

              if isEnumSetChange ch then
                  { Surface = "spec: WIRE_FORMAT.md closed-set enumeration"
                    Strength = Required
                    Note = "the closed set's admitted strings are normative doc text as well as schema `enum` array." }

              if c.Severity = BreakingWire then
                  { Surface = "profile: §15 negotiation"
                    Strength = Required
                    Note =
                      "a major wire event moves the `/vN/` segment; an older consumer must classify the new profile `Foreign` and hard-refuse it (STABILITY.md §15 negotiate outcomes)." }

              if c.Severity = BreakingForEmitters then
                  { Surface = "downstream emitters"
                    Strength = Required
                    Note =
                      "coordinate the bump with every emitter, and advance the producing package's `<Version>` in the SAME commit — an unmoved version re-packs the slot under consumers already pinned to it." } ]

    // -----------------------------------------------------------------------
    // The report.
    // -----------------------------------------------------------------------

    let private severityLabel =
        function
        | Additive -> "ADDITIVE"
        | BreakingForEmitters -> "BREAKING (emitters)"
        | BreakingWire -> "BREAKING (wire)"
        | HostSurfaceOnly -> "host-surface only"
        | Unclassifiable -> "UNDECIDED — needs a human"

    let private strengthLabel =
        function
        | Required -> "MUST"
        | Check -> "CHECK"
        | NotBound -> "n/a"

    let private summarise (c: Change) : string =
        match c with
        | ArtifactVersionChanged(b, a) -> sprintf "artifact encoding version %d -> %d" b a
        | WireShapeChanged(b, a) -> sprintf "wire shape changed: %s -> %s" b a
        | HardenPolicyChanged(b, a) -> sprintf "harden policy changed: %s -> %s" b a
        | KindAdded t -> sprintf "kind added: %s" t
        | KindRemoved t -> sprintf "kind removed: %s" t
        | KindRenamed(o, n) -> sprintf "kind renamed (inferred): %s -> %s" o n
        | KindCategoryChanged(t, b, a) -> sprintf "kind %s category %s -> %s" t b a
        | OpAdded t -> sprintf "op added: %s" t
        | OpRemoved t -> sprintf "op removed: %s" t
        | UnionAdded n -> sprintf "union added: %s" n
        | UnionRemoved n -> sprintf "union removed: %s" n
        | UnionCaseAdded(u, c) -> sprintf "union case added: %s.%s" u c
        | UnionCaseRemoved(u, c) -> sprintf "union case removed: %s.%s" u c
        | UnionParamsChanged(n, _, _) -> sprintf "union %s type parameters changed" n
        | UnionTransparencyChanged(n, _, _) -> sprintf "union %s transparent case changed" n
        | EnumAdded n -> sprintf "closed set added: %s" n
        | EnumRemoved n -> sprintf "closed set removed: %s" n
        | EnumCaseAdded(e, w) -> sprintf "enum case added: %s.\"%s\"" e w
        | EnumCaseRemoved(e, w) -> sprintf "enum case removed: %s.\"%s\"" e w
        | EnumHostMappingChanged(n, _, _) -> sprintf "enum %s host case names changed" n
        | RecordAdded n -> sprintf "record added: %s" n
        | RecordRemoved n -> sprintf "record removed: %s" n
        | FieldAdded(o, f) -> sprintf "field added: %s.%s : %s (%s)" o.Describe f.Name f.Label f.OptClass
        | FieldRemoved(o, n, _) -> sprintf "field removed: %s.%s" o.Describe n
        | FieldTypeChanged(o, n, b, a) -> sprintf "field type changed: %s.%s : %s -> %s" o.Describe n b.Label a.Label
        | FieldHostSurfaceChanged(o, n, _, _) -> sprintf "field hostSurface changed: %s.%s" o.Describe n
        | FieldOptionalityChanged(o, n, b, a) ->
            sprintf "field optionality changed: %s.%s : %s -> %s" o.Describe n b.Opt a.Opt
        | FieldAnnotationsChanged(o, n, b, _) ->
            sprintf "field annotations %s: %s.%s" (if b = "" then "declared" else "changed") o.Describe n
        | UnionCaseAnnotationsChanged(u, c, b, _) ->
            sprintf "union case annotations %s: %s.%s" (if b = "" then "declared" else "changed") u c
        | KindAnnotationsChanged(o, b, _) ->
            sprintf "annotations %s: %s" (if b = "" then "declared" else "changed") o.Describe
        | EnumCaseAnnotationsChanged(e, w, b, _) ->
            sprintf "enum case annotations %s: %s.\"%s\"" (if b = "" then "declared" else "changed") e w
        | DefaultAdded(k, f, _) -> sprintf "authoring default added: %s.%s" k f
        | DefaultRemoved(k, f, _) -> sprintf "authoring default removed: %s.%s" k f
        | DefaultChanged(k, f, b, a) -> sprintf "authoring default changed: %s.%s : %s -> %s" k f b a

    /// The advisory report. Deterministic — byte-identical for identical inputs,
    /// which is what makes it diffable and what its test asserts.
    let report (rosterSource: string) (roster: Host list) (before: Snapshot) (after: Snapshot) : string =
        let cs = changes before after |> List.map classify
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "# idl-diff report"
        line ""
        line "Advisory. Nothing here has been applied; every verdict is a draft for a phase author to"
        line "confirm or correct. See fuaran-core `Fuaran.Core.Idl.Diff`."
        line ""
        line (sprintf "Artifact encoding: %d -> %d" before.Version after.Version)
        line (sprintf "Host roster source: %s" rosterSource)
        line ""

        if cs.IsEmpty then
            line "## Verdict"
            line ""
            line "No change. The two revisions describe the same vocabulary."
            line ""
        else
            line "## Verdict"
            line ""
            line (sprintf "Draft front-matter:   stability_impact: %s" (stabilityImpact cs))
            line (sprintf "Wire profile:         %s" (profileBump cs))
            line ""

            let count sev =
                cs |> List.filter (fun c -> c.Severity = sev) |> List.length

            line (
                sprintf
                    "%d change(s): %d additive, %d breaking-for-emitters, %d breaking-wire, %d host-surface only, %d undecided."
                    cs.Length
                    (count Additive)
                    (count BreakingForEmitters)
                    (count BreakingWire)
                    (count HostSurfaceOnly)
                    (count Unclassifiable)
            )

            line ""
            line "## Changes"
            line ""

            for c in cs do
                line (sprintf "### [%s] %s" (severityLabel c.Severity) (summarise c.Change))
                line ""
                line c.Rationale
                line ""
                line (sprintf "Rule: %s" c.Citation)
                line ""
                line "Obligations:"

                for o in obligations roster c do
                    line (sprintf "  %-5s %s" (strengthLabel o.Strength) o.Surface)
                    line (sprintf "        %s" o.Note)

                line ""

            line "## Consolidated obligation set"
            line ""
            line "Every surface named above, de-duplicated, strongest strength wins."
            line ""

            let rank =
                function
                | Required -> 2
                | Check -> 1
                | NotBound -> 0

            let consolidated =
                cs
                |> List.collect (obligations roster)
                |> List.groupBy _.Surface
                |> List.map (fun (surface, os) -> surface, os |> List.maxBy (fun o -> rank o.Strength))
                |> List.sortBy (fun (surface, o) -> -(rank o.Strength), surface)

            for (surface, o) in consolidated do
                line (sprintf "  %-5s %s" (strengthLabel o.Strength) surface)

            line ""

        sb.ToString()

    /// Whole-pipeline entry: two `idl.json` texts and an optional `manifest.json`
    /// text (used only for the roster, and only once it carries one).
    let run (manifestText: string option) (oldText: string) (newText: string) : Result<string, string> =
        let roster =
            manifestText
            |> Option.bind (fun t -> Json.parse t |> Result.toOption)
            |> Option.bind rosterFrom
            |> function
                | Some hs -> "manifest.json `hosts`", hs
                | None -> "declared (WIRE_FORMAT.md §11.0 — manifest.json carries no `hosts` key yet)", declaredRoster

        parse oldText
        |> Result.mapError (fun e -> "old: " + e)
        |> Result.bind (fun before ->
            parse newText
            |> Result.mapError (fun e -> "new: " + e)
            |> Result.map (fun after -> report (fst roster) (snd roster) before after))
