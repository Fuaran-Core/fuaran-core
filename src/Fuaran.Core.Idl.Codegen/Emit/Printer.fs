namespace Fuaran.Core.Idl.Emit

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core

// ---------------------------------------------------------------------------
// Phase 524 (B §8 S10) — ONE TRAVERSAL, A PRINTER PER BACKEND. The F# and TypeScript codecs each
// carried their own dispatch over `IdlType` for the same question — which encoder a slot takes,
// and what it reads applied to a value — agreeing on the partition of the constructors and the
// refusals, and differing only in the text each arm prints. The partition, the recursion and the
// refusals are stated once here ([[SlotTraversal]]); what each backend prints is a record of
// strings and string builders ([[EncPrinter]]), one value per backend. A further backend costs a
// printer, not a dispatch.
// ---------------------------------------------------------------------------

/// How a backend encodes a HOSTED slot: verbatim through a named host expression, through the
/// slot's declared wire type, or not at all.
[<RequireQualifiedAccess>]
type internal HostedEnc =
    /// The backend's own expression for the slot, verbatim.
    | Verbatim of string
    /// Written THROUGH the declared wire type (Phase 252), so the bytes are that type's.
    | Through of IdlType
    /// The backend cannot write it; the refusal names why.
    | Refused of CodegenError

/// What one backend prints for a slot's ENCODER: the point-free function a container or a generic
/// argument is handed, and the expression it reads applied to a value.
type internal EncPrinter =
    {
        /// The backend's name in an op-vocabulary refusal.
        Backend: string
        Str: string
        Int: string
        Bool: string
        /// Not every backend's float constructor: a non-finite double rides as a sentinel string.
        Float: string
        /// An enum's encoder, by the enum's name.
        Enum: string -> string
        /// A declared name's encoder — a type variable, a non-generic union, a record.
        Named: string -> string
        Node: string
        /// A generic union's encoder, by name, over its argument encoders.
        Union: string -> string list -> string
        /// A list's encoder over its element encoder.
        List: string -> string
        /// A string-keyed map's encoder over its value encoder.
        Map: string -> string
        /// A closure slot's fixed-sentinel encoder (a `TFn` slot's too).
        Closure: string
        /// An opaque slot's fixed-sentinel encoder.
        Opaque: string
        /// A verbatim-JSON slot's encoder.
        Json: string
        Hosted: HostedCodec -> HostedEnc
        /// An encoder applied to a value.
        Apply: string -> string -> string
        /// A list's element encoder applied over a list value.
        ListApplied: string -> string -> string
        /// A node value encoded.
        NodeApplied: string -> string
        /// A generic union's encoder, by name over its argument encoders, applied to a value.
        UnionApplied: string -> string list -> string -> string
        /// A closure slot's sentinel for a value.
        ClosureApplied: string -> string
        /// An opaque slot's sentinel for a value.
        OpaqueApplied: string -> string
    }

/// The one traversal over a slot's type, printed by a backend's [[EncPrinter]].
module internal SlotTraversal =

    /// The point-free encoder for a slot of type `t`.
    let rec encFn (p: EncPrinter) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok p.Str
        | TInt -> Ok p.Int
        | TBool -> Ok p.Bool
        | TFloat -> Ok p.Float
        | TEnum n -> Ok(p.Enum n)
        | TVar v -> Ok(p.Named v)
        | TUnion(n, []) -> Ok(p.Named n)
        | TUnion(n, args) -> args |> List.map (encFn p) |> sequenceR |> Result.map (p.Union n)
        | TNode -> Ok p.Node
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot p.Backend t)
        | TList inner -> encFn p inner |> Result.map p.List
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> Ok p.Closure
        | TOpaque -> Ok p.Opaque
        // Phase 676 — verbatim passthrough: the canonical renderer already sorts keys, escapes
        // and lays floats out, so identity inherits all three.
        | TJson -> Ok p.Json
        | THosted h ->
            match p.Hosted h with
            | HostedEnc.Verbatim s -> Ok s
            | HostedEnc.Through wire -> encFn p wire
            | HostedEnc.Refused e -> Error e
        | TRecord n -> Ok(p.Named n)
        | TMap vt -> encFn p vt |> Result.map p.Map

    /// The encoder applied to the value `var` of type `t`.
    let encApplied (p: EncPrinter) (var: string) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TList inner -> encFn p inner |> Result.map (fun e -> p.ListApplied e var)
        | TNode -> Ok(p.NodeApplied var)
        | TUnion(n, (_ :: _ as args)) ->
            args
            |> List.map (encFn p)
            |> sequenceR
            |> Result.map (fun a -> p.UnionApplied n a var)
        | TClosure
        | TFn _ -> Ok(p.ClosureApplied var)
        | TOpaque -> Ok(p.OpaqueApplied var)
        | _ -> encFn p t |> Result.map (fun e -> p.Apply e var)

/// The two printers the generator ships.
module internal Printers =

    /// The F# encoder printer: `JVal` constructors and `enc<Name>` functions.
    let fsEnc: EncPrinter =
        let closure = "(fun _ -> JStr \"<closure>\")"
        let opaque = "(fun _ -> JStr \"<opaque>\")"

        { Backend = "the F# encoder emitter"
          Str = "JStr"
          Int = "JInt"
          Bool = "JBool"
          // NOT the bare `JFloat` constructor: a non-finite double has no JSON number spelling
          // and rides as a quoted sentinel string (`encodeHelpers`). `TInt` keeps its
          // constructor — WIRE_FORMAT §7 truncates at a float slot.
          Float = "encFloat"
          Enum = fun n -> "enc" + n
          Named = fun n -> "enc" + n
          Node = "encNode"
          Union = fun n args -> "(enc" + n + " " + String.concat " " args + ")"
          List = sprintf "(fun __xs -> JArr(List.map %s __xs))"
          Map = sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))"
          Closure = closure
          Opaque = opaque
          Json = "id"
          // The named host encode expression, verbatim ('host -> JVal). Canonicality is
          // inherited: the host codec builds a JVal that renders through the same renderer.
          Hosted = fun h -> HostedEnc.Verbatim h.Encode
          Apply = sprintf "%s %s"
          ListApplied = sprintf "JArr(List.map %s %s)"
          NodeApplied = sprintf "encNode %s"
          UnionApplied = fun n args var -> "(enc" + n + " " + String.concat " " args + ") " + var
          ClosureApplied = fun _ -> "JStr \"<closure>\""
          OpaqueApplied = fun _ -> "JStr \"<opaque>\"" }

    /// The TypeScript encoder printer: `enc<Name>` functions writing canonical JSON text.
    let tsEnc: EncPrinter =
        let backend = "the TypeScript encoder backend"
        let closure = "(() => '\"<closure>\"')"
        let opaque = "(() => '\"<opaque>\"')"

        { Backend = backend
          Str = "encStr"
          Int = "encInt"
          Bool = "encBool"
          Float = "encFloat"
          // An enum value IS its wire string.
          Enum = fun _ -> "encStr"
          Named = fun n -> "enc" + n
          Node = "encodeNode"
          Union = fun n args -> "((x) => enc" + n + "(" + String.concat ", " args + ", x))"
          List = fun e -> "((xs) => '[' + xs.map(" + e + ").join(',') + ']')"
          Map =
            fun e ->
                "((m) => '{' + Object.keys(m).sort().map((k) => encStr(k) + ':' + ("
                + e
                + ")(m[k])).join(',') + '}')"
          Closure = closure
          Opaque = opaque
          // `encJson` renders the parsed value canonically (see the prelude).
          Json = "encJson"
          // Phase 252 — a hosted slot that declares its wire form is written THROUGH it, so the
          // bytes are that type's canonical ones; one that does not is verbatim JSON to this
          // backend, as to the interpreter.
          Hosted =
            fun h ->
                match h.Wire with
                | Some wire ->
                    match hostedWireRefusal backend h with
                    | Some e -> HostedEnc.Refused e
                    | None -> HostedEnc.Through wire
                | None -> HostedEnc.Verbatim "encJson"
          Apply = fun e var -> e + "(" + var + ")"
          ListApplied = fun e var -> "'[' + " + var + ".map(" + e + ").join(',') + ']'"
          NodeApplied = fun var -> "encodeNode(" + var + ")"
          UnionApplied = fun n args var -> "enc" + n + "(" + String.concat ", " args + ", " + var + ")"
          ClosureApplied = fun var -> closure + "(" + var + ")"
          OpaqueApplied = fun var -> opaque + "(" + var + ")" }
