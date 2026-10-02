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

/// The IDL stability classifier over two `idl.json` revisions (and optionally each side's
/// `support.json`): the change list, each change's wire severity and F# consequence, the
/// host obligations it raises, and the verdict a gate branches on. Advisory: it writes no
/// file, bumps no version and gates no build.
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
            /// The field's wire name — the key fields are paired on across revisions.
            Name: string
            /// A human-readable rendering of the type, for report lines only; no verdict
            /// is ever decided on it.
            Label: string
            /// The type object's own `$type` — `str` / `list` / `hosted` / `json` …
            /// Carried separately because three of them (`hosted`, `json`,
            /// `opaque`) are ERASED slots whose admitted values the artifact
            /// deliberately does not state, which is a classification boundary.
            TypeTag: string
            /// The canonical JSON of the type object with every `hostSurface` key removed, at
            /// any depth — a difference here is a wire-type change.
            TypeWire: string
            /// The `hostSurface` blocks alone, as `path=<canonical JSON>` entries joined by
            /// `"; "`; empty when the type carries none.
            TypeHost: string
            /// The optionality's bare `$type` (`required` / `optional` / `omitDefault` /
            /// `hostOnly`) — what the classification rules branch on; `?` when absent.
            OptClass: string
            /// The optionality including an `omitDefault`'s canonical default value, so a moved
            /// identity default compares unequal even though `OptClass` does not.
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
        /// A node kind, by its `$type` tag.
        | OKind of tag: string
        /// A tree-op, by its `$type` tag.
        | OOp of tag: string
        /// One case of a value-union; the field belongs to the case, not to the union.
        | OUnionCase of union: string * case: string
        /// A non-discriminated record, by name.
        | ORecord of name: string
        /// The per-node field set every kind shares.
        | ONodeEnvelope

        /// The owner as report prose — `kind X`, `op X`, `union U.C`, `record R` or
        /// `node envelope` — the subject every change summary and rationale names.
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
        {
            /// The case's fields in authored order; diffed by name under `OUnionCase`.
            Fields: FieldSnap list
            /// The case's own canonical annotation set — `""` when it declares none or the
            /// revision predates the key.
            Annotations: string
        }

    /// One value-union of a revision: its type parameters, its cases and the case it
    /// encodes bare, if any.
    type UnionSnap =
        {
            /// The declared type-parameter names in order. A move is host-surface (the wire
            /// carries no type arguments) but breaks every reference at the old arity.
            Params: string list
            /// Cases keyed by tag, so a reorder alone never reads as a change.
            Cases: Map<string, CaseSnap>
            /// The case tags in artifact order. Read but never diffed, so reordering cases
            /// produces no change row.
            CaseOrder: string list
            /// The case that encodes as a bare value instead of a `$type`-tagged object, or
            /// `None`. Any move is a wire break.
            TransparentCase: string option
        }

    /// One closed string set of a revision.
    type EnumSnap =
        {
            /// The admitted wire strings, in artifact order. Cases pair by string, so a
            /// rename reads as a removal plus an addition.
            WireCases: string list
            /// The generated F# case names (`hostCases`, a host-surface key); empty when the
            /// artifact omits the key. Compared as a whole list, order included.
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
            /// The artifact ENCODING version (the shape of `idl.json` itself, not the
            /// vocabulary); `0` when the key is missing or not an integer.
            Version: int
            /// Kind tag → its fields in authored order.
            Kinds: Map<string, FieldSnap list>
            /// Kind tag → its `category`, `""` when absent. Metadata only — never serialised,
            /// so a move is host-surface.
            KindCategory: Map<string, string>
            /// A kind's OWN annotation set (Phase 119), canonically rendered — `""` when
            /// it declares none. A map beside [[Kinds]] rather than a field on it, for
            /// the reason [[KindCategory]] is one: the diff walk pairs field LISTS by
            /// tag, and a per-tag fact that is not a field belongs beside that walk
            /// rather than inside it.
            KindAnnotations: Map<string, string>
            /// Tree-op tag → its fields in authored order; empty when the revision predates
            /// the `ops` key.
            Ops: Map<string, FieldSnap list>
            /// A tree-op's own annotation set (Phase 119) — the same slot as
            /// [[KindAnnotations]], read from the `ops` collection. Ops are `IdlKind`s,
            /// so they are annotatable on identical terms.
            OpAnnotations: Map<string, string>
            /// Value-unions by name.
            Unions: Map<string, UnionSnap>
            /// Closed string sets by name.
            Enums: Map<string, EnumSnap>
            /// Non-discriminated records by name → their fields in authored order.
            Records: Map<string, FieldSnap list>
            /// `(kind, field) -> canonical value` — the smart-constructor defaults
            /// (`IdlDefault`), NOT the wire-visible `omitDefault` optionality.
            Defaults: Map<string * string, string>
            /// The node-envelope fields (`nodeFields`) every kind carries beside `id` / `kind`,
            /// diffed under `ONodeEnvelope`.
            NodeFields: FieldSnap list
            /// Phase 293 — the declared SUPPORT beside the vocabulary (`support.json`), keyed
            /// `doc:<path>` / `splice:<slot>` / `refine:<Union.Tag>` / `projection:<Kind>` /
            /// `prelude`, each value its canonical text. Empty when the snapshot was taken
            /// without a support document, so every pre-existing pair reads exactly as it did;
            /// with one, a projection or refine edit classifies instead of reading `unchanged`.
            Support: Map<string, string>
            /// The declared wire shape (Phases 108/109), as `discriminator/nodeEnvelope/keyOrder`
            /// — `"$type/nestedKind/sorted"` when the artifact predates the key, and each
            /// segment its default when the `wire` block omits it.
            Wire: string
            /// The declared hardening vocabulary (Phase 116), rendered canonically — the
            /// all-empty block when the artifact predates the key, since Phase 180 made
            /// an absent block and an undeclared policy the same claim. So the two read
            /// alike, which is what they mean.
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

    // Phase 310 — the snapshot is a TOLERANT classifier by design: an unrecognised shape degrades
    // to a placeholder rather than refusing, because a revision written before a key existed must
    // still compare. So it reads every member through the typed decode layer and DISCARDS a
    // refusal deliberately, at these three adapters and nowhere else; the traversal and the kind
    // checks are the layer's, not a private copy of them.

    let private field (name: string) (v: JVal) : JVal option = Decoder.tryMember name v

    let private str (name: string) (v: JVal) : string option =
        Decoder.optField name Decoder.str v |> Result.defaultValue None

    let private arr (name: string) (v: JVal) : JVal list =
        Decoder.fieldOr name [] Decoder.items v |> Result.defaultValue []

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

    /// The support document's declared entries as one flat map of canonical texts — the
    /// shape the diff walk pairs by key.
    let private supportEntries (doc: SupportDocument) : Map<string, string> =
        let sup = doc.Support

        let projection (p: Gen.KindProjection) =
            String.concat
                "\n--\n"
                [ p.SpecDecl
                  p.Encoder
                  p.Decoder
                  (match p.Mk with
                   | Some mk -> mk
                   | None -> "") ]

        [ for KeyValue(path, lines) in sup.Docs -> "doc:" + path, String.concat "\n" lines
          match sup.TypeSplice with
          | Some t -> yield "splice:type", t
          | None -> ()
          match sup.EncodeSplice with
          | Some t -> yield "splice:encode", t
          | None -> ()
          match sup.DecodeSplice with
          | Some t -> yield "splice:decode", t
          | None -> ()
          match sup.AccessorSplice with
          | Some t -> yield "splice:accessor", t
          | None -> ()
          for KeyValue(case, expr) in sup.CaseRefines -> "refine:" + case, expr
          for KeyValue(kind, proj) in sup.KindProjections -> "projection:" + kind, projection proj
          match doc.HostPrelude with
          | Some pre -> yield "prelude", pre.Module + "/" + pre.Path
          | None -> () ]
        |> Map.ofList

    /// Phase 293 — a snapshot with the support document beside the artifact joined in;
    /// `None` reads as a vocabulary with no declared support.
    let rec snapshotWith (artifact: JVal) (support: JVal option) : Result<Snapshot, string> =
        match support with
        | None -> snapshot artifact
        | Some sv ->
            SupportArtifact.ofJson sv
            |> Result.mapError (fun e -> "support: " + e)
            |> Result.bind (fun doc ->
                snapshot artifact
                |> Result.map (fun snap ->
                    { snap with
                        Support = supportEntries doc }))

    /// Read one `idl.json` revision. Tolerant of keys the revision predates
    /// (`ops`, `hostCases`, `transparentCase` are all emitted conditionally) —
    /// their absence is read as empty, which is what the emitter means by it.
    ///
    /// No support document is read, and a malformed member is skipped, so the only refusal is
    /// a root that is not a JSON object.
    and snapshot (artifact: JVal) : Result<Snapshot, string> =
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
                  Support = Map.empty
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
                    // Phase 180 — an absent block reads as the UNDECLARED policy, and it
                    // does so by walking the SAME members over an empty object rather
                    // than by a second answer beside them. This used to be a match whose
                    // `None` arm carried a literal second copy of the engine's old
                    // hard-coded default; it moved with `Artifact.readHarden`, because a
                    // classifier that disagrees with the reader about what an artifact
                    // MEANS is worse than either answer alone, and it is written this way
                    // so the two cannot drift apart again.
                    let h = field "harden" artifact |> Option.defaultValue (JObj [])

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
                           |> String.concat ",") }
        | _ -> Error "idl.json: expected a JSON object at the root"

    /// Parse + read in one step.
    let parse (text: string) : Result<Snapshot, string> = Json.parse text |> Result.bind snapshot

    /// Phase 293 — `parse` with the support document's text beside the artifact's.
    let parseWith (text: string) (supportText: string option) : Result<Snapshot, string> =
        match supportText with
        | None -> parse text
        | Some st ->
            Json.parse st
            |> Result.mapError (fun e -> "support: " + e)
            |> Result.bind (fun sv -> Json.parse text |> Result.bind (fun av -> snapshotWith av (Some sv)))

    // -----------------------------------------------------------------------
    // The change list.
    // -----------------------------------------------------------------------

    /// One difference between two snapshots. `changes` emits them sorted by a fixed rank
    /// and key, so the same pair always yields the same list; `classify` grades each.
    type Change =
        /// The `idl.json` encoding version moved. Graded host-surface, but a sign to
        /// reconcile the two encodings before trusting any other row.
        | ArtifactVersionChanged of before: int * after: int
        /// The declared wire shape moved (Phases 108/109) — `discriminator/nodeEnvelope/keyOrder`.
        | WireShapeChanged of before: string * after: string
        /// The declared HARDENING vocabulary moved (Phase 116) — which kind the codegen
        /// trust boundary gates, what it mints in its place, which cases it sanitises,
        /// and which unions have a transparent case.
        | HardenPolicyChanged of before: string * after: string
        /// A new `$type` branch: additive on the wire, an exhaustive-match break in F#.
        | KindAdded of tag: string
        /// A `$type` retired: every document using it is invalidated — a wire break.
        | KindRemoved of tag: string
        /// Inferred, never declared — see `renamePairs`. Reported ALONGSIDE the
        /// add + remove it explains, not instead of them.
        | KindRenamed of before: string * after: string
        /// A kind present in both revisions changed `category` — metadata that is never
        /// serialised, so host-surface only.
        | KindCategoryChanged of tag: string * before: string * after: string
        /// A new tree-op: additive; no generated F# shape exists for ops yet.
        | OpAdded of tag: string
        /// A tree-op retired: every persisted op stream carrying it stops decoding — a wire
        /// break.
        | OpRemoved of tag: string
        /// A new value-union: additive, and moves no generated shape (nothing referenced it).
        | UnionAdded of name: string
        /// A value-union retired: a wire break, and a type-name-reference break in F#.
        | UnionRemoved of name: string
        /// A case added to a union present in both revisions: additive, at the same
        /// wire-coupling cost as a new kind.
        | UnionCaseAdded of union: string * case: string
        /// A case removed from a union present in both revisions: a wire break.
        | UnionCaseRemoved of union: string * case: string
        /// The union's type-parameter list moved: host-surface on the wire (no type arguments
        /// are carried), but every reference at the old arity breaks.
        | UnionParamsChanged of name: string * before: string list * after: string list
        /// The union's bare-encoded case moved, appeared or vanished: every document using
        /// it changes bytes — a wire break.
        | UnionTransparencyChanged of name: string * before: string option * after: string option
        /// A new closed string set: additive.
        | EnumAdded of name: string
        /// A closed string set retired: a wire break.
        | EnumRemoved of name: string
        /// A wire string added to an existing set: additive, though a decoder predating it
        /// refuses the value (host lag).
        | EnumCaseAdded of enumName: string * wire: string
        /// A wire string removed from an existing set: documents carrying it stop
        /// validating — a wire break.
        | EnumCaseRemoved of enumName: string * wire: string
        /// The generated F# case names moved while the wire strings did not: host-surface,
        /// but every match arm naming an old case breaks.
        | EnumHostMappingChanged of name: string * before: string list * after: string list
        /// A new non-discriminated record: additive.
        | RecordAdded of name: string
        /// A record retired: a wire break.
        | RecordRemoved of name: string
        /// A field new to an existing owner. Its optionality class decides the severity:
        /// `required` breaks the wire (old documents lack it), `hostOnly` is host-surface,
        /// anything else is additive.
        | FieldAdded of Owner * FieldSnap
        /// A field gone from an existing owner: a wire break, or host-surface for a
        /// `hostOnly` field.
        | FieldRemoved of Owner * name: string * was: FieldSnap
        /// The field's wire type moved: a wire break, except an `int` → `float` widening
        /// (additive) or a move across an erased `hosted` / `json` / `opaque` slot at any
        /// depth (undecided).
        | FieldTypeChanged of Owner * name: string * before: FieldSnap * after: FieldSnap
        /// The generated DECLARATION moved; the wire did not. `TFn`'s F#
        /// signature, a `THosted` slot's codec expressions.
        | FieldHostSurfaceChanged of Owner * name: string * before: string * after: string
        /// The field's optionality (`Opt`, default value included) moved. Additive only for
        /// `required` → `optional`; every other move, a changed identity default among them,
        /// is a wire break.
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
        /// A smart-constructor (authoring) default appeared — not the wire-visible
        /// omit-at-default. Additive; `value` is canonical JSON.
        | DefaultAdded of kind: string * field: string * value: string
        /// An authoring default was withdrawn: the wire is unchanged, but host code that
        /// relied on it emits a different document — breaks emitters.
        | DefaultRemoved of kind: string * field: string * value: string
        /// An authoring default's value moved: every authoring site that omitted the field
        /// now emits different bytes — breaks emitters.
        | DefaultChanged of kind: string * field: string * before: string * after: string
        /// Phase 293 — a declared SUPPORT entry (`support.json`) moved: a doc block, a verbatim
        /// splice, a case refine, a kind projection or the host prelude, keyed as
        /// [[Snapshot.Support]] keys them. `None` on a side means the entry is absent there.
        /// Never a wire event — support is host-language source — and the one row that used
        /// to read `unchanged` when only the support beside the vocabulary had moved.
        | SupportChanged of key: string * before: string option * after: string option

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

    /// A change's wire verdict — what an OLD document, or an old emitter, meets under the
    /// new vocabulary. Independent of the F# consequence reported beside it.
    type Severity =
        /// Every previously-valid document stays valid and every
        /// previously-conformant emitter stays conformant.
        | Additive
        /// Every old document still decodes, to the same value and the same bytes,
        /// but host code written against the old contract now emits a different
        /// document (or stops compiling) — an authoring default removed or moved.
        /// Minor on paper, a break in practice.
        ///
        /// NOT the class of a required field arriving, or of a field becoming
        /// required (Phase 304): there the old documents themselves are refused,
        /// which is `BreakingWire`. A class is a fact about what an old document
        /// does under the new vocabulary, never about the emitter alone.
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

    /// One change with its wire verdict and the reasoning behind it — a report row.
    type Classification =
        {
            /// The graded change, carried so a row can be summarised, and joined against a
            /// host roster by `obligations`, on its own.
            Change: Change
            /// Its wire severity, from the change's rule in the descriptor table.
            Severity: Severity
            /// Report prose explaining why the severity applies, naming the subject; for an
            /// `Unclassifiable` row it also says what to check by hand.
            Rationale: string
            /// The rule document(s) the verdict rests on; `—` where none is cited.
            Citation: string
        }

    /// A field added to an existing owner. The optionality class decides
    /// everything, and the class is a fact about what an OLD document does under
    /// the new vocabulary (Phase 304): `required` is the one every stored document
    /// of the owner fails, and it is the one most likely to be declared additive by
    /// hand.
    let private classifyFieldAdd (owner: Owner) (f: FieldSnap) =
        match f.OptClass with
        | "required" ->
            // Phase 304 — this row was `BreakingForEmitters` (a minor) on the claim that
            // old documents still decode. They do not: the decoder refuses a document
            // missing a required member ("required field '<name>' is absent"), and an
            // authoring default never fills on decode. A minor would let a `Behind`
            // consumer tolerate the profile and then refuse every stored document.
            BreakingWire,
            sprintf
                "a REQUIRED field added to %s — every document written under the previous contract lacks it, and the decoder refuses a document missing a required member (an authoring default is applied by the smart constructors, never on decode). Old documents stop decoding, so this is a `/v2/` event, not a minor; declare an optional or omit-at-default field if old documents must survive."
                owner.Describe,
            "docs/idl-stability-classes.md (a class is what an old document does under the new vocabulary, Phase 304); Idl.Decode (a required member absent is refused)"
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

    /// The `hostSurface` blocks of a [[FieldSnap.TypeHost]] string, path to parsed
    /// block (Phase 252). The string joins `path=<canonical JSON>` entries with
    /// `"; "`, so it is split only where that separator sits OUTSIDE a JSON string
    /// and outside any brace — a host signature may itself contain `"; "`. An entry
    /// that does not parse is kept as a bare string, so it still compares.
    let private hostBlocks (typeHost: string) : Map<string, JVal> =
        let entries = ResizeArray<string>()
        let current = System.Text.StringBuilder()
        let mutable depth = 0
        let mutable inString = false
        let mutable escaped = false
        let mutable i = 0

        while i < typeHost.Length do
            let ch = typeHost[i]

            if inString then
                current.Append ch |> ignore

                if escaped then
                    escaped <- false
                elif ch = '\\' then
                    escaped <- true
                elif ch = '"' then
                    inString <- false

                i <- i + 1
            elif depth = 0 && ch = ';' && i + 1 < typeHost.Length && typeHost[i + 1] = ' ' then
                entries.Add(current.ToString())
                current.Clear() |> ignore
                i <- i + 2
            else
                match ch with
                | '"' -> inString <- true
                | '{'
                | '[' -> depth <- depth + 1
                | '}'
                | ']' -> depth <- depth - 1
                | _ -> ()

                current.Append ch |> ignore
                i <- i + 1

        if current.Length > 0 then
            entries.Add(current.ToString())

        entries
        |> Seq.choose (fun e ->
            match e.IndexOf '=' with
            | -1 -> None
            | at ->
                let path = e.Substring(0, at)
                let body = e.Substring(at + 1)

                Some(
                    path,
                    match Json.parse body with
                    | Ok v -> v
                    | Error _ -> JStr body
                ))
        |> Map.ofSeq

    /// Whether a `hostSurface` block is a HOSTED slot's (it names a codec) rather
    /// than a `fn` slot's (it names a placeholder): the two carry different keys.
    let private isHostedBlock (block: JVal) =
        (field "encode" block).IsSome || (field "decode" block).IsSome

    /// Did any HOSTED slot's declaration move between two `TypeHost` strings? A
    /// hosted slot's codec IS its wire form — the artifact does not state what the
    /// codec writes — so a move here can move every document's bytes, and it is
    /// `Unclassifiable`, never `HostSurfaceOnly` (Phase 252; the codec-swap
    /// revision that read as an absorbable `host-surface`, exit 0). A `fn` slot's
    /// declaration stays host-surface: a closure is the fixed sentinel on the wire
    /// whatever its signature says.
    let private hostedSlotMoved (before: string) (after: string) : bool =
        let hosted s =
            hostBlocks s |> Map.filter (fun _ b -> isHostedBlock b)

        hosted before <> hosted after

    /// Did any slot's declared F# HOST TYPE move? The `fsharp` member is the one
    /// the generated declaration spells; a codec or placeholder expression is a
    /// body, and moving only that leaves every construction site where it was.
    let private hostTypeMoved (before: string) (after: string) : bool =
        let types s =
            hostBlocks s |> Map.map (fun _ b -> str "fsharp" b)

        types before <> types after

    /// Is `after` `before` with one or more `int` positions widened to `float`, and
    /// nothing else moved (Phase 252)? Read on the wire-type objects, so a list, a
    /// map value or a union argument widens the same way a bare field does.
    let rec private widensIntToFloat (before: JVal) (after: JVal) : bool option =
        // `Some true` — widened somewhere; `Some false` — identical; `None` — any
        // other difference.
        match before, after with
        | JObj bs, JObj afs when
            (List.tryPick (fun (k, v) -> if k = "$type" then Some v else None) bs = Some(JStr "int")
             && List.tryPick (fun (k, v) -> if k = "$type" then Some v else None) afs = Some(JStr "float")
             && bs.Length = 1
             && afs.Length = 1)
            ->
            Some true
        | JObj bs, JObj afs when List.map fst bs = List.map fst afs ->
            (Some false, List.zip bs afs)
            ||> List.fold (fun acc ((_, bv), (_, av)) ->
                match acc, widensIntToFloat bv av with
                | None, _
                | _, None -> None
                | Some x, Some y -> Some(x || y))
        | JArr bs, JArr afs when bs.Length = afs.Length ->
            (Some false, List.zip bs afs)
            ||> List.fold (fun acc (bv, av) ->
                match acc, widensIntToFloat bv av with
                | None, _
                | _, None -> None
                | Some x, Some y -> Some(x || y))
        | b, a when b = a -> Some false
        | _ -> None

    let private isIntToFloatWidening (before: FieldSnap) (after: FieldSnap) : bool =
        match Json.parse before.TypeWire, Json.parse after.TypeWire with
        | Ok b, Ok a -> widensIntToFloat b a = Some true
        | _ -> false

    /// What a classified change does to a CONSUMER'S F# SOURCE compiled against the
    /// generated structural layer. A different question from what it does to a
    /// document, and the two answers diverge routinely — which is the whole reason
    /// this axis is reported beside `Severity` rather than derived from it.
    ///
    /// Each case is named for the SITE that stops working, because that is what a
    /// consumer reads in the compiler output.
    type FSharpConsequence =
        /// **Construction sites.** `Gen.fsharpTypes` emits a kind, a record and a
        /// union case as F# RECORDS, and a record literal must name every field —
        /// so a field arriving, leaving or changing type breaks every full literal
        /// that builds one: `FS0764` ("No assignment given for field") on an
        /// arrival, `FS1129` (no such field) on a departure, `FS0001` on a type
        /// move.
        ///
        /// **Independent of optionality.** `string option` is still a field the
        /// literal must name, so an OPTIONAL field added lands here too while its
        /// wire severity is `Additive`. That divergence is the row this table
        /// exists for: reading the wire verdict alone says a consumer repins
        /// without source changes, and it does not.
        | FullLiteralConstruction
        /// **Match sites.** An enum, a value-union and the per-vocabulary node-kind
        /// discriminator each emit as a closed F# DU, so a case arriving makes every
        /// exhaustive `match` incomplete — `FS0025`, a WARNING under this repo's
        /// `TreatWarningsAsErrors=false` and an error wherever a consumer sets it,
        /// and a `MatchFailureException` at run time on the first value carrying the
        /// new case — and a case leaving makes the arm naming it undefined
        /// (`FS0039`).
        | ExhaustiveMatch
        /// **Reference sites.** A whole generated TYPE left, or its type parameters
        /// moved, so a consumer naming it no longer resolves (`FS0039`) or applies
        /// the wrong arity. A type ARRIVING is not this: nothing could have
        /// referenced it.
        | TypeNameReference
        /// **Neither — until the package slot is reused.** The generated shape
        /// moved, so a consumer whose extracted package cache still holds the
        /// previous assembly for the SAME version compiles against one shape and
        /// runs against the other. It surfaces as an `InvalidCastException` thrown
        /// from code that type-checked, at the first value crossing the boundary,
        /// with nothing in the source to read.
        ///
        /// It ACCOMPANIES the three classes above rather than replacing them: those
        /// are what a clean rebuild reports, this is what a stale restore reports
        /// instead of them. Which is why a shape change wants a fresh version
        /// rather than a repack of a slot consumers already hold.
        | StalePackageSlot
        /// The generated shape did not move: the change is on the wire only, in the
        /// host-surface declarations, in an annotation, or in an authoring default.
        | NoGeneratedShapeChange
        /// The change crosses an ERASED slot (`hosted` / `json` / `opaque`), so
        /// nothing in the artifact says whether the generated shape moved.
        /// Reported, never guessed — `Unclassifiable`'s counterpart on this axis,
        /// and for the same reason: a confident wrong answer on the commonest kind
        /// of IDL tidy-up gets the whole report skimmed.
        | GeneratedShapeUnreadable

    /// The stable label of a consequence class. A contract: these strings are what
    /// an external gate greps and what the `docs/` table names.
    let consequenceLabel =
        function
        | FullLiteralConstruction -> "full-literal-construction"
        | ExhaustiveMatch -> "exhaustive-match"
        | TypeNameReference -> "type-name-reference"
        | StalePackageSlot -> "stale-package-slot"
        | NoGeneratedShapeChange -> "no-generated-shape-change"
        | GeneratedShapeUnreadable -> "generated-shape-unreadable"

    /// One sentence per consequence class — the reason it applies, for a report that
    /// has to stand on its own beside a compiler message.
    let consequenceWhy =
        function
        | FullLiteralConstruction ->
            "every full record literal that builds this owner stops compiling — FS0764 on an added field, FS1129 on a removed one, FS0001 on a moved type. True for an OPTIONAL field too: a literal must name it."
        | ExhaustiveMatch ->
            "every exhaustive match over the generated DU stops being exhaustive — FS0025 (a warning by default, an error under TreatWarningsAsErrors, a MatchFailureException at run time) on an added case, FS0039 on a removed one."
        | TypeNameReference ->
            "a generated type name no longer resolves, or no longer takes the arity a consumer applies — FS0039."
        | StalePackageSlot ->
            "no compile event at all if the package slot was REPACKED rather than advanced: a consumer restoring the same version from a warm cache compiles against the new shape and runs against the old one, and the mismatch arrives as an InvalidCastException from code that type-checked."
        | NoGeneratedShapeChange ->
            "the generated declarations are unchanged, so no construction, match or reference site moves."
        | GeneratedShapeUnreadable ->
            "the change crosses an erased slot whose admitted values the artifact does not state, so whether the generated shape moved cannot be read off it."

    /// Every consequence class, in report order — so a renderer and a document
    /// enumerate the table rather than each restating it.
    let allConsequences: FSharpConsequence list =
        [ FullLiteralConstruction
          ExhaustiveMatch
          TypeNameReference
          StalePackageSlot
          NoGeneratedShapeChange
          GeneratedShapeUnreadable ]

    // -----------------------------------------------------------------------
    // Phase 293 — THE DESCRIPTOR TABLE. One row per case of the `Change` union, carrying
    // everything the classifier used to decide in seven parallel matches: the sort rank, the
    // §11 family the change reaches, the wire verdict (severity, rationale, citation), the F#
    // consequence, the one-line summary, and the rows the `docs/` table shows for it. `classify`,
    // `consequences`, `sortKey`, `summarise`, the three family predicates and `mappingTable` are
    // all projections of `rules` — so a rule corrected once is corrected everywhere it is read,
    // which is what Phase 252's two corrections needed and did not have.
    // -----------------------------------------------------------------------

    /// Which `WIRE_FORMAT.md` §11 strand a change reaches — the three predicates the host-roster
    /// join reads.
    type private Family =
        /// Alters the NodeKind set: reaches the authoring veneers, the analyzer vocabulary, the
        /// native render arms and `manifest.kinds`.
        | KindSet
        /// Alters a `$type` discriminator family OTHER than NodeKind.
        | DiscriminatorFamily
        /// Alters a closed string set.
        | EnumSet
        | NoFamily

    /// One row of the `docs/` mapping table.
    type private DocRow =
        { Subject: string
          Wire: string
          FSharp: string }

    /// What a snapshot pair knows about a kind's field that the `Change` does not carry: its
    /// optionality class (`Some optClass` when a snapshot has the field), and whether an
    /// authoring default is declared for it on either side.
    type private Context =
        { Optionality: string -> string -> string option
          HasDefault: string -> string -> bool }

    /// The context-free reading — no snapshot to ask.
    let private noContext: Context =
        { Optionality = fun _ _ -> None
          HasDefault = fun _ _ -> false }

    type private Rule =
        {
            /// The `Change` case this rule is about — the join key, read off the value by reflection.
            Case: string
            Rank: string
            Family: Family
            /// The change's own sort key within its rank.
            Key: Change -> string
            /// The wire verdict: severity, rationale, citation.
            Verdict: Change -> Severity * string * string
            /// The F# consequence set, given the kind-field optionality lookup a snapshot pair supplies.
            Consequence: Context -> Change -> FSharpConsequence list
            Summary: Change -> string
            Doc: DocRow list
        }

    let private misapplied (rule: string) (c: Change) : 'a =
        invalidArg "c" (sprintf "rule %s applied to %A" rule c)

    let private construction = [ FullLiteralConstruction; StalePackageSlot ]
    let private matching = [ ExhaustiveMatch; StalePackageSlot ]
    let private reference = [ TypeNameReference; StalePackageSlot ]
    let private noShape = [ NoGeneratedShapeChange ]

    let private erasedTag (t: string) =
        t = "hosted" || t = "json" || t = "opaque"

    /// Phase 293 — the erased-slot rule walks NESTED slots: a list element, a map value or a
    /// union argument that is `hosted` / `json` / `opaque` is as undecidable as a bare one, and
    /// the rule used to test the top-level tag alone, so a nested crossing read as `breaking-wire`
    /// where the document said undecided. Returns the first erased tag met, outermost first.
    let rec private erasedWithin (v: JVal) : string option =
        match v with
        | JObj fs ->
            let own =
                fs
                |> List.tryPick (fun (k, x) ->
                    match k, x with
                    | "$type", JStr t when erasedTag t -> Some t
                    | _ -> None)

            match own with
            | Some t -> Some t
            | None -> fs |> List.tryPick (fun (_, x) -> erasedWithin x)
        | JArr xs -> xs |> List.tryPick erasedWithin
        | _ -> None

    let private erasedIn (f: FieldSnap) : string option =
        if erasedTag f.TypeTag then
            Some f.TypeTag
        else
            match Json.parse f.TypeWire with
            | Ok v -> erasedWithin v
            | Error _ -> None

    let private rule
        (case: string)
        (rank: string)
        (family: Family)
        (key: Change -> string)
        (verdict: Change -> Severity * string * string)
        (consequence: Context -> Change -> FSharpConsequence list)
        (summary: Change -> string)
        (doc: DocRow list)
        : Rule =
        { Case = case
          Rank = rank
          Family = family
          Key = key
          Verdict = verdict
          Consequence = consequence
          Summary = summary
          Doc = doc }

    let private row (subject: string) (wire: string) (fsharp: string) =
        { Subject = subject
          Wire = wire
          FSharp = fsharp }

    let private always (cs: FSharpConsequence list) : Context -> Change -> FSharpConsequence list = fun _ _ -> cs

    /// The annotation rows share one verdict and one documented shape.
    let private annotationRow (subject: string) =
        row
            subject
            "`additive` on a first marking, `host-surface-only` otherwise"
            "`no-generated-shape-change` — an `Obsolete` attribute moves, which changes which **warnings** a consumer sees, not a shape"

    /// A `mk<Kind>` smart constructor takes a parameter for every REQUIRED field with no
    /// authoring default (`Gen.fsharpModuleWith`'s `defaultsDecl`), so a default added to or
    /// removed from such a field moves the constructor's parameter list — a construction break
    /// at every call site. On any other optionality the constructor's BODY moves and its shape
    /// does not. Without a snapshot to ask (the context-free `consequences`), the field is read
    /// as required: the answer that costs a consumer a rebuild rather than a surprise.
    let private defaultConsequence (ctx: Context) (kind: string) (field: string) =
        match ctx.Optionality kind field with
        | Some "required"
        | None -> construction
        | Some _ -> noShape

    /// The same parameter rule from the OPTIONALITY side: a kind field moving into or out of
    /// `required` (with no authoring default to stand in for the parameter) is a parameter
    /// arriving at or leaving `mk<Kind>`, whichever class is on the other side. Found by the
    /// consequence property (Phase 293): `required -> omitDefault` emits the same record member
    /// and a shorter constructor, which the axis used to read as no shape change.
    let private requiredFlipped (ctx: Context) (owner: Owner) (field: string) (before: FieldSnap) (after: FieldSnap) =
        match owner with
        | OKind tag ->
            (before.OptClass = "required") <> (after.OptClass = "required")
            && not (ctx.HasDefault tag field)
        | _ -> false

    let private rules: Rule list =
        [ rule
              "ArtifactVersionChanged"
              "00"
              NoFamily
              (fun _ -> "")
              (function
              | ArtifactVersionChanged(b, a) ->
                  HostSurfaceOnly,
                  sprintf
                      "the artifact ENCODING version moved %d → %d. This describes the shape of idl.json itself, not the vocabulary it carries — reconcile the two revisions' encodings before trusting any other row."
                      b
                      a,
                  "Artifact.version"
              | c -> misapplied "ArtifactVersionChanged" c)
              (always noShape)
              (function
              | ArtifactVersionChanged(b, a) -> sprintf "artifact encoding version %d -> %d" b a
              | c -> misapplied "ArtifactVersionChanged" c)
              [ row "the artifact's own encoding version" "`host-surface-only`" "`no-generated-shape-change`" ]

          rule
              "WireShapeChanged"
              "01"
              NoFamily
              (fun _ -> "")
              (function
              | WireShapeChanged(b, a) ->
                  BreakingWire,
                  sprintf
                      "the declared WIRE SHAPE moved %s → %s — the discriminator key, the node-envelope nesting and/or the canonical key order relocate every tag or every byte on the wire, so every document's bytes move. A `/v2/` major event by definition."
                      b
                      a,
                  "Idl.WireShape (Phases 108/109/111); VOCABULARY.md §4.2"
              | c -> misapplied "WireShapeChanged" c)
              (always noShape)
              (function
              | WireShapeChanged(b, a) -> sprintf "wire shape changed: %s -> %s" b a
              | c -> misapplied "WireShapeChanged" c)
              [ row "the declared wire shape" "`breaking-wire`" "`no-generated-shape-change`" ]

          rule
              "HardenPolicyChanged"
              "02"
              NoFamily
              (fun _ -> "")
              (function
              | HardenPolicyChanged(b, a) ->
                  HostSurfaceOnly,
                  sprintf
                      "the declared HARDENING vocabulary moved %s → %s — the codegen trust boundary now gates a different kind, or mints a different placeholder, or matches a different literal case. Nothing here moves a document's bytes BY ITSELF: the one wire-visible member is the transparent-case set, whose effect is reported per union as its own row. What changes is what SCAFFOLDED source contains, so re-scaffold anything generated against the old declaration."
                      b
                      a,
                  "Idl.HardenPolicy (Phase 116); STABILITY.md the IDL engine"
              | c -> misapplied "HardenPolicyChanged" c)
              (always noShape)
              (function
              | HardenPolicyChanged(b, a) -> sprintf "harden policy changed: %s -> %s" b a
              | c -> misapplied "HardenPolicyChanged" c)
              [ row "the hardening vocabulary" "`host-surface-only`" "`no-generated-shape-change`" ]

          rule
              "KindAdded"
              "10"
              KindSet
              (function
              | KindAdded t -> t
              | c -> misapplied "KindAdded" c)
              (function
              | KindAdded t ->
                  Additive,
                  sprintf
                      "kind `%s` added — a new `$type` branch on the schema's top-level `oneOf`; every previously-valid document stays valid."
                      t,
                  "VOCABULARY.md §4.1 (additive `core@1.(x+1)` profile minor); STABILITY.md (NodeKind addition = minor)"
              | c -> misapplied "KindAdded" c)
              (always matching)
              (function
              | KindAdded t -> sprintf "kind added: %s" t
              | c -> misapplied "KindAdded" c)
              [ row
                    "a node kind added"
                    "`additive`"
                    "`exhaustive-match` (a kind is a case of the generated node-kind DU)" ]

          rule
              "KindRemoved"
              "11"
              KindSet
              (function
              | KindRemoved t -> t
              | c -> misapplied "KindRemoved" c)
              (function
              | KindRemoved t ->
                  BreakingWire,
                  sprintf
                      "kind `%s` REMOVED — retiring a `$type` discriminator invalidates every document that used it. A `/v2/` major event, and per §4.2 a thing to do before publication or not at all."
                      t,
                  "VOCABULARY.md §4.2 (removal / rename = a `v2` major)"
              | c -> misapplied "KindRemoved" c)
              (always matching)
              (function
              | KindRemoved t -> sprintf "kind removed: %s" t
              | c -> misapplied "KindRemoved" c)
              [ row "a node kind removed" "`breaking-wire`" "`exhaustive-match`" ]

          rule
              "KindRenamed"
              "12"
              KindSet
              (function
              | KindRenamed(o, n) -> o + ">" + n
              | c -> misapplied "KindRenamed" c)
              (function
              | KindRenamed(o, n) ->
                  BreakingWire,
                  sprintf
                      "INFERRED rename `%s` → `%s` (identical field signature, unique on both sides). Inference, not a declaration — the wire records no identity beyond the `$type` string. The add + remove above stand on their own; this row only explains them."
                      o
                      n,
                  "VOCABULARY.md §4.2 (a `$type` rename is a breaking wire change)"
              | c -> misapplied "KindRenamed" c)
              // Reported ALONGSIDE the add + remove that explain it, and those two rows carry
              // the consequence; claiming it again here would double-count.
              (always noShape)
              (function
              | KindRenamed(o, n) -> sprintf "kind renamed (inferred): %s -> %s" o n
              | c -> misapplied "KindRenamed" c)
              [ row
                    "a node kind renamed (inferred, reported beside the add and the remove it explains)"
                    "`breaking-wire`"
                    "`no-generated-shape-change` — the add and the remove carry the consequence" ]

          rule
              "KindCategoryChanged"
              "13"
              NoFamily
              (function
              | KindCategoryChanged(t, _, _) -> t
              | c -> misapplied "KindCategoryChanged" c)
              (function
              | KindCategoryChanged(t, b, a) ->
                  HostSurfaceOnly,
                  sprintf
                      "kind `%s` re-categorised %s → %s. `Category` is metadata and is never serialised (Idl.IdlKind) — no document changes."
                      t
                      b
                      a,
                  "Idl.IdlKind (`Category` is metadata, not serialised)"
              | c -> misapplied "KindCategoryChanged" c)
              (always noShape)
              (function
              | KindCategoryChanged(t, b, a) -> sprintf "kind %s category %s -> %s" t b a
              | c -> misapplied "KindCategoryChanged" c)
              [ row "a kind's category" "`host-surface-only`" "`no-generated-shape-change`" ]

          rule
              "KindAnnotationsChanged"
              "14"
              NoFamily
              (function
              | KindAnnotationsChanged(o, _, _) -> o.Key
              | c -> misapplied "KindAnnotationsChanged" c)
              (function
              // Phase 119 — the same three grades, for the same reason: a kind-level or
              // enum-case marking is no more on the wire than a field's, so marking a whole
              // node kind for retirement costs no breaking bump and the two-release
              // retirement path the charter needs is affordable.
              | KindAnnotationsChanged(owner, b, a) -> classifyAnnotations owner.Describe b a
              | c -> misapplied "KindAnnotationsChanged" c)
              (always noShape)
              (function
              | KindAnnotationsChanged(o, b, _) ->
                  sprintf "annotations %s: %s" (if b = "" then "declared" else "changed") o.Describe
              | c -> misapplied "KindAnnotationsChanged" c)
              [ annotationRow "a kind's or a tree-op's annotation set" ]

          rule
              "OpAdded"
              "20"
              DiscriminatorFamily
              (function
              | OpAdded t -> t
              | c -> misapplied "OpAdded" c)
              (function
              | OpAdded t ->
                  Additive,
                  sprintf "op `%s` added — a new `$type` branch on the TreeOp union; existing op streams stay valid." t,
                  "WIRE_FORMAT.md §3.4; VOCABULARY.md §4.1 by the same additive argument"
              | c -> misapplied "OpAdded" c)
              // The op vocabulary has no generated F# shape at all — the F# type emitter leaves
              // that leg unshipped (Phase 703) — so an op change moves no declaration. This row
              // changes the day that leg lands.
              (always noShape)
              (function
              | OpAdded t -> sprintf "op added: %s" t
              | c -> misapplied "OpAdded" c)
              [ row
                    "a tree-op added"
                    "`additive`"
                    "`no-generated-shape-change` — the F# type emitter leaves the op vocabulary unshipped (Phase 703). **This row changes the day that leg lands.**" ]

          rule
              "OpRemoved"
              "21"
              DiscriminatorFamily
              (function
              | OpRemoved t -> t
              | c -> misapplied "OpRemoved" c)
              (function
              | OpRemoved t ->
                  BreakingWire,
                  sprintf
                      "op `%s` REMOVED — every persisted op stream carrying it becomes undecodable, and an op stream is a hash-chained archive, not a live message. Strictly worse than retiring a kind."
                      t,
                  "VOCABULARY.md §4.2; STABILITY.md (op-stream wire shape)"
              | c -> misapplied "OpRemoved" c)
              (always noShape)
              (function
              | OpRemoved t -> sprintf "op removed: %s" t
              | c -> misapplied "OpRemoved" c)
              [ row "a tree-op removed" "`breaking-wire`" "`no-generated-shape-change` — as for a tree-op added" ]

          rule
              "UnionAdded"
              "30"
              NoFamily
              (function
              | UnionAdded n -> n
              | c -> misapplied "UnionAdded" c)
              (function
              | UnionAdded n ->
                  Additive,
                  sprintf "value-union `%s` introduced — reachable only from a field that also changed." n,
                  "—"
              | c -> misapplied "UnionAdded" c)
              // A type arriving breaks nothing: no source could have named it.
              (always noShape)
              (function
              | UnionAdded n -> sprintf "union added: %s" n
              | c -> misapplied "UnionAdded" c)
              [ row "a union added" "`additive`" "`no-generated-shape-change` — nothing could have referenced it" ]

          rule
              "UnionRemoved"
              "31"
              NoFamily
              (function
              | UnionRemoved n -> n
              | c -> misapplied "UnionRemoved" c)
              (function
              | UnionRemoved n ->
                  BreakingWire,
                  sprintf "value-union `%s` removed — every document carrying one of its cases is invalidated." n,
                  "VOCABULARY.md §4.2"
              | c -> misapplied "UnionRemoved" c)
              (always reference)
              (function
              | UnionRemoved n -> sprintf "union removed: %s" n
              | c -> misapplied "UnionRemoved" c)
              [ row "a union removed" "`breaking-wire`" "`type-name-reference`" ]

          rule
              "UnionCaseAdded"
              "32"
              DiscriminatorFamily
              (function
              | UnionCaseAdded(u, c) -> u + "." + c
              | c -> misapplied "UnionCaseAdded" c)
              (function
              | UnionCaseAdded(u, c) ->
                  Additive,
                  sprintf
                      "case `%s` added to `%s` — a `$type`-discriminator family (WIRE_FORMAT.md §11), so the wire-coupling cost is IDENTICAL to a new kind's; only the confusion cost is smaller. Governed: it still cites §1.1 demand evidence and acknowledges the §11 cost."
                      c
                      u,
                  "VOCABULARY.md §2 (the quiet-churn caveat); WIRE_FORMAT.md §11 (discriminator families)"
              | c -> misapplied "UnionCaseAdded" c)
              (always matching)
              (function
              | UnionCaseAdded(u, c) -> sprintf "union case added: %s.%s" u c
              | c -> misapplied "UnionCaseAdded" c)
              [ row "a union case added" "`additive`" "`exhaustive-match`" ]

          rule
              "UnionCaseRemoved"
              "33"
              DiscriminatorFamily
              (function
              | UnionCaseRemoved(u, c) -> u + "." + c
              | c -> misapplied "UnionCaseRemoved" c)
              (function
              | UnionCaseRemoved(u, c) ->
                  BreakingWire,
                  sprintf "case `%s` REMOVED from `%s` — a retired `$type` in a discriminator family." c u,
                  "VOCABULARY.md §4.2"
              | c -> misapplied "UnionCaseRemoved" c)
              (always matching)
              (function
              | UnionCaseRemoved(u, c) -> sprintf "union case removed: %s.%s" u c
              | c -> misapplied "UnionCaseRemoved" c)
              [ row "a union case removed" "`breaking-wire`" "`exhaustive-match`" ]

          rule
              "UnionParamsChanged"
              "34"
              NoFamily
              (function
              | UnionParamsChanged(n, _, _) -> n
              | c -> misapplied "UnionParamsChanged" c)
              (function
              | UnionParamsChanged(n, b, a) ->
                  HostSurfaceOnly,
                  sprintf
                      "`%s` type parameters %A → %A — generic arity is a host-declaration property; the wire carries no type arguments."
                      n
                      b
                      a,
                  "Idl.IdlUnion.Params"
              | c -> misapplied "UnionParamsChanged" c)
              (always reference)
              (function
              | UnionParamsChanged(n, _, _) -> sprintf "union %s type parameters changed" n
              | c -> misapplied "UnionParamsChanged" c)
              // The document used to say `breaking-wire` here (Phase 293 corrected it to what the
              // classifier has always decided): the wire carries no type arguments.
              [ row
                    "a union's type parameters moved"
                    "`host-surface-only` — the wire carries no type arguments"
                    "`type-name-reference` (the wrong arity)" ]

          rule
              "UnionTransparencyChanged"
              "35"
              DiscriminatorFamily
              (function
              | UnionTransparencyChanged(n, _, _) -> n
              | c -> misapplied "UnionTransparencyChanged" c)
              (function
              | UnionTransparencyChanged(n, b, a) ->
                  BreakingWire,
                  sprintf
                      "`%s` transparent case %A → %A — a transparent case encodes as a BARE value rather than a `$type`-tagged object, so this moves the bytes of every document using it."
                      n
                      b
                      a,
                  "Idl.TransparentUnion; STABILITY.md wire-format section"
              | c -> misapplied "UnionTransparencyChanged" c)
              (always noShape)
              (function
              | UnionTransparencyChanged(n, _, _) -> sprintf "union %s transparent case changed" n
              | c -> misapplied "UnionTransparencyChanged" c)
              [ row "a union's transparent case" "`breaking-wire`" "`no-generated-shape-change`" ]

          rule
              "EnumAdded"
              "40"
              NoFamily
              (function
              | EnumAdded n -> n
              | c -> misapplied "EnumAdded" c)
              (function
              | EnumAdded n -> Additive, sprintf "closed set `%s` introduced." n, "—"
              | c -> misapplied "EnumAdded" c)
              (always noShape)
              (function
              | EnumAdded n -> sprintf "closed set added: %s" n
              | c -> misapplied "EnumAdded" c)
              [ row "an enum added" "`additive`" "`no-generated-shape-change`" ]

          rule
              "EnumRemoved"
              "41"
              NoFamily
              (function
              | EnumRemoved n -> n
              | c -> misapplied "EnumRemoved" c)
              (function
              | EnumRemoved n ->
                  BreakingWire, sprintf "closed set `%s` removed — its field must have changed type or gone." n, "—"
              | c -> misapplied "EnumRemoved" c)
              (always reference)
              (function
              | EnumRemoved n -> sprintf "closed set removed: %s" n
              | c -> misapplied "EnumRemoved" c)
              [ row "an enum removed" "`breaking-wire`" "`type-name-reference`" ]

          rule
              "EnumCaseAdded"
              "42"
              EnumSet
              (function
              | EnumCaseAdded(e, w) -> e + "." + w
              | c -> misapplied "EnumCaseAdded" c)
              (function
              | EnumCaseAdded(e, w) ->
                  Additive,
                  sprintf
                      "wire string `\"%s\"` added to closed set `%s` — additive on the wire, but a decoder that predates it REJECTS the value (`UNKNOWN_DU_CASE`), so the host-lag commitment applies exactly as it does to a kind."
                      w
                      e,
                  "VOCABULARY.md §4.3 (unknown-discriminator behaviour + host-lag)"
              | c -> misapplied "EnumCaseAdded" c)
              (always matching)
              (function
              | EnumCaseAdded(e, w) -> sprintf "enum case added: %s.\"%s\"" e w
              | c -> misapplied "EnumCaseAdded" c)
              [ row "an enum case added" "`additive`" "`exhaustive-match`" ]

          rule
              "EnumCaseRemoved"
              "43"
              EnumSet
              (function
              | EnumCaseRemoved(e, w) -> e + "." + w
              | c -> misapplied "EnumCaseRemoved" c)
              (function
              | EnumCaseRemoved(e, w) ->
                  BreakingWire,
                  sprintf
                      "wire string `\"%s\"` REMOVED from closed set `%s` — documents carrying it no longer validate."
                      w
                      e,
                  "VOCABULARY.md §4.2"
              | c -> misapplied "EnumCaseRemoved" c)
              (always matching)
              (function
              | EnumCaseRemoved(e, w) -> sprintf "enum case removed: %s.\"%s\"" e w
              | c -> misapplied "EnumCaseRemoved" c)
              [ row "an enum case removed" "`breaking-wire`" "`exhaustive-match`" ]

          rule
              "EnumHostMappingChanged"
              "44"
              NoFamily
              (function
              | EnumHostMappingChanged(n, _, _) -> n
              | c -> misapplied "EnumHostMappingChanged" c)
              (function
              | EnumHostMappingChanged(n, _, _) ->
                  HostSurfaceOnly,
                  sprintf
                      "`%s` host case names changed with its wire strings unchanged — `hostCases` is a hostSurface key (WIRE_FORMAT.md §13), carrying nothing observable on the wire. A source-compat event for F# consumers, not a wire one."
                      n,
                  "WIRE_FORMAT.md §13; Artifact.json (`hostCases` is hostSurface)"
              | c -> misapplied "EnumHostMappingChanged" c)
              // The host-side case NAMES moved: every arm that spelled one is now undefined,
              // and the set is no longer covered.
              (always matching)
              (function
              | EnumHostMappingChanged(n, _, _) -> sprintf "enum %s host case names changed" n
              | c -> misapplied "EnumHostMappingChanged" c)
              [ row "an enum's **host** case names moved" "`host-surface-only`" "`exhaustive-match`" ]

          rule
              "EnumCaseAnnotationsChanged"
              "45"
              NoFamily
              (function
              | EnumCaseAnnotationsChanged(e, w, _, _) -> e + "." + w
              | c -> misapplied "EnumCaseAnnotationsChanged" c)
              (function
              | EnumCaseAnnotationsChanged(e, w, b, a) ->
                  classifyAnnotations (sprintf "case `\"%s\"` of enum `%s`" w e) b a
              | c -> misapplied "EnumCaseAnnotationsChanged" c)
              (always noShape)
              (function
              | EnumCaseAnnotationsChanged(e, w, b, _) ->
                  sprintf "enum case annotations %s: %s.\"%s\"" (if b = "" then "declared" else "changed") e w
              | c -> misapplied "EnumCaseAnnotationsChanged" c)
              [ annotationRow "an enum case's annotation set" ]

          rule
              "RecordAdded"
              "50"
              NoFamily
              (function
              | RecordAdded n -> n
              | c -> misapplied "RecordAdded" c)
              (function
              | RecordAdded n ->
                  Additive,
                  sprintf "non-discriminated record `%s` introduced — reachable only from a field that also changed." n,
                  "—"
              | c -> misapplied "RecordAdded" c)
              (always noShape)
              (function
              | RecordAdded n -> sprintf "record added: %s" n
              | c -> misapplied "RecordAdded" c)
              [ row "a record added" "`additive`" "`no-generated-shape-change`" ]

          rule
              "RecordRemoved"
              "51"
              NoFamily
              (function
              | RecordRemoved n -> n
              | c -> misapplied "RecordRemoved" c)
              (function
              | RecordRemoved n -> BreakingWire, sprintf "record `%s` removed." n, "VOCABULARY.md §4.2"
              | c -> misapplied "RecordRemoved" c)
              (always reference)
              (function
              | RecordRemoved n -> sprintf "record removed: %s" n
              | c -> misapplied "RecordRemoved" c)
              [ row "a record removed" "`breaking-wire`" "`type-name-reference`" ]

          rule
              "FieldAdded"
              "60"
              NoFamily
              (function
              | FieldAdded(o, f) -> o.Key + "/" + f.Name
              | c -> misapplied "FieldAdded" c)
              (function
              | FieldAdded(owner, f) -> classifyFieldAdd owner f
              | c -> misapplied "FieldAdded" c)
              // A field on any owner is a record member.
              (always construction)
              (function
              | FieldAdded(o, f) -> sprintf "field added: %s.%s : %s (%s)" o.Describe f.Name f.Label f.OptClass
              | c -> misapplied "FieldAdded" c)
              [ row
                    "a **required** field added — every old document lacks it and is refused"
                    "`breaking-wire`"
                    "`full-literal-construction`"
                row "an **optional** field added" "`additive`" "`full-literal-construction`"
                row "a **host-only** field added" "`host-surface-only`" "`full-literal-construction`" ]

          rule
              "FieldRemoved"
              "61"
              NoFamily
              (function
              | FieldRemoved(o, n, _) -> o.Key + "/" + n
              | c -> misapplied "FieldRemoved" c)
              (function
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
              | c -> misapplied "FieldRemoved" c)
              (always construction)
              (function
              | FieldRemoved(o, n, _) -> sprintf "field removed: %s.%s" o.Describe n
              | c -> misapplied "FieldRemoved" c)
              [ row
                    "a field removed"
                    "`breaking-wire`; `host-surface-only` for a host-only field"
                    "`full-literal-construction`" ]

          rule
              "FieldTypeChanged"
              "62"
              NoFamily
              (function
              | FieldTypeChanged(o, n, _, _) -> o.Key + "/" + n
              | c -> misapplied "FieldTypeChanged" c)
              (function
              | FieldTypeChanged(owner, n, b, a) ->
                  match erasedIn b |> Option.orElse (erasedIn a) with
                  | Some tag ->
                      Unclassifiable,
                      sprintf
                          "field `%s` on %s changed type across an ERASED slot: %s → %s. The artifact does not state what a `%s` slot admits — that is the host codec's business, by design — so nothing here can say whether the admitted value sets differ, and a nested erased slot (a list element, a map value, a union argument) is as undecidable as a bare one. CHECK: does every value the old side accepted still decode, and does the corpus come back byte-identical? If both, this is a modelling improvement and not a wire event; if either fails, it is BREAKING (wire)."
                          n
                          owner.Describe
                          b.Label
                          a.Label
                          tag,
                      "Idl.THosted / TJson / TOpaque (content carried verbatim; not described by the artifact)"
                  | None when isIntToFloatWidening b a ->
                      // Phase 252 — a float slot admits every integer literal and a whole float
                      // renders as the same digits, so every old document decodes and re-encodes
                      // byte-identically, and an old emitter (writing integers) stays conformant.
                      // That is the table's definition of additive; the cost is host lag, as for
                      // a new enum case: a decoder that predates the widening refuses `2.5`.
                      Additive,
                      sprintf
                          "field `%s` on %s WIDENED: %s → %s. A float slot admits every integer, and a whole float renders as the same digits, so every existing document decodes and re-encodes byte-identically and every previously-conformant emitter stays conformant. Not breaking-for-emitters: no old emitter's output became invalid. The cost is host lag — a decoder that predates the widening refuses a fractional value such as `2.5` — exactly as for a new enum case."
                          n
                          owner.Describe
                          b.Label
                          a.Label,
                      "docs/idl-stability-classes.md (int → float widening, Phase 252); VOCABULARY.md §4.3 (host-lag)"
                  | None ->
                      BreakingWire,
                      sprintf
                          "field `%s` on %s changed type: %s → %s. A value that decoded no longer does."
                          n
                          owner.Describe
                          b.Label
                          a.Label,
                      "STABILITY.md wire-format section"
              | c -> misapplied "FieldTypeChanged" c)
              (fun _ c ->
                  match c with
                  | FieldTypeChanged(_, _, b, a) ->
                      if (erasedIn b).IsSome || (erasedIn a).IsSome then
                          [ GeneratedShapeUnreadable ]
                      else
                          construction
                  | c -> misapplied "FieldTypeChanged" c)
              (function
              | FieldTypeChanged(o, n, b, a) ->
                  sprintf "field type changed: %s.%s : %s -> %s" o.Describe n b.Label a.Label
              | c -> misapplied "FieldTypeChanged" c)
              [ row "a field's type moved" "`breaking-wire`" "`full-literal-construction`"
                row
                    "a field's type **widened from `int` to `float`** (anywhere in it — a list element, a map value, a union argument)"
                    "`additive`"
                    "`full-literal-construction`"
                row
                    "a field's type moved across an **erased** slot (`hosted` / `json` / `opaque`), at any depth — a list element, a map value, a union argument"
                    "`undecided`"
                    "`generated-shape-unreadable`" ]

          rule
              "FieldOptionalityChanged"
              "63"
              NoFamily
              (function
              | FieldOptionalityChanged(o, n, _, _) -> o.Key + "/" + n
              | c -> misapplied "FieldOptionalityChanged" c)
              (function
              | FieldOptionalityChanged(owner, n, b, a) ->
                  let d = owner.Describe

                  (match b.OptClass, a.OptClass with
                   // Phase 304 — tightening was `BreakingForEmitters` (a minor) and loosening
                   // `BreakingWire` (a major): the inverse of what old documents do. A class is a
                   // fact about an old document under the new vocabulary, never about the emitter
                   // alone.
                   | "optional", "required"
                   | "omitDefault", "required" ->
                       BreakingWire,
                       sprintf
                           "field `%s` on %s became REQUIRED (%s → %s) — every stored document that omitted it (absent, or sitting on its default) is now refused by the decoder, and every emitter that legitimately omitted it produces a refused document. Old documents stop decoding: a `/v2/` event."
                           n
                           d
                           b.Opt
                           a.Opt,
                       "docs/idl-stability-classes.md (a class is what an old document does, Phase 304); Idl.Decode (a required member absent is refused)"
                   | "required", "optional" ->
                       // Loosening. Every old document carries the member, decodes to the same
                       // value and re-encodes to the same bytes; every old emitter writes it, so
                       // stays conformant — the table's definition of additive. The consumer that
                       // relied on presence meets absence only in a document a NEW emitter writes,
                       // which is host lag (a decoder that predates the change refuses it), exactly
                       // as for a new enum case; on the F# axis the member becomes an `option`, which
                       // `full-literal-construction` carries.
                       Additive,
                       sprintf
                           "field `%s` on %s stopped being required (%s → %s) — every existing document carries it, decodes to the same value and re-encodes byte-identically, and every previously-conformant emitter (which always writes it) stays conformant. The cost is host lag: a consumer that relied on presence meets absence only in documents a NEW emitter writes, and a decoder that predates the change refuses those — as for a new enum case."
                           n
                           d
                           b.Opt
                           a.Opt,
                       "docs/idl-stability-classes.md (loosening, Phase 304); VOCABULARY.md §4.3 (host-lag)"
                   | "required", "omitDefault" ->
                       // Loosening to omit-at-default is NOT additive: the decoder reads a member
                       // sitting on the default and the encoder then omits it, so every stored
                       // document carrying the default value changes bytes on re-encode — the
                       // argument the moved-identity-default row already rests on.
                       BreakingWire,
                       sprintf
                           "field `%s` on %s stopped being required and became omit-at-default (%s → %s) — every old document still decodes to the same value, but one carrying the default re-encodes WITHOUT the member, so its bytes move: omit-at-default is wire-visible. A hash-chained store re-encoding an old document would not reproduce it."
                           n
                           d
                           b.Opt
                           a.Opt,
                       "Idl.Optionality.OmitDefault (omit-at-default is wire-visible); docs/idl-stability-classes.md (Phase 304)"
                   | "omitDefault", "omitDefault" ->
                       BreakingWire,
                       sprintf
                           "field `%s` on %s moved its identity default (%s → %s) — omit-at-default is WIRE-VISIBLE: every document sitting on the old default changes bytes, and every document carrying the new one loses a key. The single most easily mis-declared change in this table."
                           n
                           d
                           b.Opt
                           a.Opt,
                       "Idl.Optionality.OmitDefault (omit-at-default is wire-visible)"
                   | "hostOnly", _
                   | _, "hostOnly" ->
                       BreakingWire,
                       sprintf
                           "field `%s` on %s crossed the host-only boundary (%s → %s) — a slot appeared on, or vanished from, the wire."
                           n
                           d
                           b.Opt
                           a.Opt,
                       "WIRE_FORMAT.md §9"
                   | _ ->
                       BreakingWire,
                       sprintf "field `%s` on %s changed optionality (%s → %s)." n d b.Opt a.Opt,
                       "STABILITY.md wire-format section")
              | c -> misapplied "FieldOptionalityChanged" c)
              // Only the `optional` class emits an F# `option`, so the generated type moves
              // exactly when one side is optional and the other is not. Every other optionality
              // move (required <-> omitDefault) changes the ENCODER body and leaves the record
              // member's type where it was — decidable, so decided, rather than reported
              // conservatively.
              (fun ctx c ->
                  match c with
                  | FieldOptionalityChanged(owner, name, before, after) ->
                      if
                          (before.OptClass = "optional") <> (after.OptClass = "optional")
                          || requiredFlipped ctx owner name before after
                      then
                          construction
                      else
                          noShape
                  | c -> misapplied "FieldOptionalityChanged" c)
              (function
              | FieldOptionalityChanged(o, n, b, a) ->
                  sprintf "field optionality changed: %s.%s : %s -> %s" o.Describe n b.Opt a.Opt
              | c -> misapplied "FieldOptionalityChanged" c)
              // Each row names what an OLD document does under the new vocabulary (Phase 304):
              // tightening to `required` refuses the documents that omitted the member; loosening
              // to `optional` leaves every one decoding and re-encoding identically; loosening to
              // `omitDefault`, like a moved identity default, moves the bytes of every document
              // sitting on the default.
              [ row
                    "a field's optionality moved **into or out of** `optional`"
                    "`breaking-wire` when it became required (old documents that omitted it are refused); `additive` when `required` became `optional` (every old document carries it and re-encodes identically; the consumer that relied on presence meets absence only from a new emitter — host lag); `breaking-wire` between `optional` and `omitDefault` (absence changes meaning, or a default-valued member stops re-encoding)"
                    "`full-literal-construction`"
                row
                    "a field's optionality moved **between** `required` and `omitDefault`, or its identity default moved"
                    "`breaking-wire` — becoming required refuses the old documents that omitted it; becoming omit-at-default, or moving the default, re-encodes every old document sitting on the default without the member (omit-at-default is wire-visible)"
                    "`full-literal-construction` on a kind field with no authoring default — `mk<Kind>` takes a parameter for every required field, so the parameter leaves or arrives — else `no-generated-shape-change`"
                row
                    "a field crossed the **host-only** boundary"
                    "`breaking-wire`"
                    "`full-literal-construction` when it crossed `optional` too, else `no-generated-shape-change`" ]

          rule
              "FieldHostSurfaceChanged"
              "64"
              NoFamily
              (function
              | FieldHostSurfaceChanged(o, n, _, _) -> o.Key + "/" + n
              | c -> misapplied "FieldHostSurfaceChanged" c)
              (function
              | FieldHostSurfaceChanged(owner, n, b, a) when hostedSlotMoved b a ->
                  Unclassifiable,
                  sprintf
                      "field `%s` on %s changed a HOSTED slot's declaration (its host type or its codec). A hosted slot's codec IS its wire form, and the artifact does not state what the codec writes, so nothing here can say whether a document's bytes moved. CHECK: does every value the old codec wrote still decode under the new one, and does every document come back byte-identical? If both, this is host-surface only; if either fails, it is BREAKING (wire)."
                      n
                      owner.Describe,
                  "Idl.THosted (the codec is the wire form; Phase 252); docs/idl-stability-classes.md (anything crossing an erased slot)"
              | FieldHostSurfaceChanged(owner, n, _, _) ->
                  HostSurfaceOnly,
                  sprintf
                      "field `%s` on %s changed its hostSurface declaration only — the generated F#/TS signature moved, the wire did not. A recompile event for the reference host; invisible to every third-party codec."
                      n
                      owner.Describe,
                  "WIRE_FORMAT.md §13 (hostSurface is host-language spec, not wire spec)"
              | c -> misapplied "FieldHostSurfaceChanged" c)
              // The generated DECLARATION is readable whatever the wire verdict: a `hostSurface`
              // block states the F# host type outright (Phase 252). Only the `fsharp` member is
              // spelled by the generated record, so a codec or placeholder expression moving
              // alone leaves every site where it was — decidable, so decided, including on a
              // hosted slot whose WIRE verdict is undecided.
              (fun _ c ->
                  match c with
                  | FieldHostSurfaceChanged(_, _, before, after) ->
                      if hostTypeMoved before after then construction else noShape
                  | c -> misapplied "FieldHostSurfaceChanged" c)
              (function
              | FieldHostSurfaceChanged(o, n, _, _) -> sprintf "field hostSurface changed: %s.%s" o.Describe n
              | c -> misapplied "FieldHostSurfaceChanged" c)
              [ row
                    "a `fn` slot's `hostSurface` block moved"
                    "`host-surface-only`"
                    "`full-literal-construction` when its `fsharp` signature moved, else `no-generated-shape-change`"
                row
                    "a **hosted** slot's `hostSurface` block moved — its host type, its `encode` or its `decode`"
                    "`undecided`"
                    "`full-literal-construction` when its `fsharp` type moved, else `no-generated-shape-change`" ]

          rule
              "FieldAnnotationsChanged"
              "65"
              NoFamily
              (function
              | FieldAnnotationsChanged(o, n, _, _) -> o.Key + "/" + n
              | c -> misapplied "FieldAnnotationsChanged" c)
              (function
              | FieldAnnotationsChanged(owner, n, b, a) ->
                  classifyAnnotations (sprintf "field `%s` on %s" n owner.Describe) b a
              | c -> misapplied "FieldAnnotationsChanged" c)
              (always noShape)
              (function
              | FieldAnnotationsChanged(o, n, b, _) ->
                  sprintf "field annotations %s: %s.%s" (if b = "" then "declared" else "changed") o.Describe n
              | c -> misapplied "FieldAnnotationsChanged" c)
              [ annotationRow "a field's annotation set" ]

          rule
              "UnionCaseAnnotationsChanged"
              "66"
              NoFamily
              (function
              | UnionCaseAnnotationsChanged(u, c, _, _) -> u + "." + c
              | c -> misapplied "UnionCaseAnnotationsChanged" c)
              (function
              | UnionCaseAnnotationsChanged(u, c, b, a) -> classifyAnnotations (sprintf "case `%s` of `%s`" c u) b a
              | c -> misapplied "UnionCaseAnnotationsChanged" c)
              (always noShape)
              (function
              | UnionCaseAnnotationsChanged(u, c, b, _) ->
                  sprintf "union case annotations %s: %s.%s" (if b = "" then "declared" else "changed") u c
              | c -> misapplied "UnionCaseAnnotationsChanged" c)
              [ annotationRow "a union case's annotation set" ]

          rule
              "DefaultAdded"
              "70"
              NoFamily
              (function
              | DefaultAdded(kd, f, _) -> kd + "/" + f
              | c -> misapplied "DefaultAdded" c)
              (function
              | DefaultAdded(kd, f, _) ->
                  Additive,
                  sprintf
                      "smart-constructor default added for `%s.%s` — an AUTHORING default (Idl.IdlDefault), not the wire-visible omit-at-default. It changes what a host author gets when they say nothing; it does not change what the wire admits."
                      kd
                      f,
                  "Idl.IdlDefault (applied by the generated smart constructors)"
              | c -> misapplied "DefaultAdded" c)
              (fun ctx c ->
                  match c with
                  | DefaultAdded(kd, f, _) -> defaultConsequence ctx kd f
                  | c -> misapplied "DefaultAdded" c)
              (function
              | DefaultAdded(k, f, _) -> sprintf "authoring default added: %s.%s" k f
              | c -> misapplied "DefaultAdded" c)
              [ row
                    "an authoring default added"
                    "`additive`"
                    "`full-literal-construction` when the field is required — `mk<Kind>` loses the parameter, so every call site moves — else `no-generated-shape-change`" ]

          rule
              "DefaultRemoved"
              "71"
              NoFamily
              (function
              | DefaultRemoved(kd, f, _) -> kd + "/" + f
              | c -> misapplied "DefaultRemoved" c)
              (function
              | DefaultRemoved(kd, f, _) ->
                  BreakingForEmitters,
                  sprintf
                      "smart-constructor default REMOVED for `%s.%s` — host authoring code that relied on it now emits a different document (or fails to compile). Authoring-surface break; the wire contract is unchanged."
                      kd
                      f,
                  "Idl.IdlDefault"
              | c -> misapplied "DefaultRemoved" c)
              (fun ctx c ->
                  match c with
                  | DefaultRemoved(kd, f, _) -> defaultConsequence ctx kd f
                  | c -> misapplied "DefaultRemoved" c)
              (function
              | DefaultRemoved(k, f, _) -> sprintf "authoring default removed: %s.%s" k f
              | c -> misapplied "DefaultRemoved" c)
              [ row
                    "an authoring default removed"
                    "`breaking-for-emitters`"
                    "`full-literal-construction` when the field is required — `mk<Kind>` gains the parameter — else `no-generated-shape-change`" ]

          rule
              "DefaultChanged"
              "72"
              NoFamily
              (function
              | DefaultChanged(kd, f, _, _) -> kd + "/" + f
              | c -> misapplied "DefaultChanged" c)
              (function
              | DefaultChanged(kd, f, b, a) ->
                  BreakingForEmitters,
                  sprintf
                      "smart-constructor default for `%s.%s` moved (%s → %s) — every authoring site that omitted the field now emits a different document. The wire contract is unchanged; the emitted bytes are not."
                      kd
                      f
                      b
                      a,
                  "Idl.IdlDefault"
              | c -> misapplied "DefaultChanged" c)
              (always noShape)
              (function
              | DefaultChanged(k, f, b, a) -> sprintf "authoring default changed: %s.%s : %s -> %s" k f b a
              | c -> misapplied "DefaultChanged" c)
              [ row
                    "an authoring default changed"
                    "`breaking-for-emitters`"
                    "`no-generated-shape-change` — the parameter list is unchanged, the constructor's body is not" ]

          rule
              "SupportChanged"
              "80"
              NoFamily
              (function
              | SupportChanged(k, _, _) -> k
              | c -> misapplied "SupportChanged" c)
              (function
              | SupportChanged(k, b, a) ->
                  let what =
                      match b, a with
                      | None, _ -> "ADDED"
                      | _, None -> "REMOVED"
                      | _ -> "changed"

                  HostSurfaceOnly,
                  sprintf
                      "declared support `%s` %s — a doc block, a verbatim splice, a case refine, a kind projection or the host prelude is host-language SOURCE the generator splices, never a wire fact: no document's bytes move and no emitter's output changes. What changes is the GENERATED module, so regenerate against the new support and read the F# consequence beside this row."
                      k
                      what,
                  "SupportArtifact (support.json, Phase 114); docs/idl-stability-classes.md (declared support, Phase 293)"
              | c -> misapplied "SupportChanged" c)
              // A projection supplies the kind's record, encoder, decoder and constructor
              // verbatim, and a type splice adds members to the type group: a move there moves
              // generated declarations. A doc block, a refine (the final expression of one
              // decoder arm, built from binders already read), an encoder/decoder/accessor
              // splice (bodies) and the prelude move no declaration a consumer constructs.
              (fun _ c ->
                  match c with
                  | SupportChanged(k, _, _) ->
                      if k.StartsWith "projection:" || k = "splice:type" then
                          construction
                      else
                          noShape
                  | c -> misapplied "SupportChanged" c)
              (function
              | SupportChanged(k, b, a) ->
                  sprintf
                      "declared support %s: %s"
                      (match b, a with
                       | None, _ -> "added"
                       | _, None -> "removed"
                       | _ -> "changed")
                      k
              | c -> misapplied "SupportChanged" c)
              [ row
                    "a declared support entry (`support.json`) added, removed or changed — a doc block, a splice, a case refine, a kind projection, the host prelude"
                    "`host-surface-only` — host-language source the generator splices, never on the wire"
                    "`full-literal-construction` for a kind projection or the type splice (generated declarations move), else `no-generated-shape-change`" ] ]

    /// The rule for a change, by its union case name. Built once; a `Change` case with no rule
    /// is a defect this table reports at first use rather than a silent default.
    let private ruleOf: Change -> Rule =
        let byCase = rules |> List.map (fun r -> r.Case, r) |> Map.ofList

        let cases =
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<Change>, System.Reflection.BindingFlags.Public)
            |> Array.map (fun c -> c.Name)

        let missing = cases |> Array.filter (fun n -> not (byCase.ContainsKey n))

        if missing.Length > 0 then
            failwithf "the descriptor table has no rule for: %s" (String.concat ", " missing)

        fun (c: Change) ->
            let case, _ =
                Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(
                    c,
                    typeof<Change>,
                    System.Reflection.BindingFlags.Public
                )

            byCase[case.Name]

    /// Deterministic ordering. Sorted by a per-case rank then by the change's own
    /// key, so identical inputs produce byte-identical output regardless of map
    /// enumeration order. Both come off the descriptor table.
    let private sortKey (c: Change) : string * string =
        let r = ruleOf c
        r.Rank, r.Key c

    /// Every difference between two snapshots, sorted by rule rank then by the change's
    /// own key, so identical inputs give an identical list. A kind rename is INFERRED and
    /// reported beside its add and remove, never instead of them; annotation and category
    /// moves are reported only for members both revisions carry.
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
                      DefaultRemoved(kd, f, v)

              // Phase 293 — the declared support, paired by key.
              for KeyValue(key, v) in after.Support do
                  match Map.tryFind key before.Support with
                  | None -> SupportChanged(key, None, Some v)
                  | Some old when old <> v -> SupportChanged(key, Some old, Some v)
                  | Some _ -> ()

              for KeyValue(key, v) in before.Support do
                  if not (Map.containsKey key after.Support) then
                      SupportChanged(key, Some v, None) ]

        unordered |> List.sortBy sortKey

    // -----------------------------------------------------------------------
    // Classification — `STABILITY.md` + `VOCABULARY.md` §4, applied.
    // -----------------------------------------------------------------------

    /// Grade one change by its rule in the descriptor table. Total over `Change`, and
    /// context-free: the severity depends on the change value alone.
    let classify (c: Change) : Classification =
        let sev, why, cite = (ruleOf c).Verdict c

        { Change = c
          Severity = sev
          Rationale = why
          Citation = cite }

    /// Phase 293 — ONE precedence over the severities, read by `stabilityImpact`,
    /// `profileBump` and `verdictClass` alike: an undecided row dominates, because
    /// reporting the decidable remainder as the answer is how a `/v2/` event gets
    /// published as a minor; then the wire break, the emitter break, the addition,
    /// and last the host-surface move.
    let private precedence: Severity list =
        [ Unclassifiable; BreakingWire; BreakingForEmitters; Additive; HostSurfaceOnly ]

    /// The severity that heads a classification list under [[precedence]] — `None` for
    /// an empty list.
    let private headline (cs: Classification list) : Severity option =
        precedence
        |> List.tryFind (fun s -> cs |> List.exists (fun c -> c.Severity = s))

    /// The headline over the DECIDED rows alone — what the verdict becomes once every
    /// undecided row has been checked and found absorbable.
    let private decidedHeadline (cs: Classification list) : Severity option =
        headline (cs |> List.filter (fun c -> c.Severity <> Unclassifiable))

    /// The draft `stability_impact:` value — the roadmap front-matter vocabulary
    /// is `additive` / `breaking` / `null`, so this emits one of the first two.
    let internal stabilityImpact (cs: Classification list) : string =
        match headline cs with
        | Some BreakingWire
        | Some BreakingForEmitters -> "breaking"
        | Some Unclassifiable ->
            (match decidedHeadline cs with
             | Some BreakingWire
             | Some BreakingForEmitters -> "breaking"
             | _ -> "additive   ← ONLY IF every unclassifiable row below checks out; `breaking` otherwise")
        | _ -> "additive"

    /// The wire-profile recommendation. `core@1.x` is the profile-id grammar
    /// (`STABILITY.md` §15 sentinel strings); `/v1/` is the schema `$id` major.
    let internal profileBump (cs: Classification list) : string =
        let major =
            "`/v2/` MAJOR — the schema `$id` major segment moves. VOCABULARY.md §4.2 says avoid this after publication; do it pre-launch or not at all."

        match headline cs with
        | Some BreakingWire -> major
        | Some Unclassifiable ->
            (match decidedHeadline cs with
             | Some BreakingWire -> major
             | _ ->
                 "UNDECIDED — at least one change crosses an erased slot the artifact does not describe. `core@1.(x+1)` if the checks below pass; `/v2/` MAJOR if any of them fails.")
        | Some BreakingForEmitters ->
            "`core@1.(x+1)` profile MINOR on paper — but at least one change breaks EMITTERS, so the minor understates it. Treat every downstream emitter as needing a coordinated bump."
        | Some Additive -> "`core@1.(x+1)` profile minor — the `/v1/` major segment does not move (VOCABULARY.md §4.1)."
        | Some HostSurfaceOnly
        | None -> "no wire-profile movement — every change is host-surface only."

    // -----------------------------------------------------------------------
    // The host-strand report — `WIRE_FORMAT.md` §11 obligations per host class.
    // -----------------------------------------------------------------------

    /// What a roster host does with the wire, which decides the obligations a change
    /// raises for it.
    type HostRole =
        /// Encodes and decodes documents itself: every wire-touching change obliges its
        /// encoder, decoder and schema in the same change-set.
        | CodecHost
        /// Renders a tree it does not decode itself: obliged to add or drop a render arm on a
        /// NodeKind-set change, and only checked otherwise.
        | RenderProjection

    /// One entry of a vocabulary's host roster (`manifest.json` `hosts`).
    type Host =
        {
            /// The host's identifier, named in its obligation rows. The id `fuaran` is
            /// special: its presence switches on the UI tier's full checklist.
            Id: string
            /// Display only; `?` when the manifest omits it.
            Language: string
            /// Read from the manifest's `role`: `render-projection` gives `RenderProjection`,
            /// anything else (or nothing) gives `CodecHost`.
            Role: HostRole
        }

    /// How firmly an obligation binds. `Check` exists because the honest answer
    /// to several of these is conditional, and a report that stated them as
    /// `Required` would train its reader to skim.
    type Strength =
        /// The surface must change in the same change-set (`MUST` in the report).
        | Required
        /// Whether the surface is bound depends on something the artifact does not record;
        /// the author decides (`CHECK`).
        | Check
        /// The surface is not bound by this change (`n/a`); the weakest, so it loses every
        /// consolidation.
        | NotBound

    /// One surface a classified change obliges someone to touch.
    type Obligation =
        {
            /// The surface's label, e.g. `codec: <id> (<language>)`. The consolidated set
            /// de-duplicates on it, keeping the strongest `Strength`.
            Surface: string
            /// Printed as `MUST` / `CHECK` / `n/a`; the consolidated set sorts by it, strongest
            /// first.
            Strength: Strength
            /// What to do on that surface, or why it is only conditionally bound.
            Note: string
        }

    /// The UI vocabulary's §11.0 roster, hand-declared — ONE vocabulary's hosts.
    ///
    /// **Not a default since Phase 252.** It was the roster every report used when no
    /// manifest supplied one, so a vocabulary with none of these hosts was told to
    /// change all of them. [[run]] now takes the roster from the vocabulary's own
    /// manifest (`hosts`) and from nowhere else; a caller that wants this list passes
    /// it to [[report]] / [[obligations]] explicitly, which is what it is kept for.
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

    /// The §11 family a change reaches, off the descriptor table.
    let private familyOf (c: Change) = (ruleOf c).Family

    /// Does this change alter the NodeKind set? That is the one class that
    /// reaches the authoring veneers, the analyzer vocabulary, the native render
    /// arms and `manifest.kinds`.
    let private isKindSetChange (c: Change) = familyOf c = KindSet

    /// Does it alter a `$type` discriminator family OTHER than NodeKind
    /// (`FormFieldKind`, `ChartKind`, `Binding`, `Action`, `TreeOp` …)?
    let private isFamilyChange (c: Change) = familyOf c = DiscriminatorFamily

    let private isEnumSetChange (c: Change) = familyOf c = EnumSet

    /// The obligation set for one change, joined against the roster.
    ///
    /// The two rows worth reading rather than skimming are the veneer rows and
    /// the native render-arm row, because both are conditional and both have
    /// been got wrong in a downstream consumer before:
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
    ///
    /// **Which rows a roster earns (Phase 252).** Every row naming a corpus, a spec
    /// document, a veneer or an analyzer is the UI tier's §11 checklist, and is emitted
    /// only when the roster declares that tier's reference codec host (`fuaran`). A
    /// roster without it — another vocabulary's, or the EMPTY roster a vocabulary with
    /// no manifest `hosts` gets — is obliged by the rows that hold for any vocabulary:
    /// each declared host, the generated layer, the artifact, and the profile and
    /// emitter rows a breaking verdict carries.
    let obligations (roster: Host list) (c: Classification) : Obligation list =
        let codecHosts = roster |> List.filter (fun h -> h.Role = CodecHost)
        let projections = roster |> List.filter (fun h -> h.Role = RenderProjection)
        let uiTier = roster |> List.exists (fun h -> h.Id = "fuaran")
        let ch = c.Change

        if not (touchesWire c) then
            [ { Surface = "reference host (F#) regeneration"
                Strength = Required
                Note =
                  "host-surface only — regenerate the generated layer and recompile. No codec host, corpus fixture or spec row is obliged." } ]
        elif not uiTier then
            [ if roster.IsEmpty then
                  { Surface = "hosts: none declared"
                    Strength = Check
                    Note =
                      "the vocabulary's manifest declares no `hosts`, so no codec host is obliged by name. Every host generated from this vocabulary (`Gen.fsharpModule`, `Gen.typescriptModule`, `Gen.jsonSchema`) regenerates in the same change-set; declare `hosts` in the manifest to have each named here." }

              for h in codecHosts do
                  { Surface = sprintf "codec: %s (%s)" h.Id h.Language
                    Strength = Required
                    Note = "encoder + decoder + schema shape, same change-set, pinned to the vocabulary's documents." }

              for h in projections do
                  { Surface = sprintf "render arm: %s (%s)" h.Id h.Language
                    Strength = (if isKindSetChange ch then Required else Check)
                    Note =
                      "a projection that models the changed family as a closed type gains or loses an arm; one that does not is unaffected." }

              { Surface = "generated layer: regenerate"
                Strength = Required
                Note = "regenerate every generated module and schema from the new vocabulary, and recompile." }

              { Surface = "artifact: idl.json"
                Strength = Required
                Note =
                  "re-render the vocabulary artifact beside the vocabulary it projects; a regenerate-and-byte-compare guard fails when the committed artifact and a fresh emission disagree." }

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

    let private summarise (c: Change) : string = (ruleOf c).Summary c

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

    /// Phase 293 — `run` with each side's support document text, or `None` for a side with none.
    let runWith
        (manifestText: string option)
        (oldText: string)
        (newText: string)
        (oldSupport: string option)
        (newSupport: string option)
        : Result<string, string> =
        let roster =
            manifestText
            |> Option.bind (fun t -> Json.parse t |> Result.toOption)
            |> Option.bind rosterFrom
            |> function
                | Some hs -> "manifest.json `hosts`", hs
                | None ->
                    (match manifestText with
                     | None -> "none declared (no manifest given)"
                     | Some _ -> "none declared (the manifest carries no `hosts` key)"),
                    []

        parseWith oldText oldSupport
        |> Result.mapError (fun e -> "old: " + e)
        |> Result.bind (fun before ->
            parseWith newText newSupport
            |> Result.mapError (fun e -> "new: " + e)
            |> Result.map (fun after -> report (fst roster) (snd roster) before after))

    /// Whole-pipeline entry: two `idl.json` texts and an optional `manifest.json`
    /// text (used only for the roster).
    ///
    /// The roster is the manifest's `hosts` and nothing else (Phase 252): absent, it
    /// is EMPTY, and the report says so, rather than falling back to one vocabulary's
    /// hosts ([[declaredRoster]]) for every vocabulary.
    ///
    /// No support documents are read. `Error` names the side (`old:` / `new:`) that did not parse.
    let run (manifestText: string option) (oldText: string) (newText: string) : Result<string, string> =
        let roster =
            manifestText
            |> Option.bind (fun t -> Json.parse t |> Result.toOption)
            |> Option.bind rosterFrom
            |> function
                | Some hs -> "manifest.json `hosts`", hs
                | None ->
                    (match manifestText with
                     | None -> "none declared (no manifest given)"
                     | Some _ -> "none declared (the manifest carries no `hosts` key)"),
                    []

        parse oldText
        |> Result.mapError (fun e -> "old: " + e)
        |> Result.bind (fun before ->
            parse newText
            |> Result.mapError (fun e -> "new: " + e)
            |> Result.map (fun after -> report (fst roster) (snd roster) before after))

    // -----------------------------------------------------------------------
    // Phase 127 — ONE classifier entry point, the F# consequence table, and the
    // wire-profile bump.
    //
    // Everything above answers "what does this change do to a DOCUMENT", and says
    // so in prose for a phase author to read. Three things were missing, and each
    // is a different kind of gap:
    //
    //  - There was no entry point over two `Idl` VALUES. The pipeline —
    //    `Artifact.render` -> `parse` -> `changes` -> `classify` -> read the
    //    severities — existed only as a private helper inside this repo's own test
    //    file, whose comment already described it as "as the CLI does" of a CLI
    //    that did not exist. So every gate that wanted the classification
    //    re-derived those five steps, and one that got a step wrong got a
    //    plausible answer rather than an error.
    //
    //  - `profileBump` returns PROSE. That is the right output for a human and it
    //    is unbranchable: nothing could ask "does this move the major", so nothing
    //    called `Versioning.bump` at all — the very function whose `Evolution` /
    //    `Profile` vocabulary this classification exists to feed.
    //
    //  - The F# CONSEQUENCE of a change was stated nowhere, although it is the
    //    half a consuming host feels first and it does NOT follow from the wire
    //    verdict. An OPTIONAL field added to a kind is `Additive` on the wire and
    //    still stops every full record literal compiling; a `hostSurface` edit is
    //    invisible on the wire and moves a generated field's type. External
    //    surface guards and corpus gates re-derive that mapping independently,
    //    each from its own reading; this is the one table they can cite instead.
    //
    // Nothing here becomes authoritative: the module stays advisory (it writes no
    // file, bumps no version and gates no build), for the reason at the head of
    // this file — a classifier that applied itself would make the hand-declared
    // classification unfalsifiable.
    // -----------------------------------------------------------------------

    /// The F# consequence set of one classified change, given the kind-field optionality
    /// lookup the snapshot pair supplies (Phase 293: an authoring default's consequence
    /// depends on whether the field is required, which the `Change` does not carry).
    ///
    /// Keyed on the CHANGE rather than on its severity, because the two axes are
    /// independent by construction: `FieldHostSurfaceChanged` is `HostSurfaceOnly`
    /// on the wire and a construction break here, and a `FieldAdded` that is
    /// `Additive` on the wire is a construction break all the same.
    ///
    /// `StalePackageSlot` rides every shape change rather than standing alone — see
    /// its own note.
    let private consequencesWith (ctx: Context) (c: Classification) : FSharpConsequence list =
        (ruleOf c.Change).Consequence ctx c.Change

    /// The F# consequence set of one classified change, with no snapshot to ask: an
    /// authoring default is read as a required field's, and a required-ness move as one
    /// with no default to stand in (see `defaultConsequence` / `requiredFlipped`).
    let consequences (c: Classification) : FSharpConsequence list = consequencesWith noContext c

    /// The `Versioning.Evolution` a classification list amounts to.
    ///
    /// It DELEGATES the additive-vs-breaking decision to `Versioning.classify`
    /// rather than restating that rule, by handing it the two subject sets that
    /// function compares: the members a revision RETIRES and the members it
    /// INTRODUCES. A change to the rule therefore changes this answer too, which is
    /// the point of routing through it — one rule, in one place, with `bump` reading
    /// it.
    ///
    /// The split is the severity's:
    ///
    ///  - `BreakingWire` RETIRES — a document that was valid is not, or its bytes
    ///    moved, so an older consumer cannot interpret the result and the major must
    ///    move.
    ///  - `Additive` and `BreakingForEmitters` INTRODUCE. The second reads oddly
    ///    until you ask whose profile it is: every existing document still decodes,
    ///    so a consumer negotiating the profile is `Behind` rather than `Foreign`,
    ///    and the minor is the honest answer to THAT question. What the minor does
    ///    not say is that emitters need a coordinated bump — which is why the
    ///    verdict carries `BreaksEmitters` separately instead of folding it in here
    ///    and calling the format broken.
    ///  - `HostSurfaceOnly` moves no profile at all.
    ///  - `Unclassifiable` moves nothing here either, and stops the bump outright —
    ///    see `bumpProfile`.
    let evolution (cs: Classification list) : Versioning.Evolution =
        let subjects pick =
            cs |> List.filter pick |> List.map (fun c -> summarise c.Change) |> Set.ofList

        let retired = subjects (fun c -> c.Severity = BreakingWire)

        let introduced =
            subjects (fun c ->
                match c.Severity with
                | Additive
                | BreakingForEmitters -> true
                | _ -> false)

        Versioning.classify retired introduced

    /// The whole classification of one revision pair: the rows, the wire evolution,
    /// the F# consequence set, and the two prose drafts the report already emitted.
    type Verdict =
        {
            /// Every classified row, in `changes` order; empty means the two revisions
            /// describe the same contract (`VerdictClass.Unchanged`).
            Changes: Classification list
            /// The wire evolution as `Versioning.classify` decides it — the input
            /// `Versioning.bump` takes.
            Evolution: Versioning.Evolution
            /// The rows the artifact cannot decide. Non-empty means no profile
            /// answer is available — NOT that the answer is "additive".
            Undecided: Classification list
            /// At least one row keeps every existing document valid AND stops a
            /// conformant emitter conforming. Carried separately because the profile
            /// minor cannot express it — see `evolution`.
            BreaksEmitters: bool
            /// Every F# consequence class any row exhibits, de-duplicated, in table
            /// order.
            FSharpConsequences: FSharpConsequence list
            /// The roadmap front-matter draft — `stabilityImpact`.
            StabilityImpact: string
            /// The prose wire-profile recommendation — `profileBump`.
            ProfileAdvice: string
        }

    /// The verdict over two read snapshots.
    let verdictOf (before: Snapshot) (after: Snapshot) : Verdict =
        let cs = changes before after |> List.map classify

        // Phase 293 — an authoring default's F# consequence depends on the field's
        // optionality (a `mk<Kind>` parameter exists for a required field with no default),
        // which the `Change` does not carry and the snapshots do.
        let ctx: Context =
            { Optionality =
                fun kind fieldName ->
                    [ after; before ]
                    |> List.tryPick (fun snap ->
                        snap.Kinds
                        |> Map.tryFind kind
                        |> Option.bind (fun fs -> fs |> List.tryFind (fun f -> f.Name = fieldName))
                        |> Option.map (fun f -> f.OptClass))
              HasDefault =
                fun kind fieldName ->
                    [ after; before ]
                    |> List.exists (fun snap -> snap.Defaults.ContainsKey(kind, fieldName)) }

        let exhibited = cs |> List.collect (consequencesWith ctx) |> Set.ofList

        { Changes = cs
          Evolution = evolution cs
          Undecided = cs |> List.filter (fun c -> c.Severity = Unclassifiable)
          BreaksEmitters = cs |> List.exists (fun c -> c.Severity = BreakingForEmitters)
          FSharpConsequences = allConsequences |> List.filter exhibited.Contains
          StabilityImpact = stabilityImpact cs
          ProfileAdvice = profileBump cs }

    /// Phase 293 — the verdict over two artifact texts WITH each side's support document
    /// text (`None` for a side with none): a projection or refine edit beside an unchanged
    /// vocabulary classifies `host-surface`, where the artifact-only door reads `unchanged`.
    let classifyArtifactsWith
        (beforeText: string)
        (afterText: string)
        (beforeSupport: string option)
        (afterSupport: string option)
        : Result<Verdict, string> =
        parseWith beforeText beforeSupport
        |> Result.mapError (fun e -> "old: " + e)
        |> Result.bind (fun before ->
            parseWith afterText afterSupport
            |> Result.mapError (fun e -> "new: " + e)
            |> Result.map (verdictOf before))

    /// The verdict over two `idl.json` TEXTS — the committed-artifact door, which
    /// works across revisions whose F# vocabulary no longer compiles.
    ///
    /// No support documents are read, so a support-only edit reads `unchanged` here. `Error`
    /// names the side (`old:` / `new:`) that did not parse.
    let classifyArtifacts (beforeText: string) (afterText: string) : Result<Verdict, string> =
        classifyArtifactsWith beforeText afterText None None

    /// Phase 293 — the in-process door with each side's support document (rendered through
    /// `SupportArtifact.render`, the published shape, for the reason `classifyDiff` renders).
    let classifyDiffWith
        (before: Idl)
        (after: Idl)
        (beforeSupport: SupportDocument option)
        (afterSupport: SupportDocument option)
        : Result<Verdict, string> =
        classifyArtifactsWith
            (Artifact.render before)
            (Artifact.render after)
            (beforeSupport |> Option.map SupportArtifact.render)
            (afterSupport |> Option.map SupportArtifact.render)

    /// The verdict over two `Idl` VALUES — the in-process door, for a caller holding
    /// both revisions as values (a proposal applied to a vocabulary, a generated
    /// pair under test).
    ///
    /// It goes THROUGH `Artifact.render`, deliberately: the artifact is the
    /// published contract and the render is lossy by design (it elides authored
    /// ordering of sorted collections, flags host-surface), so classifying the
    /// values directly would classify more than the contract does. The `Error`
    /// branch is reachable only if the artifact encoder and its reader disagree —
    /// a defect in this package rather than in the caller's input, since the round
    /// trip is certified — and it is returned rather than raised so both doors have
    /// one shape.
    let classifyDiff (before: Idl) (after: Idl) : Result<Verdict, string> =
        classifyArtifacts (Artifact.render before) (Artifact.render after)

    /// The one-word class a gate branches on.
    [<RequireQualifiedAccess>]
    type VerdictClass =
        /// No rows: the two revisions describe the same contract.
        | Unchanged
        /// Rows, none of them observable on the wire.
        | HostSurface
        /// Every wire-observable row is additive.
        | Additive
        /// At least one row breaks the wire, or breaks emitters.
        | Breaking
        /// At least one row the artifact cannot decide. Takes precedence over every
        /// other class: an undecided row makes the whole verdict undecided, because
        /// reporting the decidable remainder as the answer is how a `/v2/` event
        /// gets published as a minor.
        | Undecided

    /// The class of a verdict, by the one severity precedence the report's drafts also read:
    /// any undecided row makes it `Undecided`, then a wire or emitter break `Breaking`, then
    /// `Additive`, then `HostSurface`; no rows at all is `Unchanged`.
    let verdictClass (v: Verdict) : VerdictClass =
        // Phase 293 — the ONE precedence `stabilityImpact` and `profileBump` read too.
        match headline v.Changes with
        | Some Unclassifiable -> VerdictClass.Undecided
        | None -> VerdictClass.Unchanged
        | Some BreakingWire
        | Some BreakingForEmitters -> VerdictClass.Breaking
        | Some Severity.Additive -> VerdictClass.Additive
        | Some HostSurfaceOnly -> VerdictClass.HostSurface

    /// The stable label of a verdict class — the CLI's `--expect` vocabulary. A
    /// contract: a spec home's gate passes or greps these strings.
    let classLabel =
        function
        | VerdictClass.Unchanged -> "unchanged"
        | VerdictClass.HostSurface -> "host-surface"
        | VerdictClass.Additive -> "additive"
        | VerdictClass.Breaking -> "breaking"
        | VerdictClass.Undecided -> "undecided"

    /// Every verdict class, in the order a report and the CLI usage list them.
    let allClasses: VerdictClass list =
        [ VerdictClass.Unchanged
          VerdictClass.HostSurface
          VerdictClass.Additive
          VerdictClass.Breaking
          VerdictClass.Undecided ]

    /// Read a label back. `None` on anything else — an unrecognised `--expect` is
    /// refused rather than read as a default, because defaulting would make a
    /// misspelled assertion pass.
    let classOfLabel (s: string) : VerdictClass option =
        allClasses |> List.tryFind (fun c -> classLabel c = s)

    /// The process exit code a branching gate reads when it has NOT declared what it
    /// expects: `0` for anything a consumer absorbs by repinning, `3` for a break,
    /// `4` for undecided. Three codes rather than a boolean, because "I cannot tell"
    /// and "this breaks" want different handling — collapsing them is how the
    /// erased-slot case gets treated as a break and the report stops being read.
    let exitCode =
        function
        | VerdictClass.Unchanged
        | VerdictClass.HostSurface
        | VerdictClass.Additive -> 0
        | VerdictClass.Breaking -> 3
        | VerdictClass.Undecided -> 4

    /// The F# consequence table, rendered from the code that decides it. `docs/`
    /// carries the same table for a reader with no build; a test asserts the
    /// document names every class, so the two cannot drift silently.
    let consequenceTable: string =
        let sb = System.Text.StringBuilder()
        sb.Append("F# consequence classes\n\n") |> ignore

        sb.Append(
            "What a classified IDL change does to a consumer's F# source compiled against the\n"
            + "generated structural layer. Independent of the wire severity reported beside it.\n\n"
        )
        |> ignore

        for c in allConsequences do
            sb.Append("  ").Append(consequenceLabel c).Append("\n        ") |> ignore
            sb.Append(consequenceWhy c).Append("\n") |> ignore

        sb.ToString()

    /// Phase 293 — the mapping table `docs/idl-stability-classes.md` carries, RENDERED from the
    /// descriptor table the classifier runs on, so the document and the code cannot drift: the
    /// test suite holds the document's generated section byte-equal to this string. One row per
    /// documented shape of each `Change` case, in the classifier's own rank order, with the case
    /// name beside it so a reader can go from a report line to the rule and back.
    let mappingTable: string =
        let header =
            [ "| Change | Wire severity | F# consequence | Classifier case |"
              "|---|---|---|---|" ]

        let rows =
            [ for r in rules do
                  for d in r.Doc do
                      sprintf "| %s | %s | %s | `%s` |" d.Subject d.Wire d.FSharp r.Case ]

        String.concat "\n" (header @ rows) + "\n"

    /// The branchable block the CLI prints under the advisory report: the class, the
    /// wire evolution, the emitter warning the minor cannot carry, and the F#
    /// consequence set with the reason each applies.
    ///
    /// A function of the `Verdict` alone rather than of the snapshots, so it composes
    /// onto `report`'s output without a second roster resolution — see `runVerdict`.
    let verdictBlock (v: Verdict) : string =
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "## Verdict class"
        line ""
        line (sprintf "class:            %s" (classLabel (verdictClass v)))

        line (
            sprintf
                "wire evolution:   %s"
                (match v.Evolution with
                 | Versioning.Additive [] -> "no movement"
                 | Versioning.Additive added -> sprintf "additive — %d introduced" (List.length added)
                 | Versioning.Breaking(removed, added) ->
                     sprintf "BREAKING — %d retired, %d introduced" (List.length removed) (List.length added))
        )

        line (sprintf "breaks emitters:  %s" (if v.BreaksEmitters then "yes" else "no"))

        line (
            sprintf
                "F# consequences:  %s"
                (if v.FSharpConsequences.IsEmpty then
                     "none"
                 else
                     v.FSharpConsequences |> List.map consequenceLabel |> String.concat ", ")
        )

        line ""

        for c in v.FSharpConsequences do
            line (sprintf "  %s" (consequenceLabel c))
            line (sprintf "        %s" (consequenceWhy c))

        line ""
        sb.ToString()

    /// Phase 293 — `runVerdict` with each side's support document text.
    let runVerdictWith
        (manifestText: string option)
        (oldText: string)
        (newText: string)
        (oldSupport: string option)
        (newSupport: string option)
        : Result<string * Verdict, string> =
        runWith manifestText oldText newText oldSupport newSupport
        |> Result.bind (fun reportText ->
            classifyArtifactsWith oldText newText oldSupport newSupport
            |> Result.map (fun v -> reportText + verdictBlock v, v))

    /// Whole-pipeline entry for a caller that wants BOTH the text and something to
    /// branch on: `run`'s advisory report, the verdict block under it, and the
    /// `Verdict` value itself.
    ///
    /// It calls `run` rather than reproducing its roster resolution, so there stays
    /// exactly one place that decides whether the manifest carries a host roster.
    /// The cost is reading the two artifacts twice, which for two files on a gate's
    /// command line is not a cost.
    ///
    /// No support documents are read (`runVerdictWith` reads them).
    let runVerdict
        (manifestText: string option)
        (oldText: string)
        (newText: string)
        : Result<string * Verdict, string> =
        runVerdictWith manifestText oldText newText None None

    /// What `bumpProfile` answers: a profile to publish, or the undecided rows that stop one
    /// being named.
    [<RequireQualifiedAccess>]
    type Bump =
        /// Every row was decided: the base profile under `Versioning.bump` of the verdict's
        /// evolution (unchanged when nothing on the wire moved).
        | Bumped of Versioning.Profile
        /// At least one row crosses an erased slot; `rows` are those rows, to check by hand
        /// before any profile can be named.
        | Undecided of rows: Classification list

    /// The profile a `baseProfile` bumps to under a verdict — `Versioning.bump`
    /// applied to `evolution`'s answer, which is the whole reason this module
    /// produces an `Evolution` at all.
    ///
    /// An UNDECIDED verdict yields no profile, and that is the load-bearing half: a
    /// function that returned `baseProfile` unchanged, or a minor bump, would hand a
    /// caller a number to publish for a revision whose class nobody has established.
    let bumpProfile (baseProfile: Versioning.Profile) (v: Verdict) : Bump =
        match v.Undecided with
        | [] -> Bump.Bumped(Versioning.bump baseProfile v.Evolution)
        | rows -> Bump.Undecided rows
