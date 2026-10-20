namespace Fuaran.Core.Idl.Codegen.Types

// Phase 524 — the structural derivations a module emits on request, the one request set every
// emitter reads (the collecting decoders included: `SpecDecoders` is a member of the set, not a
// `bool` carried beside it).

/// Phase 374 — a STRUCTURAL DERIVATION the F# module emits on request: a member that follows
/// mechanically from what the generator already knows about every kind (which fields hold
/// nodes, which hold a given declared type, which declarations carry the message parameter,
/// what each field defaults to, what each kind is tagged with) and that a consuming domain
/// would otherwise write by hand. Requesting none is `fsharpModuleWith`, byte for byte. A
/// request the vocabulary cannot honour is refused as a typed `UnsupportedConstruct`.
[<RequireQualifiedAccess>]
type Derivation =
    /// Public `wireTag`, `allWireTags`, `children` and `withChildren` (kids first:
    /// `withChildren (children n) n = n`), with `nodeWitness` built on them. A child is a
    /// node or node list a kind always carries; an optional node is a keyed position.
    | StructuralAccess
    /// `keyedChildren`, `withKeyedChildren` (arity-preserving) and `keyedWitness`, a
    /// `KeyedWitness` over every node position that is not a structural child: an optional
    /// node, a node inside a record, a union case, a list or a map, and the node envelope.
    /// Implies `StructuralAccess`.
    | KeyedPositions
    /// `slotsOf<T> : Node -> (string * T) list` — every kind and envelope field declared at
    /// the named enum, record or union `T` (directly, optional or in a list), by wire field
    /// name; `(string * obj) list` when `T` is a generic union.
    | SlotsOf of typeName: string
    /// `mapMsg : ('Msg -> 'Msg2) -> Node<'Msg> -> Node<'Msg2>`, rebuilding every declaration
    /// generic in the message parameter.
    | MapMsg
    /// `<Union>.fold : ('S -> U -> 'S) -> 'S -> U -> 'S` over a self-recursive union: the
    /// value and every nested value of its own type, in preorder.
    | Fold of unionName: string
    /// `<Union>.<field>` for each named field: `U -> T` where every case carries it at one
    /// type, `U -> T option` where only some do.
    | Projections of unionName: string * fieldNames: string list
    /// `default<Tag>Spec` / `default<Record>` for every kind and record whose every field has
    /// a value without the caller — the values the `mk<Kind>` constructors fill.
    | DefaultRecords
    /// `kindCategories`, `kindFieldNames`, `envelopeFieldNames` and `opFieldNames` as `Set`
    /// values.
    | VocabularyConstants
    /// Phase 377 — a COLLECTING decoder beside every short-circuiting one, answering every
    /// defect as a `DecodeError list` in a stated order (field declaration order, each
    /// member's nested defects at its position; list items by index; map entries in document
    /// order — the order is written above the collecting prelude), and the public entries
    /// over them: per kind `decode<Tag>Spec` (the first defect) and `decode<Tag>SpecAll`
    /// (every defect), per node `decodeNodeJson`, `decodeNodeJsonAll` and `decodeNodeAll`.
    /// A collecting decoder's first defect is its short-circuiting twin's defect.
    /// Phase 541 — every nested position is read through a `CollectingDecoders` record
    /// (`collectingDecoders` is the generated one), and `decode<Tag>SpecAllWith`,
    /// `decodeNodeJsonAllWith` and `decodeNodeAllWith` take a host's copy, so a host decoder
    /// at a type applies at every depth (DECISIONS.md D175).
    | SpecDecoders
