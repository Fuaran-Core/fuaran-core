module Fuaran.Core.Tests.VectorOracle

// ============================================================================
//  Phase 478 — the extracted models the exporters evaluate, and the stamp each vector carries.
//
//  A vector's answer is computed by calling the reference implementation, so a host certified on the
//  vectors agrees with this implementation. Where an extracted model (`proofs/oracle`, the F* module of
//  the same name) asks the same question, the exporter ALSO runs the model on the vector's input, maps
//  the model's answer back into the reference's answer type, renders it with the exporter's one
//  renderer, and stamps the vector `["reference", "model:<Model>"]` — or fails the export, naming the
//  vector, the model and the proof-ladder row, when the two renders differ. A vector no model answers
//  is stamped `["reference"]`, so the difference stays visible to every host.
//
//  The bridges here map the reference's inputs INTO the model and the model's answers BACK, and
//  nothing else: the answer a vector records is rendered once, by the exporter, from either side. The
//  test-side differentials (`ProofOracle.*Tests`) read the same bridges rather than keep copies.
// ============================================================================

// Aliased before `Fuaran.Core` is opened: production's modules share the models' names.
module ModelProp = Propagation
module ModelDecimal = DecimalText

open Fuaran.Core

/// An F# option as the extracted models read one.
let toMOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> =
    match o with
    | Some v -> FStar_Pervasives_Native.Some v
    | None -> FStar_Pervasives_Native.None

/// A model's option, back.
let ofMOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option =
    match o with
    | FStar_Pervasives_Native.Some v -> Some v
    | FStar_Pervasives_Native.None -> None

/// One model's answer to a vector, as the exporter renders answers: the F* module, the ladder row
/// whose differential holds it to production, and the answer cell.
type Answer =
    { Model: string
      LadderRow: string
      Cell: CorpusCell }

/// The `answeredBy` member of vector `vectorId`, whose reference answer is `reference`: `["reference"]`
/// with no model answer, `["reference", "model:<Model>"]` when the model's answer renders the same.
/// A disagreement FAILS the export — nothing is written that claims an agreement the run did not see.
let stamp (vectorId: string) (reference: CorpusCell) (model: Answer option) : string * CorpusCell =
    let answer =
        model
        |> Option.map (fun m ->
            { ModelAnswer.Model = m.Model
              LadderRow = m.LadderRow
              Answer = CorpusFile.renderCell m.Cell })

    match AnsweredBy.agree vectorId (CorpusFile.renderCell reference) answer with
    | Ok a -> "answeredBy", CorpusFile.answeredBy a
    | Error e -> failwith ("the export refuses: " + e)

/// The same member as a JSON value, for a vector written as ONE compact canonical object (the
/// refusal and decode families), whose answer is compared as its canonical text.
let stampValue (vectorId: string) (reference: JVal) (model: (string * string * JVal) option) : string * JVal =
    let cell =
        model
        |> Option.map (fun (m, row, answer) ->
            { Model = m
              LadderRow = row
              Cell = CorpusCell.Value answer })

    match stamp vectorId (CorpusCell.Value reference) cell with
    | name, CorpusCell.Value v -> name, v
    | name, _ -> failwithf "the `%s` stamp is a compact value" name

// ---------------------------------------------------------------------------
//  The "not answered" rule (Phase 563, DECISIONS.md D232)
// ---------------------------------------------------------------------------
//
//  A model that asks a vector's question may still decline the vector's INPUT: the input lies outside
//  the domain the model is stated over (the column model's `OutOfModel`, a reader the parser model does
//  not carry). Such a vector is reference-only BY DECLARATION: its exporter names it, with the reason,
//  beside the vectors it emits. The export holds the declaration to the run in both directions — a
//  model that declines a vector the family did not declare fails the export, and so does a declared
//  vector the model answers after all, because a declaration that no longer matches the model is a
//  stamp saying less than the run checked. Nothing is skipped silently.

/// What a model said when the exporter asked it about one vector.
type Asked =
    /// The model answered; the stamp holds the answer to the reference's.
    | Answered of Answer
    /// The model declined the input as outside what it models.
    | Declined of model: string * ladderRow: string * why: string

/// The model answer for vector `vectorId`, under the family's declared out-of-model vectors
/// (`declared`, id to reason): the answer when the model answered an undeclared vector, none for a
/// declared vector the model declined, and a failed export for either mismatch.
let admit (declared: Map<string, string>) (vectorId: string) (asked: Asked) : Answer option =
    match asked, Map.tryFind vectorId declared with
    | Answered a, None -> Some a
    | Declined _, Some _ -> None
    | Declined(model, row, why), None ->
        failwithf
            "the export refuses: vector %s: the extracted model %s (ladder row %s) declines it as out of model (%s), and its family does not declare it reference-only"
            vectorId
            model
            row
            why
    | Answered a, Some reason ->
        failwithf
            "the export refuses: vector %s is declared reference-only (%s), but the extracted model %s (ladder row %s) answers it — the declaration is stale"
            vectorId
            reason
            a.Model
            a.LadderRow

// ---------------------------------------------------------------------------
//  The JSON syntax layer (`proofs/JsonParse.fst`, ladder row `parser-differential`)
// ---------------------------------------------------------------------------

/// The parser model, read through production's text and values.
module JsonModel =

    [<Literal>]
    let Model = "JsonParse"

    [<Literal>]
    let LadderRow = "parser-differential"

    let private inv = System.Globalization.CultureInfo.InvariantCulture

    /// A production character as the model reads it. Every character the parser DISTINGUISHES has
    /// its own constructor; every other is carried verbatim, so two different characters are never
    /// identified and the bridge loses nothing.
    let toCh (c: char) : JsonParse.ch =
        match c with
        | ' ' -> JsonParse.CSpace
        | '\t' -> JsonParse.CTab
        | '\n' -> JsonParse.CNewline
        | '\r' -> JsonParse.CReturn
        | '"' -> JsonParse.CQuote
        | '\\' -> JsonParse.CBackslash
        | '/' -> JsonParse.CSlash
        | '{' -> JsonParse.CLBrace
        | '}' -> JsonParse.CRBrace
        | '[' -> JsonParse.CLBrack
        | ']' -> JsonParse.CRBrack
        | ':' -> JsonParse.CColon
        | ',' -> JsonParse.CComma
        | '-' -> JsonParse.CMinus
        | '+' -> JsonParse.CPlus
        | '.' -> JsonParse.CDot
        | '0' -> JsonParse.CD0
        | '1' -> JsonParse.CD1
        | '2' -> JsonParse.CD2
        | '3' -> JsonParse.CD3
        | '4' -> JsonParse.CD4
        | '5' -> JsonParse.CD5
        | '6' -> JsonParse.CD6
        | '7' -> JsonParse.CD7
        | '8' -> JsonParse.CD8
        | '9' -> JsonParse.CD9
        | 'a' -> JsonParse.CLa
        | 'b' -> JsonParse.CLb
        | 'c' -> JsonParse.CLc
        | 'd' -> JsonParse.CLd
        | 'e' -> JsonParse.CLe
        | 'f' -> JsonParse.CLf
        | 'l' -> JsonParse.CLl
        | 'n' -> JsonParse.CLn
        | 'r' -> JsonParse.CLr
        | 's' -> JsonParse.CLs
        | 't' -> JsonParse.CLt
        | 'u' -> JsonParse.CLu
        | 'A' -> JsonParse.CUa
        | 'B' -> JsonParse.CUb
        | 'C' -> JsonParse.CUc
        | 'D' -> JsonParse.CUd
        | 'E' -> JsonParse.CUe
        | 'F' -> JsonParse.CUf
        // A RAW surrogate unit is one of the model's two surrogate constructors (Phase 306): the
        // parser distinguishes them — a string is well-formed UTF-16 or it is refused — and which
        // class a raw unit is in is this bridge's to say.
        | other when int other >= 0xD800 && int other <= 0xDBFF -> JsonParse.CHiSur(string other)
        | other when int other >= 0xDC00 && int other <= 0xDFFF -> JsonParse.CLoSur(string other)
        // A raw control other than the three whitespace ones above is the model's `CCtrl`
        // (Phase 462), carrying the `U+00XX` spelling production's refusal names it by: the
        // parser refuses it inside a string, and which units are controls is this bridge's to say.
        | other when int other < 0x20 -> JsonParse.CCtrl(string other, "U+" + (int other).ToString("X4"))
        | other -> JsonParse.COther(string other)

    let toChs (s: string) : JsonParse.ch list = s |> Seq.map toCh |> List.ofSeq

    /// The model's own spelling of a character, so the two sides never disagree merely about how a
    /// character is written.
    let chStr (c: JsonParse.ch) : string = JsonParse.ch_str c

    let tokStr (t: JsonParse.ch list) : string = t |> List.map chStr |> String.concat ""

    /// The four hex digits a `\uXXXX` carried, resolved to the code point HERE. The model declines
    /// to compute it (it carries no integers); the host can, so the differential still compares the
    /// decoded character rather than only the escape's guards.
    let hexVal (c: JsonParse.ch) : int = System.Convert.ToInt32(chStr c, 16)

    let ochToString (o: JsonParse.och) : string =
        match o with
        | JsonParse.OLit c -> chStr c
        | JsonParse.OEsc e ->
            match chStr e with
            | "n" -> "\n"
            | "r" -> "\r"
            | "t" -> "\t"
            | "b" -> "\b"
            | "f" -> "\f"
            | other -> other
        | JsonParse.OUni(a, b, c, d) ->
            string (char ((hexVal a <<< 12) + (hexVal b <<< 8) + (hexVal c <<< 4) + hexVal d))

    let modelStr (os: JsonParse.och list) : string =
        os |> List.map ochToString |> String.concat ""

    /// The model's one opacity parameter, instantiated: `System.Double.TryParse` plus the
    /// finiteness gate, which is exactly what `parseNumber` asks — and, since Phase 253, whether
    /// the token is the canonical float layout of the double it reads as (`FCanonical`), which is
    /// what `parseNumber` asks of an integer token past 2^53.
    let floatRead (tok: JsonParse.ch list) : JsonParse.freadv =
        let s = tokStr tok

        match System.Double.TryParse(s, System.Globalization.NumberStyles.Float, inv) with
        | true, v when System.Double.IsNaN v || System.Double.IsInfinity v -> JsonParse.FNonFinite
        | true, v when FloatLayout.finite v = s -> JsonParse.FCanonical
        | true, _ -> JsonParse.FFinite
        | _ -> JsonParse.FUnparsable

    /// The depth cap as the model spends it — one element per descent (`proofs/README.md`: the
    /// extraction carries no integers).
    let budget (n: int) : unit list = List.replicate n ()

    /// The null policy as the model reads it.
    let policy (p: NullPolicy) : JsonParse.policy =
        match p with
        | RejectNull -> JsonParse.RejectNull
        | EraseMemberNull -> JsonParse.EraseMemberNull

    /// The model's value as the reference's: a string by its decoded characters, a number token read
    /// back with the same .NET call production makes, so a model that scanned one character more or
    /// less reads as a different value.
    let rec toJVal (v: JsonParse.jval) : JVal =
        match v with
        | JsonParse.JStr s -> JStr(modelStr s)
        | JsonParse.JInt tok -> JInt(System.Int32.Parse(tokStr tok, inv))
        | JsonParse.JBool b -> JBool b
        | JsonParse.JFloat tok -> JFloat(System.Double.Parse(tokStr tok, System.Globalization.NumberStyles.Float, inv))
        | JsonParse.JArr xs -> JArr(xs |> List.map toJVal)
        | JsonParse.JObj fs -> JObj(fs |> List.map (fun (k, v) -> modelStr k, toJVal v))

    /// The model's read of `input` under the strict options (`ReadOptions.strict`: null refused, the
    /// depth cap the only bound): the value, or the refusal's class as the codec family spells it —
    /// a `JsonErrorKind` case name, the cap's breach `LimitExceeded/depth` (the lift
    /// `limited_at_no_bound_is_the_parser`).
    let readStrict (input: string) : Result<JVal, string> =
        let cap = ReadOptions.strict.Limits.MaxDepth

        match JsonParse.parse floatRead (string cap) (policy ReadOptions.strict.Null) (budget cap) (toChs input) with
        | JsonParse.ROk v -> Ok(toJVal v)
        | JsonParse.RErr(JsonParse.MaxDepthExceeded, _, _) -> Error("LimitExceeded/" + JsonLimit.name JsonLimit.Depth)
        | JsonParse.RErr(k, _, _) -> Error(sprintf "%A" k)
        | JsonParse.RLimit(l, _) -> Error(sprintf "LimitExceeded/%A" l)

// ---------------------------------------------------------------------------
//  Propagation (`proofs/Propagation.fst`, ladder row `propagation-differential`)
// ---------------------------------------------------------------------------

/// The propagation model, read through production's types.
[<RequireQualifiedAccess>]
module PropagationModel =

    [<Literal>]
    let Model = "Propagation"

    [<Literal>]
    let LadderRow = "propagation-differential"

    /// The deps as the model reads them (`sets-are-lists`).
    let deps (d: Map<string, Set<string>>) : ModelProp.dmap =
        d |> Map.toList |> List.map (fun (k, rs) -> k, Set.toList rs)

    /// A production evaluator as the model reads it: the resolver and the result cross the option and
    /// outcome shims and nothing else changes.
    let evaluator (ev: (string -> 'v option) -> string -> Result<'v, string>) : ModelProp.evaluator<'v> =
        fun resolve id ->
            match ev (fun k -> ofMOpt (resolve k)) id with
            | Ok v -> ModelProp.Ok v
            | Error m -> ModelProp.Error m

    let private error (e: ModelProp.propagation_error) : Propagation.PropagationError =
        match e with
        | ModelProp.EvalUnknownChange ids -> Propagation.PropagationError.EvalUnknownChange ids
        | ModelProp.EvalNodeFailed(n, m) -> Propagation.PropagationError.EvalNodeFailed(n, m)
        | ModelProp.EvalUndeclaredRead(n, r) -> Propagation.PropagationError.EvalUndeclaredRead(n, r)

    let private outcome
        (r: ModelProp.outcome<ModelProp.eval_outcome<'v>, ModelProp.propagation_error>)
        : Result<Propagation.EvalOutcome<'v>, Propagation.PropagationError> =
        match r with
        | ModelProp.Ok o ->
            Ok
                { Propagation.EvalOutcome.Values = Map.ofList o.values
                  Propagation.EvalOutcome.Cyclic = o.cyclic }
        | ModelProp.Error e -> Error(error e)

    /// `dirty_from_changed_ids`.
    let dirty (d: Map<string, Set<string>>) (changed: Set<string>) : Set<string> =
        ModelProp.dirty_from_changed_ids (deps d) (Set.toList changed) |> Set.ofList

    /// `sort`.
    let sort (d: Map<string, Set<string>>) : Propagation.TopoResult =
        let t = ModelProp.sort (deps d)
        { Order = t.order; Cycles = t.cycles }

    /// `eval`, over the read witness `touches` — the ids the evaluator reads at a node, which a pure
    /// model takes as a parameter because it cannot observe a call.
    let eval ev (touches: string -> string list) (d: Map<string, Set<string>>) =
        ModelProp.eval (evaluator ev) touches (deps d) |> outcome

    /// `eval_from` over `prior`.
    let evalFrom ev (touches: string -> string list) (prior: Map<string, 'v>) (changed: Set<string>) d =
        ModelProp.eval_from (evaluator ev) touches (Map.toList prior) (Set.toList changed) (deps d)
        |> outcome

    /// `walk_invoked` — the nodes `eval_from` invokes the evaluator on.
    let invoked ev (touches: string -> string list) (prior: Map<string, 'v>) (changed: Set<string>) d : Set<string> =
        ModelProp.walk_invoked (evaluator ev) touches (Map.toList prior) (Set.toList changed) (deps d)
        |> Set.ofList

// ---------------------------------------------------------------------------
//  Decimal text (`proofs/DecimalText.fst`, ladder row `decimal-text-differential`)
// ---------------------------------------------------------------------------

/// The decimal-text model, read through production's strings.
[<RequireQualifiedAccess>]
module DecimalModel =

    [<Literal>]
    let Model = "DecimalText"

    [<Literal>]
    let LadderRow = "decimal-text-differential"

    /// A text as the model reads it: one symbol per UTF-16 unit, anything outside the grammar's
    /// alphabet the model's `Other`.
    let toSyms (s: string) : ModelDecimal.sym list =
        [ for c in s ->
              if c = '-' then
                  ModelDecimal.Minus
              elif c = '.' then
                  ModelDecimal.Dot
              elif c >= '0' && c <= '9' then
                  ModelDecimal.Digit(System.Numerics.BigInteger(int c - int '0'))
              else
                  ModelDecimal.Other ]

    /// The model's symbols spelt back.
    let ofSyms (l: ModelDecimal.sym list) : string =
        l
        |> List.map (function
            | ModelDecimal.Minus -> "-"
            | ModelDecimal.Dot -> "."
            | ModelDecimal.Digit d -> string (int d)
            | ModelDecimal.Other -> "?")
        |> String.concat ""

    /// `try_canonical`.
    let tryCanonical (s: string) : string option =
        ModelDecimal.try_canonical (toSyms s) |> ofMOpt |> Option.map ofSyms

    /// `compare`.
    let compare (a: string) (b: string) : int option =
        ModelDecimal.compare (toSyms a) (toSyms b) |> ofMOpt |> Option.map int

    /// `add`.
    let add (a: string) (b: string) : string option =
        ModelDecimal.add (toSyms a) (toSyms b) |> ofMOpt |> Option.map ofSyms

    /// A decimal vector's model answer, its `expected` members as the exporter builds them.
    let answer (expected: (string * CorpusCell) list) : Answer option =
        Some
            { Model = Model
              LadderRow = LadderRow
              Cell = CorpusCell.Row expected }

// ---------------------------------------------------------------------------
//  The tree algebra (`proofs/TreeOps.fst`, ladder row `tree-differential`)
// ---------------------------------------------------------------------------

/// The tree-algebra model, over the reference node as the witness surface shows it — an id, a kind
/// tag and an ordered child list, and nothing else.
[<RequireQualifiedAccess>]
module TreeModel =

    [<Literal>]
    let Model = "TreeOps"

    [<Literal>]
    let LadderRow = "tree-differential"

    /// The reference node as the model reads it — the three accessors, and no more.
    let rec toTree (n: Reference.RNode) : TreeOps.tree =
        TreeOps.TNode(n.Id, n.Kind, n.Children |> List.map toTree)

    /// The model's tree back, as a reference node with the same three members.
    let rec ofTree (t: TreeOps.tree) : Reference.RNode =
        match t with
        | TreeOps.TNode(id, kind, children) -> Reference.RNode.node id kind (children |> List.map ofTree)

    /// A skeleton op as the model reads it, its nodes through `bridge`.
    let rec toOpWith (bridge: Reference.RNode -> TreeOps.tree) (op: SkeletonOp<Reference.RNode, string>) : TreeOps.op =
        match op with
        | InsertChild(p, node) -> TreeOps.InsertChild(p, bridge node)
        | RemoveNode t -> TreeOps.RemoveNode t
        | MoveNode(t, np) -> TreeOps.MoveNode(t, np)
        | ReorderChildren(p, order) -> TreeOps.ReorderChildren(p, order)
        | Batch inner -> TreeOps.Batch(inner |> List.map (toOpWith bridge))
        | UpdateNode node -> TreeOps.UpdateNode(bridge node)

    /// `apply`, over the plain scope: the applied tree, or the model's rejection.
    let apply
        (op: SkeletonOp<Reference.RNode, string>)
        (t: Reference.RNode)
        : Result<Reference.RNode, TreeOps.rejection> =
        match TreeOps.apply (toOpWith toTree op) (toTree t) with
        | DagFold.Ok r -> Ok(ofTree r)
        | DagFold.Error e -> Error e
