namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core

/// Phase 511 — where a kind's object sits in the node envelope: under one key of the envelope
/// object (`{ "id", "kind": { … } }`), or merged into it (`{ "$type", "id", …kind members }`).
/// Decided once at resolution from the vocabulary's declared envelope shape; the printers read
/// this rather than each matching the shape.
[<RequireQualifiedAccess>]
type internal KindPlacement =
    /// The kind object is the value of the named envelope key.
    | NestedUnder of key: string
    /// The kind's discriminator and members are the envelope object's own keys.
    | Merged

/// Phase 511 — the node envelope's object layout: where the kind sits and the envelope's own
/// declared members (`idl.NodeFields`, in declared order).
type internal EnvelopeLayout =
    { Kind: KindPlacement
      Members: IdlField list }

/// Phase 511 — how one union case is laid out on the wire: discriminator-tagged (the default),
/// or BARE — its single field's value with no envelope (a declared transparent case). A declared
/// transparent case that does not carry exactly one field has no bare layout; the refusal is
/// carried, not raised, so a backend that never prints the case (the JSON schema's `oneOf`
/// lists the fields it has) is not refused by a printer that would.
[<RequireQualifiedAccess>]
type internal CaseLayout =
    | Tagged
    | Bare of Result<IdlField, CodegenError>

/// Phase 732 — an inline record as the field holding it REFERS to it: the declaration, the type
/// arguments the reference applies, and the members it lays on the holding object at that
/// instantiation. A member naming one of the record's parameters is here at the argument, so a
/// backend that reads [[Fields]] lays out and reads the instantiation without binding anything
/// itself; a holder that passes its own parameter down leaves that variable in [[Args]] and in the
/// member types, for the holder's own instantiation to bind.
type internal InlineRecord =
    {
        /// The declaration: its name and annotations, its fields as declared.
        Decl: IdlRecord
        /// The type arguments the reference applies, in position.
        Args: IdlType list
        /// The members on the holding object, each at the reference's instantiation.
        Fields: IdlField list
    }

/// Phase 474 (DECISIONS.md D220) — an INLINE declaration a field holds: its members are keys of the
/// object holding the field, so no printer sees a nested key. An inline record is read and written
/// member by member from that object; a presence-keyed union's case is the one whose key is
/// present ([[UnionDiscrimination.ByPresence]]), its members and its synthesised members laid out
/// in the union's roster order.
[<RequireQualifiedAccess>]
type internal InlineSlot =
    | Record of InlineRecord
    | Keyed of IdlUnion * KeyedCase list

/// Phase 732 — a generic record instantiated at the arguments a vocabulary names, as
/// [[Instantiation]] is a generic union: its instance name, the declaration, and the binding.
type internal RecordInstantiation =
    { Name: string
      Record: IdlRecord
      Subst: Map<string, IdlType> }

/// A generic union instantiated at the arguments a vocabulary names: its instance name (the
/// declaration's name plus its mangled arguments), the declaration, and the parameter binding.
type internal Instantiation =
    { Name: string
      Union: IdlUnion
      Subst: Map<string, IdlType> }

/// Phase 525 — what a kind selection reaches ON THE WIRE, monomorphised: the closed types (no
/// type variable left) a model of the wire declares or iterates, in FIRST-REACH order. The walk
/// starts at the node, takes the envelope's wire fields and then the selected kinds' in the
/// caller's order; a record or a union instance is placed when it is first met, before what it
/// reaches, and a list or a map after its element. A host-only field is never on the wire and
/// reaches nothing; a hosted slot reaches its DECLARED wire form (Phase 252), the value every
/// host's decoder checks, and nothing when it declares none. Two types that are one wire type
/// (a hosted slot and its declared wire, a `TFn` and a `TClosure`) are one entry.
///
/// Phase 732 — a record is an entry per INSTANTIATION, as a union is: `Ranged<float>` and
/// `Ranged<int>` are two, each reaching its fields at its own arguments. A declaration whose
/// recursion grows its type argument has no finite set of instances; the walk stops following it at
/// a fixed depth of nested instantiations and names it in [[Grows]], so the reach is always finite.
///
/// This is the reach the F* model is printed from. The host reach above ([[Decl.Records]] and
/// its siblings) answers a different question — which declarations the host SOURCE must state,
/// including those named only by a signature or a host-only field — and is a set in declaration
/// order; this one is a sequence, because the model's order is the order it was reached in.
type internal WireReach =
    {
        /// `TNode`, then everything the node envelope reaches, in first-reach order.
        FromEnvelope: IdlType list
        /// Everything the selected kinds reach beyond [[FromEnvelope]], in first-reach order.
        FromKinds: IdlType list
        /// The enums met, in first-reach order.
        Enums: string list
        /// The first generic declaration the walk stopped following, as `union '<name>'` or
        /// `record '<name>'`: its instantiations were still new [[Decl.instantiationDepth]] deep on
        /// one path, which is a recursion that grows its type argument. `None` for every other
        /// vocabulary, whose reach is then complete.
        Grows: string option
    }

/// Phase 525 — an IDL-declared default VALUE as a typed literal: the value at the type it is
/// declared at, every admissibility question already answered. [[Decl.literal]] is the ONE place
/// a default is admitted or refused; each backend prints what it admits (the F# backend's
/// `fsDefaultLit`, the F* target's `defaultLit`), and a backend that cannot spell an admitted
/// literal says so as its own boundary, never by deciding admissibility again. A record's and a
/// union case's members stay as authored: the presence rules that read them are the printers'.
[<RequireQualifiedAccess>]
type internal Literal =
    | Str of string
    | Int of int
    /// A finite float — also an integer authored at a float slot.
    | Float of float
    | Bool of bool
    /// The enum's HOST case, resolved from the wire spelling the value carries.
    | Enum of enumName: string * case: string
    | EmptyList
    /// The EMPTY map (Phase 539) — admitted where the field's own type is the map, never nested in a
    /// record or union literal (see [[FSharpDefaults.fsDefaultLit]]).
    | EmptyMap
    /// A JSON value at a `TJson` slot (Phase 539) — every string well-formed and every number finite;
    /// admitted where the field's own type is the JSON slot, never nested.
    | Json of JVal
    | Record of IdlRecord * args: IdlType list * authored: (string * IdlValue) list
    | Union of IdlUnion * args: IdlType list * IdlUnionCase * authored: (string * IdlValue) list
    /// Phase 656 — a 64-bit integer (D251.3), read from its canonical decimal text.
    | Int64 of int64
    /// Phase 656 — a string-borne scalar every host binds to its canonical text: a decimal, a time
    /// of day or a duration (D251.4, D251.6). The type is carried for the target that refines the
    /// text by the type's grammar (the F* model).
    | Text of scalar: IdlType * text: string
    /// Phase 656 — bytes (D251.5): the canonical base64 text and the bytes it spells.
    | Bytes of text: string * bytes: byte[]
    /// Phase 656 — a tuple at its declared arity. Its items stay as authored, as a record's members
    /// do: each is admitted at its component type by the traversal that prints it.
    | Tuple of components: IdlType list * items: IdlValue list
    /// Phase 656 — the EMPTY set and the EMPTY keyed map, admitted where the member's own type is the
    /// collection and never nested, as the empty map is (Phase 539).
    | EmptySet
    | EmptyKeyedMap

/// Phase 524 (carried from 511.t4) — WHERE a member's value is asked for. The sites differ in
/// exactly two policies, and this is where they are stated, once: what a HOST-ONLY member takes,
/// and what a REQUIRED member with no value is.
[<RequireQualifiedAccess>]
type internal ValueSite =
    /// A member inside a declared default LITERAL (a record's or a union case's): the literal is
    /// used in pattern position too, so a host-only member (its placeholder is an expression) and
    /// a required member with no authored value are refused.
    | Literal
    /// A field of a smart constructor or a derived default record: a host-only member takes its
    /// placeholder, a required member with no declared default is the caller's PARAMETER, and an
    /// omit-default member takes its identity default whatever is declared beside it.
    | Constructor
    /// A field of a scaffolded authored VALUE: a host-only member takes its placeholder (a wire
    /// value at its name is not its value), and a required member with no value is refused.
    | Scaffold

/// Phase 524 (511.t4) — the value one member takes at a [[ValueSite]], decided by
/// [[Decl.memberValue]] and printed by each backend (F#, TypeScript, F*), so no backend restates
/// a presence policy.
[<RequireQualifiedAccess>]
type internal MemberValue =
    /// The member carries this value as it is (a required member's value, an omit-default
    /// member's authored value).
    | Carries of IdlValue
    /// An OPTIONAL member carrying this value (F# `Some(…)`; TypeScript the value).
    | Optional of IdlValue
    /// An omit-default member at its identity default.
    | IdentityDefault of IdlValue
    /// An optional member with no value (F# `None`; TypeScript the member is omitted).
    | Absent
    /// A host-only member: its `TFn` placeholder (absent from the TypeScript object).
    | Placeholder
    /// A required member with no value at a constructor: the caller passes it.
    | Parameter
    /// The site refuses the member, naming the value it was offered (`VAbsent` when none).
    | Refused of IdlValue

/// Phase 511 — THE ONE RESOLUTION of a vocabulary for one kind selection (B §8 S2). Built once
/// by [[Decl.resolve]] and handed to every emitter, which reads the declaration graph from here
/// and never re-walks the `Idl`: what a selection reaches, which declarations carry the message
/// parameter, which take wire equality, which hold a node, the generic instantiations, the
/// envelope layout. Each answer used to be computed by its own walk in the emitter that needed
/// it (seven walks and four helpers); two backends can no longer disagree about reach, because
/// there is one reach.
///
/// Orders are the orders the emitters always used, which the byte guards hold: the reached
/// enums, unions and records in DECLARATION order (the reach is a set, filtered over the
/// declarations), the selected kinds in the caller's order.
type internal Decl =
    {
        /// The vocabulary, for the printers' per-declaration reads (names, wire settings).
        Idl: Idl
        /// The selected kinds, in the order the caller named them.
        Kinds: IdlKind list
        /// The enums the selection (and the node envelope) reaches, in declaration order.
        Enums: IdlEnum list
        /// The unions the selection reaches, in declaration order.
        Unions: IdlUnion list
        /// The records the selection reaches, in declaration order.
        Records: IdlRecord list
        /// Kind tag → declared host projection (empty for a backend with no host projections:
        /// a projection is host source, so only the F# module declares one).
        Projections: Map<string, KindProjection>
        /// The type names emitted generic in `'Msg` (vocabulary-wide).
        Msg: Set<string>
        /// The declaration keys (`Node`, `R:<record>`, `S:<kind>`) that take wire equality.
        EqualityCapable: Set<string>
        /// The reached record / union names that hold a node at any depth.
        NodeHolders: Set<string>
        /// Whether any declaration earns an `Obsolete` attribute.
        Obsolete: bool
        /// Whether the vocabulary declares any hosted slot.
        Hosted: bool
        /// Whether any hosted slot declares a format.
        HostedFormat: bool
        /// The node envelope's layout.
        Envelope: EnvelopeLayout
        /// Phase 525 — what the selection reaches on the wire, monomorphised and in first-reach
        /// order (see [[WireReach]]).
        Wire: WireReach
    }

[<RequireQualifiedAccess>]
module internal Decl =

    // -----------------------------------------------------------------------
    // Reach — the enums, unions and records a kind selection transitively references.
    // -----------------------------------------------------------------------

    /// Transitive closure of the enum / union / record types referenced from a set of kinds —
    /// through union case fields, record fields, list elements, map value-types, and union
    /// type-args. Records ↔ unions are mutually recursive (`CellKindErased.ButtonGroup` holds a
    /// `ButtonGroupItem` record; `FormField` holds a `FormFieldKind` union), so the walk visits
    /// both. Returns each set filtered to IDL declaration order.
    let private referenced (idl: Idl) (kinds: IdlKind list) : IdlEnum list * IdlUnion list * IdlRecord list =
        let enums = System.Collections.Generic.HashSet<string>()
        let unions = System.Collections.Generic.HashSet<string>()
        let records = System.Collections.Generic.HashSet<string>()

        let rec visit (t: IdlType) =
            match t with
            | TEnum n -> enums.Add n |> ignore
            | TUnion(n, args) ->
                args |> List.iter visit

                if unions.Add n then
                    match CodegenLookup.tryUnion idl n with
                    | Some u ->
                        for c in u.Cases do
                            for f in c.Fields do
                                visit f.Type
                    | None -> ()
            | TRecord(n, args) ->
                // Phase 732 — a declaration named only as a record's argument is reached.
                args |> List.iter visit

                if records.Add n then
                    match CodegenLookup.tryRecord idl n with
                    | Some r ->
                        for f in r.Fields do
                            visit f.Type
                    | None -> ()
            | TList inner -> visit inner
            | TMap vt -> visit vt
            // fuaran#691 — a declared type NAMED IN A SIGNATURE is reachable. `TFn.FSharp` is free
            // text the walker cannot parse, so this searches it for the names the IDL already
            // declares: self-limiting (it can only mark something the IDL defines), and without it
            // a type reached ONLY through a signature (`Motion`) is never emitted.
            | TFn sg -> visitNamed sg.FSharp
            // A hosted slot's type and codec expressions may reference declared types AND
            // generated codecs (`encRangePair`) — a type reached only that way is still emitted.
            | THosted h ->
                // Phase 252 — a declared wire form is a reachability root like any field type.
                h.Wire |> Option.iter visit
                visitNamed (h.FSharp + " " + h.Encode + " " + h.Decode)
            | _ -> ()

        and visitNamed (text: string) =
            for e in idl.Enums do
                if text.Contains e.Name then
                    enums.Add e.Name |> ignore

            for r in idl.Records do
                if text.Contains r.Name then
                    visit (TRecord(r.Name, []))

            for u in idl.Unions do
                if text.Contains u.Name then
                    visit (TUnion(u.Name, []))

        for k in kinds do
            for f in k.Fields do
                visit f.Type
        // fuaran#690 — the node envelope is a reachability ROOT too: its records are reachable
        // from no kind, so walking only the kinds emits a `Node` whose field types are undeclared.
        for f in idl.NodeFields do
            visit f.Type

        idl.Enums |> List.filter (fun e -> enums.Contains e.Name),
        idl.Unions |> List.filter (fun u -> unions.Contains u.Name),
        idl.Records |> List.filter (fun r -> records.Contains r.Name)

    // -----------------------------------------------------------------------
    // Every field list the vocabulary declares, for the vocabulary-wide predicates.
    // -----------------------------------------------------------------------

    let private allFieldLists (idl: Idl) : IdlField list list =
        [ idl.NodeFields
          yield! idl.Kinds |> List.map _.Fields
          yield! idl.Ops |> List.map _.Fields
          yield! idl.Records |> List.map _.Fields
          yield! idl.Unions |> List.collect (fun u -> u.Cases |> List.map _.Fields) ]

    /// Whether any hosted slot in the vocabulary satisfies `pick`. Phase 732 — every case of the type
    /// is named, with no wildcard: the wildcard is how a slot held in a tuple, a set or a keyed map
    /// was not seen, and its helpers not emitted.
    let private declaresHostedWhere (pick: HostedCodec -> bool) (idl: Idl) : bool =
        let rec has (t: IdlType) =
            match t with
            | THosted h -> pick h
            | TList inner
            | TMap inner
            | TSet inner -> has inner
            | TRecord(_, args)
            | TUnion(_, args)
            | TTuple args -> args |> List.exists has
            | TKeyedMap(k, v) -> has k || has v
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TVar _
            | TNode
            | TKind
            | TOp
            | TClosure
            | TOpaque
            | TFn _
            | TJson
            | TInt64
            | TDecimal
            | TBytes
            | TTimeOfDay
            | TDuration -> false

        allFieldLists idl |> List.exists (List.exists (fun f -> has f.Type))

    // -----------------------------------------------------------------------
    // Phase 689 — which declarations are generic in `'Msg`. `TFn` lets a field carry a typed
    // closure over the host's message type; a declaration holding one (directly or through
    // anything that does) is emitted generic in `'Msg`, so the parameter stays confined to the
    // kinds that genuinely dispatch.
    // -----------------------------------------------------------------------

    /// The type names emitted generic in `'Msg` — union names, record names, `<Tag>Spec` names,
    /// plus `NodeKind` / `Node` when any kind qualifies.
    let msgCarrying (idl: Idl) : Set<string> =
        // Phase 732 — every case of the type is named, with no wildcard (a holder inside a tuple, a
        // set or a keyed map was not seen), and a record reference's arguments are read as a union
        // reference's are.
        let rec mentions (seen: Set<string>) (t: IdlType) =
            match t with
            | TFn s -> s.FSharp.Contains "'Msg"
            | TList inner
            | TMap inner
            | TSet inner -> mentions seen inner
            | TTuple args -> args |> List.exists (mentions seen)
            | TKeyedMap(k, v) -> mentions seen k || mentions seen v
            | TNode -> seen.Contains "Node"
            | TRecord(n, args)
            | TUnion(n, args) -> seen.Contains n || args |> List.exists (mentions seen)
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TVar _
            | TKind
            | TOp
            | TClosure
            | TOpaque
            | TJson
            | THosted _
            | TInt64
            | TDecimal
            | TBytes
            | TTimeOfDay
            | TDuration -> false

        let step (seen: Set<string>) =
            let fieldsMention (fs: IdlField list) =
                fs |> List.exists (fun f -> mentions seen f.Type)

            let unions =
                idl.Unions
                |> List.filter (fun u -> u.Cases |> List.exists (fun c -> fieldsMention c.Fields))
                |> List.map _.Name

            let records =
                idl.Records |> List.filter (fun r -> fieldsMention r.Fields) |> List.map _.Name

            let kinds =
                idl.Kinds
                |> List.filter (fun k -> fieldsMention k.Fields)
                |> List.map (fun k -> k.Tag + "Spec")

            // `NodeKind` wraps every spec, and `Node` wraps `NodeKind` — so one dispatching kind
            // makes the whole tree generic. That is the point.
            let tree =
                if
                    idl.Kinds
                    |> List.exists (fun k -> Set.contains (k.Tag + "Spec") (Set.ofList kinds))
                then
                    [ "NodeKind"; "Node" ]
                else
                    []

            Set.unionMany
                [ seen
                  Set.ofList unions
                  Set.ofList records
                  Set.ofList kinds
                  Set.ofList tree ]

        let rec fix (seen: Set<string>) =
            let next = step seen
            if next = seen then seen else fix next

        fix Set.empty

    // -----------------------------------------------------------------------
    // Phase 252 — WIRE EQUALITY. A declaration holding a host-only FUNCTION field has no
    // structural equality; one whose wire fields all compare takes custom equality over them.
    // -----------------------------------------------------------------------

    /// The declaration keys (`Node`, `R:<record>`, `S:<kind>`) that take custom equality over
    /// their wire fields: each holds a host-only closure field, is not generic in `'Msg`, and
    /// every wire field compares (fixpoint over the candidates). A projected kind's record is
    /// verbatim host source, so it neither qualifies nor counts as capable.
    let private wireEqualityKeys
        (idl: Idl)
        (msg: Set<string>)
        (kinds: IdlKind list)
        (projected: Set<string>)
        : Set<string> =
        let fieldsOf (key: string) : IdlField list option =
            if key = "Node" then
                Some idl.NodeFields
            elif key.StartsWith "R:" then
                idl.Records
                |> List.tryFind (fun r -> r.Name = key.Substring 2)
                |> Option.map _.Fields
            elif key.StartsWith "S:" && not (projected.Contains(key.Substring 2)) then
                kinds |> List.tryFind (fun k -> k.Tag = key.Substring 2) |> Option.map _.Fields
            else
                None

        // A host-only field whose declared host type is a FUNCTION — the one kind of host-only
        // field that removes structural equality. A host-only DATA field compares already.
        let hostOnlyClosure (f: IdlField) =
            f.Opt = HostOnly
            && (match f.Type with
                | TFn s -> s.FSharp.Contains "->"
                | _ -> false)

        // Phase 732 — a record declaring a type parameter is generic as a message-carrying one is:
        // whether a member held at the parameter compares is its argument's to say, so the record
        // keeps the equality F# derives for it and takes no custom one.
        let generic (key: string) =
            if key = "Node" then
                msg.Contains "Node"
            elif key.StartsWith "R:" then
                msg.Contains(key.Substring 2)
                || idl.Records
                   |> List.exists (fun r -> r.Name = key.Substring 2 && not (List.isEmpty r.Params))
            else
                msg.Contains(key.Substring 2 + "Spec")

        let candidates =
            [ "Node"
              yield! idl.Records |> List.map (fun r -> "R:" + r.Name)
              yield! kinds |> List.map (fun k -> "S:" + k.Tag) ]
            |> List.filter (fun key ->
                not (generic key)
                && (fieldsOf key |> Option.exists (List.exists hostOnlyClosure)))
            |> Set.ofList

        let rec capable (granted: Set<string>) (visiting: Set<string>) (t: IdlType) : bool =
            match t with
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TJson
            | TClosure
            | TOpaque
            | TVar _
            | TInt64
            | TDecimal
            | TBytes
            | TTimeOfDay
            | TDuration -> true
            | TList inner
            | TMap inner
            | TSet inner -> capable granted visiting inner
            | TTuple args -> args |> List.forall (capable granted visiting)
            | TKeyedMap(k, v) -> capable granted visiting k && capable granted visiting v
            | TRecord(n, args) ->
                args |> List.forall (capable granted visiting)
                && declCapable granted visiting ("R:" + n)
            | TNode -> declCapable granted visiting "Node"
            | TUnion(n, args) ->
                args |> List.forall (capable granted visiting)
                && (match CodegenLookup.tryUnion idl n with
                    | Some u when not (msg.Contains n) && not (Set.contains ("U:" + n) visiting) ->
                        u.Cases
                        |> List.forall (fun c ->
                            c.Fields
                            |> List.forall (fun f ->
                                not (hostOnlyClosure f)
                                && (f.Opt = HostOnly || capable granted (Set.add ("U:" + n) visiting) f.Type)))
                    | Some u when Set.contains ("U:" + n) visiting -> true
                    | _ -> false)
            | TFn _
            | THosted _
            | TKind
            | TOp -> false

        and declCapable (granted: Set<string>) (visiting: Set<string>) (key: string) : bool =
            if Set.contains key granted || Set.contains key visiting then
                true
            else
                let visiting = Set.add key visiting

                let own =
                    match fieldsOf key with
                    | Some fs ->
                        fs
                        |> List.forall (fun f ->
                            not (hostOnlyClosure f) && (f.Opt = HostOnly || capable granted visiting f.Type))
                    | None -> false

                // The node's equality also reaches every kind it can carry.
                own
                && (key <> "Node"
                    || kinds |> List.forall (fun k -> declCapable granted visiting ("S:" + k.Tag)))

        let wireFieldsCapable (granted: Set<string>) (key: string) =
            let own =
                fieldsOf key
                |> Option.defaultValue []
                |> List.filter (fun f -> f.Opt <> HostOnly)
                |> List.forall (fun f -> capable granted (Set.singleton key) f.Type)

            own
            && (key <> "Node"
                || kinds
                   |> List.forall (fun k -> declCapable granted (Set.singleton key) ("S:" + k.Tag)))

        let rec fixpoint (granted: Set<string>) =
            let next = granted |> Set.filter (wireFieldsCapable granted)
            if next = granted then granted else fixpoint next

        fixpoint candidates

    // -----------------------------------------------------------------------
    // Node holders — the reached declarations that hold a node at any depth.
    // -----------------------------------------------------------------------

    /// Whether a value of type `t` holds a node, given the holder set `hs`.
    let rec holdsIn (hs: Set<string>) (t: IdlType) =
        match t with
        | TNode -> true
        | TList i
        | TMap i -> holdsIn hs i
        | TRecord(n, args)
        | TUnion(n, args) -> hs.Contains n || List.exists (holdsIn hs) args
        | _ -> false

    /// The record / union names among `unions` and `records` that hold a node at any depth
    /// (fixpoint; a node held through a type argument counts, and is refused where it is met).
    let private nodeHolders (unions: IdlUnion list) (records: IdlRecord list) : Set<string> =
        let step (hs: Set<string>) =
            let us =
                unions
                |> List.filter (fun u ->
                    u.Cases
                    |> List.exists (fun c -> c.Fields |> List.exists (fun f -> holdsIn hs f.Type)))
                |> List.map _.Name

            let rs =
                records
                |> List.filter (fun r -> r.Fields |> List.exists (fun f -> holdsIn hs f.Type))
                |> List.map _.Name

            Set.unionMany [ hs; Set.ofList us; Set.ofList rs ]

        let rec fix hs =
            let next = step hs
            if next = hs then hs else fix next

        fix Set.empty

    // -----------------------------------------------------------------------
    // Phase 525 — the wire reach, monomorphised (see [[WireReach]]).
    // -----------------------------------------------------------------------

    /// The fields a declaration puts on the wire — every one but a host-only field.
    let private onWire (fields: IdlField list) : IdlField list =
        fields |> List.filter (fun f -> f.Opt <> HostOnly)

    /// One wire type's identity: two closed types with one key are one entry of the reach. A
    /// hosted slot is its declared wire form, or a verbatim JSON value when it declares none; a
    /// signature and a closure are one sentinel.
    let rec private wireKey (t: IdlType) : string =
        match t with
        | THosted h ->
            match h.Wire with
            | Some w -> wireKey w
            | None -> "json"
        | TJson -> "json"
        | TFn _
        | TClosure -> "closure"
        | TOpaque -> "opaque"
        | TStr -> "str"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TNode -> "node"
        | TKind -> "kind"
        | TOp -> "op"
        | TVar v -> "var_" + v
        | TEnum n -> "enum_" + n
        | TRecord(n, args) -> "record_" + n + "(" + (args |> List.map wireKey |> String.concat ",") + ")"
        | TList inner -> "list_" + wireKey inner
        | TMap vt -> "map_" + wireKey vt
        | TUnion(n, args) -> "union_" + n + "(" + (args |> List.map wireKey |> String.concat ",") + ")"
        | TInt64 -> "int64"
        | TDecimal -> "decimal"
        | TBytes -> "bytes"
        | TTimeOfDay -> "timeOfDay"
        | TDuration -> "duration"
        | TTuple args -> "tuple(" + (args |> List.map wireKey |> String.concat ",") + ")"
        | TSet inner -> "set_" + wireKey inner
        | TKeyedMap(k, v) -> "keyedMap(" + wireKey k + "," + wireKey v + ")"

    /// How many instantiations of generic declarations one path may nest before a walk stops and
    /// names the declaration as one whose recursion grows its type argument: the wire reach's bound
    /// ([[WireReach.Grows]]) and the fold's ([[foldSelfIn]]).
    [<Literal>]
    let instantiationDepth = 64

    let private wireReach (idl: Idl) (kinds: IdlKind list) : WireReach =
        let order = ResizeArray<IdlType>()
        let seen = System.Collections.Generic.HashSet<string>()
        let enums = ResizeArray<string>()
        let mutable envelopeReach = 0
        let mutable grows: string option = None

        // `depth` counts the instantiations of generic declarations entered on the current path.
        let rec visit (depth: int) (t: IdlType) =
            match t with
            | THosted h -> h.Wire |> Option.iter (visit depth)
            | TEnum n ->
                if not (enums.Contains n) then
                    enums.Add n
            | TList inner
            | TMap inner
            | TSet inner ->
                if seen.Add(wireKey t) then
                    visit depth inner
                    order.Add t
            // Phase 639 — a tuple and a keyed map after what they hold; a text-borne scalar once, so
            // the F* model declares its canonical-spelling predicate where a member first reaches it.
            | TTuple args ->
                if seen.Add(wireKey t) then
                    args |> List.iter (visit depth)
                    order.Add t
            | TKeyedMap(k, v) ->
                if seen.Add(wireKey t) then
                    visit depth k
                    visit depth v
                    order.Add t
            | TInt64
            | TDecimal
            | TBytes
            | TTimeOfDay
            | TDuration ->
                if seen.Add(wireKey t) then
                    order.Add t
            | TNode ->
                if seen.Add "node" then
                    // Placed BEFORE its members are walked, so a kind that holds a node does not
                    // re-enter; the kinds come after everything the envelope reaches.
                    order.Add TNode
                    visitFields depth Map.empty idl.NodeFields
                    envelopeReach <- order.Count

                    for k in kinds do
                        visitFields depth Map.empty k.Fields
            | TRecord(n, args) ->
                instance depth t "record" n args (fun () ->
                    CodegenLookup.tryRecord idl n
                    |> Option.bind (fun r ->
                        TypeParams.bindRecord r args |> Option.map (fun subst -> subst, [ r.Fields ])))
            | TUnion(n, args) ->
                instance depth t "union" n args (fun () ->
                    CodegenLookup.tryUnion idl n
                    |> Option.bind (fun u ->
                        TypeParams.bind u args
                        |> Option.map (fun subst -> subst, u.Cases |> List.map _.Fields)))
            | _ -> ()

        // One instance of a declaration: placed when first met, before what its fields reach at its
        // own arguments. A reference no binding can be made for reaches nothing.
        and instance
            (depth: int)
            (t: IdlType)
            (what: string)
            (name: string)
            (args: IdlType list)
            (members: unit -> (Map<string, IdlType> * IdlField list list) option)
            =
            let inner = if List.isEmpty args then depth else depth + 1

            if inner > instantiationDepth then
                if grows.IsNone then
                    grows <- Some(sprintf "%s '%s'" what name)
            elif seen.Add(wireKey t) then
                order.Add t

                match members () with
                | Some(subst, fieldLists) -> fieldLists |> List.iter (visitFields inner subst)
                | None -> ()

        and visitFields (depth: int) (subst: Map<string, IdlType>) (fields: IdlField list) =
            for f in onWire fields do
                visit depth (TypeParams.substitute subst f.Type)

        visit 0 TNode
        let all = List.ofSeq order

        { FromEnvelope = List.truncate envelopeReach all
          FromKinds = List.skip envelopeReach all
          Enums = List.ofSeq enums
          Grows = grows }

    // -----------------------------------------------------------------------
    // Phase 539 — the canonical-form declarations (DECISIONS.md D190), named for a backend that
    // does not apply them and must refuse them by name rather than ignore them.
    // -----------------------------------------------------------------------

    /// Every field list of the vocabulary with the owner it belongs to, as a refusal names it.
    let fieldOwners (idl: Idl) : (string * IdlField list) list =
        [ for k in idl.Kinds do
              yield "kind '" + k.Tag + "'", k.Fields
          for o in idl.Ops do
              yield "op '" + o.Tag + "'", o.Fields
          for u in idl.Unions do
              for c in u.Cases do
                  yield "union case '" + u.Name + "." + c.Tag + "'", c.Fields
          for r in idl.Records do
              yield "record '" + r.Name + "'", r.Fields
          yield "the node envelope", idl.NodeFields ]

    /// Whether any declared type in the vocabulary is of the wider data universe.
    let rec private widerType (t: IdlType) : bool =
        match t with
        | TInt64
        | TDecimal
        | TBytes
        | TTimeOfDay
        | TDuration
        | TTuple _
        | TSet _
        | TKeyedMap _ -> true
        | TList i
        | TMap i -> widerType i
        | TRecord(_, args)
        | TUnion(_, args) -> args |> List.exists widerType
        | THosted { Wire = Some w } -> widerType w
        | _ -> false

    /// Whether the vocabulary declares a type of the wider data universe anywhere.
    let reachesWider (idl: Idl) : bool =
        fieldOwners idl
        |> List.exists (fun (_, fields) -> fields |> List.exists (fun f -> widerType f.Type))

    /// Each alias or presence condition the vocabulary declares that a backend applying the conditions
    /// `applied` does not apply, as a phrase naming it — `[]` for a vocabulary that declares none
    /// (Phase 660: the TypeScript codec applies [[WhenNonEmpty]] and refuses the rest).
    let canonicalFormDeclarationsBeyond (applied: PresenceCondition -> bool) (idl: Idl) : string list =
        [ for owner, fields in fieldOwners idl do
              for f in fields do
                  if not (List.isEmpty f.Aliases) then
                      yield sprintf "the aliases of field '%s' of %s" f.Name owner

                  match f.Presence with
                  | Some c when not (applied c) ->
                      yield sprintf "the presence condition of field '%s' of %s" f.Name owner
                  | _ -> ()
          for e in idl.Enums do
              if e.Aliases |> List.exists (fun (_, a) -> not (List.isEmpty a)) then
                  yield sprintf "the case aliases of enum '%s'" e.Name ]

    /// Each alias or presence condition the vocabulary declares, as a phrase naming it — `[]` for a
    /// vocabulary that declares none.
    let canonicalFormDeclarations (idl: Idl) : string list =
        canonicalFormDeclarationsBeyond (fun _ -> false) idl

    // -----------------------------------------------------------------------
    // Phase 525 — the ONE admissibility of a declared default (see [[Literal]]).
    // -----------------------------------------------------------------------

    /// The declared default `v` at the type `t` as a typed [[Literal]], or the refusal every
    /// backend gives it. Admitted: a well-formed string; an int; a bool; a finite float (an int
    /// authored at a float slot reads as one); an enum's WIRE spelling, resolved to its host case
    /// through [[IdlEnum.CaseOf]] (a host case name the wire does not use is refused — Phase 292);
    /// the empty list; the empty map and a JSON value at a `TJson` slot whose strings are well-formed
    /// and whose numbers are finite (Phase 539); a declared record; a declared union case at the
    /// declared arity that is not the union's declared TRANSPARENT case (the 2026-09-13 ruling: the
    /// case encodes bare, so no host can test for it on the wire). Phase 656 — at the wider data
    /// universe's types (D251): a 64-bit integer, a decimal, bytes, a time of day and a duration in
    /// the ONE canonical spelling its reader admits ([[ScalarText]]); a tuple at its declared arity
    /// (each item admitted at its component); the empty set and the empty keyed map. A spelling that
    /// is not canonical is refused and never repaired, as the encoder refuses it. Everything else — a
    /// non-empty map, set or keyed map, a closure or hosted value, a non-empty list, an unpaired
    /// surrogate, a non-finite float — is refused.
    let literal (idl: Idl) (t: IdlType) (v: IdlValue) : Result<Literal, CodegenError> =
        let refuse () =
            Error(CodegenError.UnsupportedDefault(t, v))

        let rec spellable (j: JVal) =
            match j with
            | JStr s -> SourceLit.isWellFormed s
            | JFloat f -> System.Double.IsFinite f
            | JInt _
            | JBool _ -> true
            | JArr xs -> List.forall spellable xs
            | JObj ms -> ms |> List.forall (fun (k, x) -> SourceLit.isWellFormed k && spellable x)

        match t, v with
        | TStr, VStr s when not (SourceLit.isWellFormed s) -> refuse ()
        | TStr, VStr s -> Ok(Literal.Str s)
        | TInt, VInt i -> Ok(Literal.Int i)
        | TBool, VBool b -> Ok(Literal.Bool b)
        | TFloat, VFloat f when System.Double.IsFinite f -> Ok(Literal.Float f)
        | TFloat, VInt i -> Ok(Literal.Float(float i))
        | TEnum n, VEnum spelling ->
            match CodegenLookup.tryEnum idl n |> Option.bind (fun e -> e.CaseOf spelling) with
            | Some case -> Ok(Literal.Enum(n, case))
            | None -> refuse ()
        | TList _, VList [] -> Ok Literal.EmptyList
        | TMap _, VMap [] -> Ok Literal.EmptyMap
        | TInt64, VStr s when ScalarText.isInt64 s ->
            Ok(Literal.Int64(System.Int64.Parse(s, System.Globalization.CultureInfo.InvariantCulture)))
        | (TDecimal, VStr s) when ScalarText.isDecimal s -> Ok(Literal.Text(t, s))
        | (TTimeOfDay, VStr s) when ScalarText.isTimeOfDay s -> Ok(Literal.Text(t, s))
        | (TDuration, VStr s) when ScalarText.isDuration s -> Ok(Literal.Text(t, s))
        | TBytes, VStr s when ScalarText.isBytes s -> Ok(Literal.Bytes(s, System.Convert.FromBase64String s))
        | TTuple cs, VList xs when List.length cs = List.length xs -> Ok(Literal.Tuple(cs, xs))
        | TSet _, VList [] -> Ok Literal.EmptySet
        | TKeyedMap _, VList [] -> Ok Literal.EmptyKeyedMap
        | TJson, VJson j when spellable j -> Ok(Literal.Json j)
        | TRecord(n, args), VRecord authored ->
            match CodegenLookup.tryRecord idl n with
            | Some r when List.length r.Params = List.length args -> Ok(Literal.Record(r, args, authored))
            | _ -> refuse ()
        | TUnion(n, args), VUnion(tag, authored) ->
            match CodegenLookup.tryUnion idl n with
            | Some u when
                List.length u.Params = List.length args
                && TransparentUnion.tag idl.Harden u <> (u.CaseByWireTag tag |> Option.map _.Tag)
                ->
                match u.CaseByWireTag tag with
                // Phase 550 — a case-level host codec's case holds a host payload a literal cannot spell.
                | Some c when c.Hosted.IsSome -> refuse ()
                | Some c -> Ok(Literal.Union(u, args, c, authored))
                | None -> refuse ()
            | _ -> refuse ()
        | _ -> refuse ()

    /// Phase 524 (511.t4) — the ONE presence rule for a member's value: the member's declared
    /// optionality and the value offered for it (authored, or declared as a default; `VAbsent`
    /// reads as none) at the [[ValueSite]] asking. Every backend prints the answer; none decides it.
    let memberValue (site: ValueSite) (opt: Optionality) (offered: IdlValue option) : MemberValue =
        let value =
            match offered with
            | Some VAbsent
            | None -> None
            | Some v -> Some v

        match opt, value with
        | Optional, Some v -> MemberValue.Optional v
        | Optional, None -> MemberValue.Absent
        | OmitDefault _, Some v when site <> ValueSite.Constructor -> MemberValue.Carries v
        | OmitDefault d, _ -> MemberValue.IdentityDefault d
        | Required, Some v -> MemberValue.Carries v
        | Required, None when site = ValueSite.Constructor -> MemberValue.Parameter
        | Required, None -> MemberValue.Refused VAbsent
        | HostOnly, v when site = ValueSite.Literal -> MemberValue.Refused(defaultArg v VAbsent)
        | HostOnly, _ -> MemberValue.Placeholder

    // -----------------------------------------------------------------------
    // The resolution.
    // -----------------------------------------------------------------------

    let private build
        (idl: Idl)
        (projections: Map<string, KindProjection>)
        (kinds: IdlKind list)
        (enums: IdlEnum list, unions: IdlUnion list, records: IdlRecord list)
        : Decl =
        let msg = msgCarrying idl

        { Idl = idl
          Kinds = kinds
          Enums = enums
          Unions = unions
          Records = records
          Projections = projections
          Msg = msg
          EqualityCapable = wireEqualityKeys idl msg kinds (projections |> Map.toList |> List.map fst |> Set.ofList)
          NodeHolders = nodeHolders unions records
          Obsolete = Annotations.emitsObsolete idl
          Hosted = declaresHostedWhere (fun _ -> true) idl
          HostedFormat = declaresHostedWhere _.Format.IsSome idl
          Envelope =
            { Kind =
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind -> KindPlacement.NestedUnder "kind"
                | NodeEnvelopeShape.FlatKind -> KindPlacement.Merged
              Members = idl.NodeFields }
          Wire = wireReach idl kinds }

    /// Resolve `idl` for the kinds named by `kindTags` (a tag the vocabulary does not declare
    /// selects nothing), with the host projections `projections` (empty where the target has
    /// none): the selected kinds and what they REACH. The module targets read this.
    let resolve (idl: Idl) (projections: Map<string, KindProjection>) (kindTags: string list) : Decl =
        let kinds = kindTags |> List.choose (fun t -> CodegenLookup.tryKind idl t)
        build idl projections kinds (referenced idl kinds)

    /// The resolution of EVERY declaration, reached or not, with no host projections — the
    /// selection of the whole-vocabulary targets (the JSON schema, `fsharpTypes`), which state
    /// each declaration whether or not a kind reaches it.
    let declared (idl: Idl) : Decl =
        build idl Map.empty idl.Kinds (idl.Enums, idl.Unions, idl.Records)

    // -----------------------------------------------------------------------
    // Reads over a resolution.
    // -----------------------------------------------------------------------

    /// The reached union declared as `name`.
    let tryUnion (d: Decl) (name: string) : IdlUnion option =
        d.Unions |> List.tryFind (fun u -> u.Name = name)

    /// The reached record declared as `name`.
    let tryRecord (d: Decl) (name: string) : IdlRecord option =
        d.Records |> List.tryFind (fun r -> r.Name = name)

    /// The tag of the union's declared transparent case, if it declares one.
    let transparentTag (d: Decl) (u: IdlUnion) : string option = TransparentUnion.tag d.Idl.Harden u

    /// How the case `c` of union `u` is laid out on the wire — see [[CaseLayout]].
    let caseLayout (d: Decl) (u: IdlUnion) (c: IdlUnionCase) : CaseLayout =
        match transparentTag d u with
        | Some ttag when ttag = c.Tag ->
            match c.Fields with
            | [ f ] -> CaseLayout.Bare(Ok f)
            | _ -> CaseLayout.Bare(Error(transparentArity u.Name c.Tag))
        | _ -> CaseLayout.Tagged

    /// The union's declared transparent case, if it declares one.
    let transparentCase (d: Decl) (u: IdlUnion) : IdlUnionCase option =
        transparentTag d u
        |> Option.bind (fun ttag -> u.Cases |> List.tryFind (fun c -> c.Tag = ttag))

    /// Whether `t` reaches the union `target` at any depth, through any reached declaration.
    let reaches (d: Decl) (target: string) (t: IdlType) : bool =
        let rec go (seen: Set<string>) (t: IdlType) =
            match t with
            | TUnion(n, args) ->
                n = target
                || List.exists (go seen) args
                || (not (seen.Contains n)
                    && (tryUnion d n
                        |> Option.exists (fun u ->
                            u.Cases
                            |> List.exists (fun c -> c.Fields |> List.exists (fun f -> go (seen.Add n) f.Type)))))
            | TRecord(n, args) ->
                List.exists (go seen) args
                || (not (seen.Contains n)
                    && (tryRecord d n
                        |> Option.exists (fun r -> r.Fields |> List.exists (fun f -> go (seen.Add n) f.Type))))
            | TList i
            | TMap i -> go seen i
            | _ -> false

        go Set.empty t

    let private refuse (construct: string) (principle: string) (alternative: string) : Result<'a, CodegenError> =
        Error(CodegenError.UnsupportedConstruct(construct, principle, alternative))

    /// The instance name of a type — for a non-generic declaration its own name, and for an
    /// instantiated generic union or record (Phase 732) the name plus its mangled arguments
    /// (`Binding_float`, `Binding_list_SelectOption`). Total over `IdlType`, so a printer can name an instance for
    /// any slot it meets: the instantiation walk below names by this same function, so the set
    /// of instances EMITTED and the set REFERENCED are computed one way.
    let rec instName (t: IdlType) : string =
        match t with
        | TStr -> "str"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TJson -> "json"
        | THosted _ -> "hosted"
        | TOpaque -> "opaque"
        | TClosure
        | TFn _ -> "closure"
        | TNode -> "Node"
        | TKind -> "NodeKind"
        | TOp -> "TreeOp"
        | TEnum n
        | TRecord(n, [])
        | TUnion(n, []) -> n
        | TVar v -> v
        | TList inner -> "list_" + instName inner
        | TMap vt -> "map_" + instName vt
        | TRecord(n, args)
        | TUnion(n, args) -> n + "_" + (args |> List.map instName |> String.concat "_")
        | TInt64 -> "int64"
        | TDecimal -> "decimal"
        | TBytes -> "bytes"
        | TTimeOfDay -> "timeOfDay"
        | TDuration -> "duration"
        | TTuple args -> "tuple_" + (args |> List.map instName |> String.concat "_")
        | TSet inner -> "set_" + instName inner
        | TKeyedMap(k, v) -> "keyedMap_" + instName k + "_" + instName v

    /// Phase 732 — a record's fields at the instantiation a reference applies: a member naming one
    /// of the record's parameters is here at its argument. The declared fields where the reference
    /// applies no argument, or one no binding can be made for.
    let recordFieldsAt (r: IdlRecord) (args: IdlType list) : IdlField list =
        match TypeParams.bindRecord r args with
        | Some subst when not subst.IsEmpty ->
            r.Fields
            |> List.map (fun f ->
                { f with
                    Type = TypeParams.substitute subst f.Type })
        | _ -> r.Fields

    /// Whether a value of type `t` holds a value of the union `u`'s own instantiation — the
    /// positions a fold over `u` descends — refusing a recursion the fold cannot follow (at other
    /// type arguments, or through another union). `seen` is the records already entered, each by
    /// its instance name ([[instName]]): a record is read at the instantiation the reference applies
    /// (Phase 732), so a union held at a record's argument is found where the member naming the
    /// parameter holds it. Host-neutral: the F# and TypeScript folds read the same analysis.
    let rec foldSelfIn (d: Decl) (u: IdlUnion) (seen: Set<string>) (t: IdlType) : Result<bool, CodegenError> =
        match t with
        | TUnion(n, args) when n = u.Name ->
            if args = (u.Params |> List.map TVar) then
                Ok true
            else
                refuse
                    (sprintf "a fold over '%s', which recurses at other type arguments" u.Name)
                    "the fold visits nested values of the union's own instantiation"
                    "recurse at the declared type parameters, or do not request the fold"
        | TUnion(n, _) when reaches d u.Name t ->
            refuse
                (sprintf "a fold over '%s', which recurses through the union '%s'" u.Name n)
                "the fold descends lists, options, maps and records, and a recursion through another union would leave the nested values it holds unvisited"
                "recurse through a list, an option, a map or a record, or do not request the fold"
        | TList i
        | TMap i -> foldSelfIn d u seen i
        | TRecord(n, _) when seen.Count >= instantiationDepth ->
            refuse
                (sprintf "a fold over '%s' through the record '%s', whose recursion grows its type argument" u.Name n)
                "the fold reads a record at each instantiation on its path, and this record names a new one at every step"
                "recurse at a fixed argument, or do not request the fold"
        | TRecord(n, args) when not (seen.Contains(instName t)) ->
            tryRecord d n
            |> Option.map (fun r ->
                recordFieldsAt r args
                |> List.map (fun f -> foldSelfIn d u (seen.Add(instName t)) f.Type)
                |> sequenceR
                |> Result.map (List.exists id))
            |> Option.defaultValue (Ok false)
        | _ -> Ok false

    /// Every projected record's declared field types name declarations the module emits — a
    /// type the module does not declare is one no derived member could walk, and would emit
    /// source that does not compile. Checked whatever is requested; it changes no byte of an
    /// admitted module, and is vacuous where there are no projections.
    let projectionsDeclared (d: Decl) : Result<unit, CodegenError> =
        let declared =
            Set.unionMany
                [ d.Unions |> List.map _.Name |> Set.ofList
                  d.Records |> List.map _.Name |> Set.ofList
                  d.Idl.Enums |> List.map _.Name |> Set.ofList ]

        let rec names (t: IdlType) : string list =
            match t with
            | TEnum n -> [ n ]
            | TRecord(n, args)
            | TUnion(n, args) -> n :: List.collect names args
            | TList i
            | TMap i -> names i
            | _ -> []

        let undeclaredIn =
            d.Projections
            |> Map.toList
            |> List.collect (fun (tag, p) ->
                p.RecordFields
                |> Option.defaultValue []
                |> List.collect (fun f ->
                    names f.Type
                    |> List.filter (fun n -> not (declared.Contains n))
                    |> List.map (fun n -> tag, f.Name, n)))

        match undeclaredIn with
        | [] -> Ok()
        | (tag, field, n) :: _ ->
            refuse
                (sprintf "the projected record field '%s.%s' at the undeclared type '%s'" tag field n)
                "a projected record's declared fields name declarations the module emits, so every derivation over them can walk what they hold"
                "declare the field at a type the selected kinds reach, or correct the name"

    // -----------------------------------------------------------------------
    // fuaran#1068 — the instantiations of every GENERIC union, monomorphised.
    // -----------------------------------------------------------------------

    /// The construct a backend names for a union or record reference no binding can be made for:
    /// one sentence, `what` the family (`union` / `record`).
    let private misappliedConstruct (what: string) (name: string) (given: int) (declared: int) : string =
        sprintf "a reference applying %s '%s' to %d type argument(s) where it declares %d" what name given declared

    /// The refusal of a backend handed such a reference ([[misappliedConstruct]]).
    let private misappliedRefusal (construct: string) : Result<'a, CodegenError> =
        refuse
            construct
            "GP5: the refusal names the construct and thereby the set that IS supported"
            "apply the declaration to as many arguments as it declares parameters — `Declare.errors` names every fault of the vocabulary"

    /// Every instantiation of a generic union and of a generic record (Phase 732) the vocabulary
    /// names on the wire, each keyed and ordered by instance name. ONE worklist over the wire fields
    /// of every declaration (an instantiation's own fields substituted before being walked), because
    /// a record's instantiation names a union's and a union's a record's; a vocabulary whose
    /// recursion GROWS its type argument has no finite set, and the bound reports it as a typed
    /// refusal rather than hanging (Phase 195).
    let private instantiated (idl: Idl) : Result<Instantiation list * RecordInstantiation list, CodegenError> =
        let generics =
            idl.Unions
            |> List.filter (fun u -> not (List.isEmpty u.Params))
            |> List.map (fun u -> u.Name, u)
            |> Map.ofList

        let wireFields (fields: IdlField list) =
            fields |> List.filter (fun f -> f.Opt <> HostOnly)

        let mutable found: Map<string, Instantiation> = Map.empty
        let mutable foundRecords: Map<string, RecordInstantiation> = Map.empty
        let mutable queue: IdlType list = []

        let rec discover (t: IdlType) =
            match t with
            | TList inner -> discover inner
            | TMap vt -> discover vt
            | TSet inner -> discover inner
            | TTuple args -> args |> List.iter discover
            | TKeyedMap(k, v) ->
                discover k
                discover v
            | TUnion(n, args) when not (List.isEmpty args) ->
                args |> List.iter discover
                let key = instName t

                if not (Map.containsKey key found) then
                    match Map.tryFind n generics with
                    | None -> ()
                    | Some u when List.length u.Params <> List.length args -> ()
                    | Some u ->
                        let subst = TypeParams.bind u args |> Option.defaultValue Map.empty
                        found <- Map.add key { Name = key; Union = u; Subst = subst } found
                        queue <- t :: queue
            | TRecord(n, args) when not (List.isEmpty args) ->
                args |> List.iter discover
                let key = instName t

                if not (Map.containsKey key foundRecords) then
                    match
                        CodegenLookup.tryRecord idl n
                        |> Option.map (fun r -> r, TypeParams.bindRecord r args)
                    with
                    | Some(r, Some subst) ->
                        foundRecords <-
                            Map.add
                                key
                                { Name = key
                                  Record = r
                                  Subst = subst }
                                foundRecords

                        queue <- t :: queue
                    // A reference no binding can be made for has no instantiation to name; every
                    // entry refuses the vocabulary before it asks for this walk ([[referencesApplied]]).
                    | Some(_, None)
                    | None -> ()
            | _ -> ()

        let discoverFields (fields: IdlField list) =
            for f in wireFields fields do
                discover f.Type

        for u in idl.Unions do
            if List.isEmpty u.Params then
                for c in u.Cases do
                    discoverFields c.Fields

        for r in idl.Records do
            if List.isEmpty r.Params then
                discoverFields r.Fields

        for k in idl.Kinds do
            discoverFields k.Fields

        for o in idl.Ops do
            discoverFields o.Fields

        discoverFields idl.NodeFields

        // Expand: an instantiation's own fields can name further ones.
        let mutable guard = 0

        while not (List.isEmpty queue) && guard <= 1000 do
            guard <- guard + 1

            if guard <= 1000 then
                let head = List.head queue
                queue <- List.tail queue

                match head, Map.tryFind (instName head) found, Map.tryFind (instName head) foundRecords with
                | TRecord _, _, Some i ->
                    for f in wireFields i.Record.Fields do
                        discover (TypeParams.substitute i.Subst f.Type)
                | TUnion _, Some i, _ ->
                    for c in i.Union.Cases do
                        for f in wireFields c.Fields do
                            discover (TypeParams.substitute i.Subst f.Type)
                | _ -> ()

        if guard > 1000 then
            Error(
                CodegenError.UnsupportedConstruct(
                    "a generic union or record whose instantiation walk did not close after 1000 expansions",
                    "GP4: a typed value, not an exception",
                    "the vocabulary's recursion grows its type argument, so it has no finite JSON-Schema `$defs` — recurse at a FIXED argument instead"
                )
            )
        else
            Ok(found |> Map.toList |> List.map snd, foundRecords |> Map.toList |> List.map snd)

    /// Every instantiation of a generic union the vocabulary names on the wire ([[instantiated]]).
    let instantiations (idl: Idl) : Result<Instantiation list, CodegenError> = instantiated idl |> Result.map fst

    /// Every instantiation of a generic record the vocabulary names on the wire ([[instantiated]]),
    /// an inline record's included: it has no object of its own, and its members are still walked.
    let recordInstantiations (idl: Idl) : Result<RecordInstantiation list, CodegenError> =
        instantiated idl |> Result.map snd

    // -----------------------------------------------------------------------
    // Phase 474 (DECISIONS.md D220) — the INLINE declarations. Each reads `Fuaran.Core.Idl`'s
    // internal lowering, so each is `NoInlining` (D129).
    // -----------------------------------------------------------------------

    /// The inline declaration `t` names, if it names one — see [[InlineSlot]].
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let inlineSlot (idl: Idl) (t: IdlType) : InlineSlot option =
        match WireForm.inlineRecord idl t, WireForm.keyedUnion idl t with
        | Some r, _ ->
            Some(
                InlineSlot.Record
                    { Decl = r.Decl
                      Args = r.Args
                      Fields = WireForm.instantiatedFields r }
            )
        | _, Some(u, keyed) -> Some(InlineSlot.Keyed(u, keyed))
        | None, None -> None

    /// Whether the union is presence-keyed.
    let isKeyed (u: IdlUnion) : bool =
        u.Discrimination <> UnionDiscrimination.Tagged

    /// Phase 722 — what EVERY backend asks of a vocabulary before it lays an inline declaration out:
    /// the declaration check's own verdict on the layout ([[Declare.layoutErrors]]), its first fault.
    /// A backend is handed an `Idl` value, not a declaration that was checked, and an inline
    /// declaration is laid out as the check leaves it: where it stands, one key per case, what a
    /// case may synthesise, no key on its object twice. `None` where the selection reaches no inline
    /// declaration on the wire, so a vocabulary declaring none is asked nothing and emits as it did.
    let layoutFault (d: Decl) : VocabularyFault option =
        let reachesInline =
            d.Wire.FromEnvelope @ d.Wire.FromKinds
            |> List.exists (fun t -> (inlineSlot d.Idl t).IsSome)

        if reachesInline then
            Declare.layoutErrors d.Idl |> List.tryHead
        else
            None

    /// The construct a backend names when [[layoutFault]] answers a fault.
    let layoutFaultConstruct (fault: VocabularyFault) : string =
        "an inline declaration the declaration check refuses (" + fault.Detail + ")"

    /// [[layoutFault]] as the refusal of a backend that answers `UnsupportedConstruct`, or the
    /// resolution itself.
    let laidOut (d: Decl) : Result<Decl, CodegenError> =
        match layoutFault d with
        | None -> Ok d
        | Some fault ->
            Error(
                CodegenError.UnsupportedConstruct(
                    layoutFaultConstruct fault,
                    "GP5: a backend lays an inline declaration out as the declaration check leaves it, or refuses it by name",
                    "correct the declaration — `Declare.errors` names every fault of the vocabulary"
                )
            )

    /// The first answer `pick` gives a union or record reference anywhere in the WHOLE vocabulary:
    /// every declared member's type and every contract method's, at any depth, a reference's own
    /// arguments after the reference. Every case of the type is named.
    let private firstReference (pick: IdlType -> string option) (idl: Idl) : string option =
        let rec walk (t: IdlType) : string option =
            match t with
            | TUnion(_, args)
            | TRecord(_, args) -> pick t |> Option.orElseWith (fun () -> args |> List.tryPick walk)
            | TList i
            | TMap i
            | TSet i -> walk i
            | TTuple args -> args |> List.tryPick walk
            | TKeyedMap(k, v) -> [ k; v ] |> List.tryPick walk
            | THosted h -> h.Wire |> Option.bind walk
            | TStr
            | TInt
            | TBool
            | TFloat
            | TEnum _
            | TVar _
            | TNode
            | TKind
            | TOp
            | TClosure
            | TOpaque
            | TFn _
            | TJson
            | TInt64
            | TDecimal
            | TBytes
            | TTimeOfDay
            | TDuration -> None

        let methodTypes =
            idl.Contracts
            |> List.collect _.Methods
            |> List.collect (fun m -> (m.Arguments |> List.map _.Type) @ Option.toList m.Result)

        (allFieldLists idl |> List.collect (List.map _.Type)) @ methodTypes
        |> List.tryPick walk

    /// Phase 732 — what every backend's entries ask of a vocabulary before they emit: that every
    /// reference applies as many arguments as its union or record declares parameters. A backend
    /// is handed an `Idl` value, not a declaration that was checked, and a reference no binding can
    /// be made for has no generic type, function, definition or model instance to name; it is
    /// refused by the declaration's name, a union's in the sentence a record's is.
    let referencesAppliedIdl (idl: Idl) : Result<unit, CodegenError> =
        let misapplied (t: IdlType) =
            let counted (what: string) (name: string) (args: IdlType list) (declared: string list option) =
                match declared with
                | Some pars when List.length pars <> List.length args ->
                    Some(misappliedConstruct what name (List.length args) (List.length pars))
                | _ -> None

            match t with
            | TUnion(n, args) ->
                idl.Unions
                |> List.tryFind (fun u -> u.Name = n)
                |> Option.map _.Params
                |> counted "union" n args
            | TRecord(n, args) -> CodegenLookup.tryRecord idl n |> Option.map _.Params |> counted "record" n args
            | _ -> None

        match firstReference misapplied idl with
        | Some construct -> misappliedRefusal construct
        | None -> Ok()

    /// [[referencesAppliedIdl]] over a resolution, answering the resolution itself.
    let referencesApplied (d: Decl) : Result<Decl, CodegenError> =
        referencesAppliedIdl d.Idl |> Result.map (fun () -> d)

    /// A presence-keyed union's roster — see [[WireForm.keyedRoster]].
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let keyedRoster (u: IdlUnion) : string list = WireForm.keyedRoster u

    /// The interpreter's encoding of `v` at `t` (D129: the one call across the assembly boundary).
    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
    let private encodedAt (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> = Encode.valueJson idl t v

    /// A keyed case's synthesised members as the wire values the encoder writes, each the
    /// interpreter's encoding of its declared value at the member's type.
    let synthesisedJson (idl: Idl) (u: IdlUnion) (k: KeyedCase) : Result<(string * JVal) list, CodegenError> =
        k.Synthesised
        |> List.map (fun (name, v) ->
            match
                u.Cases
                |> List.tryPick (fun c -> c.Fields |> List.tryFind (fun f -> f.Name = name))
            with
            | Some f ->
                match encodedAt idl f.Type v with
                | Ok j -> Ok(name, j)
                | Error m ->
                    Error(
                        CodegenError.UnsupportedConstruct(
                            sprintf "the synthesised member '%s' of union '%s' case '%s'" name u.Name k.Case,
                            sprintf "a synthesised member is written at a value of its type (%s)" m,
                            "declare a value the member's type admits"
                        )
                    )
            | None ->
                Error(
                    CodegenError.UnsupportedConstruct(
                        sprintf "the synthesised member '%s' of union '%s' case '%s'" name u.Name k.Case,
                        "a synthesised member is another case's member",
                        "name a member another case declares"
                    )
                ))
        |> sequenceR
