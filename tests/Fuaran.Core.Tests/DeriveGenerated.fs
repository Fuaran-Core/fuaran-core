// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen 0.35.2. Do not edit by hand.
module Fuaran.Core.Tests.DeriveGenerated

open Fuaran.Core

[<RequireQualifiedAccess>]
type Priority =
    | Low
    | Normal
    | High

[<RequireQualifiedAccess>]
type Rule =
    | Always
    | Equals of key: string * value: string
    | AllOf of rules: Rule list
    | Not of rule: Rule
    | Maybe of inner: Rule option
    | Guarded of guard: Guard
    | Named of rules: Map<string, Rule>

and [<RequireQualifiedAccess>] Trigger<'Msg> =
    | Timer of owner: Owner * every: int * fire: (int -> 'Msg)
    | Signal of owner: Owner * name: string * fire: (string -> 'Msg)
    | Manual of label: string

and [<RequireQualifiedAccess>] Measure =
    | Hours of amount: float
    | Days of amount: float

and Owner =
    {
      Name: string option
      Priority: Priority
    }

and Branch<'Msg> =
    {
      Label: string
      Body: Node<'Msg>
    }

and Guard =
    {
      Rule: Rule
      Note: string option
    }

// structure
and SectionSpec<'Msg> =
    {
      Title: string
      Children: Node<'Msg> list
      Owner: Owner option
      Estimate: Measure option
    }

// structure
and ChoiceSpec<'Msg> =
    {
      Branches: Branch<'Msg> list
      Otherwise: Node<'Msg>
      Rule: Rule
    }

// work
and TaskSpec<'Msg> =
    {
      Name: string
      Due: Rule
      Trigger: Trigger<'Msg>
      Hint: Node<'Msg> option
      Extras: Map<string, Node<'Msg>>
      Owner: Owner
    }

// work
and NoteSpec =
    {
      Text: string
      Priority: Priority
    }

and [<RequireQualifiedAccess>] NodeKind<'Msg> =
    | Section of SectionSpec<'Msg>
    | Choice of ChoiceSpec<'Msg>
    | Task of TaskSpec<'Msg>
    | Note of NoteSpec

and Node<'Msg> =
    {
      Id: string
      Kind: NodeKind<'Msg>
      Annotation: Node<'Msg> option
    }

let private encPriority (v: Priority) : JVal =
    match v with
    | Priority.Low -> JStr "Low"
    | Priority.Normal -> JStr "Normal"
    | Priority.High -> JStr "High"

// WIRE_FORMAT §5 — a non-finite double has no JSON *number* spelling, so it rides as
// one of the three quoted sentinel strings, which §7 requires a decoder to read back
// AT A FLOAT SLOT (`dFloat` below; `dInt` is deliberately not widened — §7 stops at
// the float slot, and an integer slot has no sentinel).
//
// Building the `JStr` HERE rather than leaving `Canon.render` to spell a non-finite
// `JFloat` is what keeps the emitted `JVal` renderable by the GUARDED
// `Fuaran.Core.Wire.tryRender`, which refuses a non-finite `JFloat` outright. The core
// wire model still has no non-finite float — the sentinel is a string, which it carries
// perfectly — so this widens the generated float slot's spelling, not the model.
let private encFloat (f: float) : JVal =
    if System.Double.IsNaN f then JStr "NaN"
    elif System.Double.IsPositiveInfinity f then JStr "Infinity"
    elif System.Double.IsNegativeInfinity f then JStr "-Infinity"
    else JFloat f

let rec private encNodeKind (k: NodeKind<'Msg>) : JVal =
    match k with
    | NodeKind.Section s -> encSectionSpec s
    | NodeKind.Choice s -> encChoiceSpec s
    | NodeKind.Task s -> encTaskSpec s
    | NodeKind.Note s -> encNoteSpec s

and private encNode (n: Node<'Msg>) : JVal =
    let kind = encNodeKind n.Kind

    JObj([ Some("id", JStr n.Id); Some("kind", kind); (n.Annotation |> Option.map (fun v -> "annotation", encNode v)) ] |> List.choose id)

and private encRule (v: Rule) : JVal =
    match v with
    | Rule.Always -> Canon.typed "Always" [  ]
    | Rule.Equals (key, value) -> Canon.typed "Equals" [ "key", JStr key; "value", JStr value ]
    | Rule.AllOf rules -> Canon.typed "AllOf" [ "rules", JArr(List.map encRule rules) ]
    | Rule.Not rule -> Canon.typed "Not" [ "rule", encRule rule ]
    | Rule.Maybe inner -> Canon.typed "Maybe" ([ (inner |> Option.map (fun v -> "inner", encRule v)) ] |> List.choose id)
    | Rule.Guarded guard -> Canon.typed "Guarded" [ "guard", encGuard guard ]
    | Rule.Named rules -> Canon.typed "Named" [ "rules", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encRule v))) rules ]

and private encTrigger<'Msg> (v: Trigger<'Msg>) : JVal =
    match v with
    | Trigger.Timer (owner, every, fire) -> Canon.typed "Timer" [ "owner", encOwner owner; "every", JInt every; "fire", JStr "<closure>" ]
    | Trigger.Signal (owner, name, fire) -> Canon.typed "Signal" [ "owner", encOwner owner; "name", JStr name; "fire", JStr "<closure>" ]
    | Trigger.Manual label -> Canon.typed "Manual" [ "label", JStr label ]

and private encMeasure (v: Measure) : JVal =
    match v with
    | Measure.Hours amount -> Canon.typed "Hours" [ "amount", encFloat amount ]
    | Measure.Days amount -> Canon.typed "Days" [ "amount", encFloat amount ]

and private encOwner (s: Owner) : JVal =
    JObj([ (s.Name |> Option.map (fun v -> "name", JStr v)); (if s.Priority = Priority.Normal then None else Some("priority", encPriority s.Priority)) ] |> List.choose id)

and private encBranch<'Msg> (s: Branch<'Msg>) : JVal =
    JObj([ Some("label", JStr s.Label); Some("body", encNode s.Body) ] |> List.choose id)

and private encGuard (s: Guard) : JVal =
    JObj([ Some("rule", encRule s.Rule); (s.Note |> Option.map (fun v -> "note", JStr v)) ] |> List.choose id)

and private encSectionSpec<'Msg> (s: SectionSpec<'Msg>) : JVal =
    Canon.typed "Section" ([ Some("title", JStr s.Title); Some("children", JArr(List.map encNode s.Children)); (s.Owner |> Option.map (fun v -> "owner", encOwner v)); (s.Estimate |> Option.map (fun v -> "estimate", encMeasure v)) ] |> List.choose id)

and private encChoiceSpec<'Msg> (s: ChoiceSpec<'Msg>) : JVal =
    Canon.typed "Choice" ([ Some("branches", JArr(List.map encBranch s.Branches)); Some("otherwise", encNode s.Otherwise); Some("rule", encRule s.Rule) ] |> List.choose id)

and private encTaskSpec<'Msg> (s: TaskSpec<'Msg>) : JVal =
    Canon.typed "Task" ([ Some("name", JStr s.Name); Some("due", encRule s.Due); Some("trigger", encTrigger s.Trigger); (s.Hint |> Option.map (fun v -> "hint", encNode v)); Some("extras", (fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, encNode v))) s.Extras); Some("owner", encOwner s.Owner) ] |> List.choose id)

and private encNoteSpec (s: NoteSpec) : JVal =
    Canon.typed "Note" ([ Some("text", JStr s.Text); (if s.Priority = Priority.Normal then None else Some("priority", encPriority s.Priority)) ] |> List.choose id)

let encodeNode (n: Node<'Msg>) : string = Canon.render (encNode n)

/// JVal-level accessors (Phase 694) — for host codecs that splice generated
/// encodings into a larger canonical document (e.g. a TreeOp codec).
let encodeNodeJson (n: Node<'Msg>) : JVal = encNode n

let encodeNodeKindJson (k: NodeKind<'Msg>) : JVal = encNodeKind k

// Phase 337 — a refusal is Core's `DecodeError`: a code from the closed `DecodeCode` set, the
// path from the value the outermost decoder was handed, what the position expected, and a
// sentence. The code and the path are the ones the IDL interpreter reports for the same
// document; the sentence is this layer's own.
let private dFail (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
    Error(DecodeError.make code expected message)

// One step further from the root — what a refusal gains as it leaves a member or an item.
let private dUnder (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match r with
    | Ok v -> Ok v
    | Error e -> Error(DecodeError.under step e)

let private dObj (j: JVal) : Result<(string * JVal) list, DecodeError> =
    match j with
    | JObj fs -> Ok fs
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"

// The discriminator: absent is `MissingField` naming it, a non-string `WrongKind` at it.
let private dTag (fs: (string * JVal) list) : Result<string, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = "$type") with
    | Some(_, JStr t) -> Ok t
    | Some _ -> dFail DecodeCode.WrongKind "string" "missing or non-string $type" |> dUnder (PathSegment.Key "$type")
    | None ->
        Error
            { Decoder.missing "$type" with
                Message = "missing or non-string $type" }

// A tag naming no case this decoder knows: `UnknownTag` at the discriminator.
let private dUnknown (expected: string) (message: string) : Result<'T, DecodeError> =
    dFail DecodeCode.UnknownTag expected message |> dUnder (PathSegment.Key "$type")

let private dStr (j: JVal) : Result<string, DecodeError> =
    match j with
    | JStr s -> Ok s
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"

let private dInt (j: JVal) : Result<int, DecodeError> =
    match j with
    | JInt i -> Ok i
    | _ -> dFail DecodeCode.WrongKind "int" "expected an int"

let private dBool (j: JVal) : Result<bool, DecodeError> =
    match j with
    | JBool b -> Ok b
    | _ -> dFail DecodeCode.WrongKind "bool" "expected a bool"

// A whole-valued float renders without a decimal point, so it parses back as JInt.
// WIRE_FORMAT §7 — a float slot also accepts the three quoted non-finite sentinels, which
// is how §5 spells a number JSON has no literal for. The value decodes to the FLOAT, never
// to the string: a host that answered the string would hand a consumer a different tree on
// the second decode while the bytes stayed identical. `dInt` is NOT widened — §7 stops at
// the float slot.
let private dFloat (j: JVal) : Result<float, DecodeError> =
    match j with
    | JFloat f -> Ok f
    | JInt i -> Ok(float i)
    | JStr "NaN" -> Ok System.Double.NaN
    | JStr "Infinity" -> Ok System.Double.PositiveInfinity
    | JStr "-Infinity" -> Ok System.Double.NegativeInfinity
    | _ -> dFail DecodeCode.WrongKind "number" "expected a number"

// Phase 347 — a closure / opaque slot holds one fixed sentinel string, read BY VALUE as the
// interpreter reads it: another string is `OutOfRange` (a string, but not the one value the slot
// takes), any other kind `WrongKind`. The sentinel carries nothing, so it decodes to `()`.
let private dSentinel (sentinel: string) (j: JVal) : Result<unit, DecodeError> =
    match j with
    | JStr s when s = sentinel -> Ok()
    | JStr _ -> dFail DecodeCode.OutOfRange "string" ("expected the sentinel " + sentinel)
    | _ -> dFail DecodeCode.WrongKind "string" "expected a string"

// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, DecodeError> = Ok j

let private dList (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<'T list, DecodeError> =
    match j with
    | JArr xs ->
        let rec go (i: int) (acc: 'T list) (rest: JVal list) =
            match rest with
            | [] -> Ok(List.rev acc)
            | x :: tail ->
                match dec x with
                | Ok v -> go (i + 1) (v :: acc) tail
                | Error e -> Error(DecodeError.under (PathSegment.Index i) e)

        go 0 [] xs
    | _ -> dFail DecodeCode.WrongKind "array" "expected an array"

// Every entry is checked, in document order; a repeated key keeps its FIRST value, as every
// member read does (Phase 347 — `Map.ofList` kept the last, as `JSON.parse` does).
let private dMap (dec: JVal -> Result<'T, DecodeError>) (j: JVal) : Result<Map<string, 'T>, DecodeError> =
    match j with
    | JObj fs ->
        (Ok Map.empty, fs)
        ||> List.fold (fun acc (k, v) ->
            match acc with
            | Error e -> Error e
            | Ok items ->
                dec v
                |> dUnder (PathSegment.Key k)
                |> Result.map (fun d -> if Map.containsKey k items then items else Map.add k d items))
    | _ -> dFail DecodeCode.WrongKind "object" "expected an object"

let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None ->
        Error
            { Decoder.missing name with
                Message = "missing required field '" + name + "'" }

let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) : Result<'T option, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name) |> Result.map Some
    | None -> Ok None

let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, DecodeError>) (dflt: 'T) : Result<'T, DecodeError> =
    match fs |> List.tryFind (fun (k, _) -> k = name) with
    | Some(_, v) -> dec v |> dUnder (PathSegment.Key name)
    | None -> Ok dflt

let private decPriority (j: JVal) : Result<Priority, DecodeError> =
    match j with
    | JStr "Low" -> Ok Priority.Low
    | JStr "Normal" -> Ok Priority.Normal
    | JStr "High" -> Ok Priority.High
    | JStr _ -> dFail DecodeCode.UnknownTag "one of 'Low', 'Normal', 'High'" "not a Priority"
    | _ -> dFail DecodeCode.WrongKind "string" "not a Priority"

let rec private decNodeKind (j: JVal) : Result<NodeKind<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dTag __fs |> Result.bind (fun __t ->
    match __t with
    | "Section" -> decSectionSpec j |> Result.map NodeKind.Section
    | "Choice" -> decChoiceSpec j |> Result.map NodeKind.Choice
    | "Task" -> decTaskSpec j |> Result.map NodeKind.Task
    | "Note" -> decNoteSpec j |> Result.map NodeKind.Note
    | __other -> dUnknown "one of 'Section', 'Choice', 'Task', 'Note'" ("unknown node kind: " + __other)))

and private decNode (j: JVal) : Result<Node<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "id" __fs dStr |> Result.bind (fun id ->
    dReq "kind" __fs decNodeKind |> Result.bind (fun kind ->
    dOpt "annotation" __fs decNode |> Result.bind (fun annotation ->
    Ok { Id = id; Kind = kind; Annotation = annotation }))))

and private decRule (j: JVal) : Result<Rule, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Always" -> Ok Rule.Always
        | "Equals" ->
            dReq "key" __fs dStr |> Result.bind (fun key ->
            dReq "value" __fs dStr |> Result.bind (fun value ->
            Ok(Rule.Equals(key, value))))
        | "AllOf" ->
            dReq "rules" __fs (dList decRule) |> Result.bind (fun rules ->
            Ok(Rule.AllOf(rules)))
        | "Not" ->
            dReq "rule" __fs decRule |> Result.bind (fun rule ->
            Ok(Rule.Not(rule)))
        | "Maybe" ->
            dOpt "inner" __fs decRule |> Result.bind (fun inner ->
            Ok(Rule.Maybe(inner)))
        | "Guarded" ->
            dReq "guard" __fs decGuard |> Result.bind (fun guard ->
            Ok(Rule.Guarded(guard)))
        | "Named" ->
            dReq "rules" __fs (dMap decRule) |> Result.bind (fun rules ->
            Ok(Rule.Named(rules)))
        | __other -> dUnknown "one of 'Always', 'Equals', 'AllOf', 'Not', 'Maybe', 'Guarded', 'Named'" ("unknown Rule case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Rule object"

and private decTrigger (j: JVal) : Result<Trigger<obj>, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Timer" ->
            dReq "owner" __fs decOwner |> Result.bind (fun owner ->
            dReq "every" __fs dInt |> Result.bind (fun every ->
            dReq "fire" __fs (fun (__j: JVal) -> dSentinel "<closure>" __j |> Result.map (fun () -> (fun (_: int) -> box "<closure>"))) |> Result.bind (fun fire ->
            Ok(Trigger.Timer(owner, every, fire)))))
        | "Signal" ->
            dReq "owner" __fs decOwner |> Result.bind (fun owner ->
            dReq "name" __fs dStr |> Result.bind (fun name ->
            dReq "fire" __fs (fun (__j: JVal) -> dSentinel "<closure>" __j |> Result.map (fun () -> (fun (_: string) -> box "<closure>"))) |> Result.bind (fun fire ->
            Ok(Trigger.Signal(owner, name, fire)))))
        | "Manual" ->
            dReq "label" __fs dStr |> Result.bind (fun label ->
            Ok(Trigger.Manual(label)))
        | __other -> dUnknown "one of 'Timer', 'Signal', 'Manual'" ("unknown Trigger case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Trigger object"

and private decMeasure (j: JVal) : Result<Measure, DecodeError> =
    match j with
    | JObj __fs ->
        dTag __fs |> Result.bind (fun __t ->
        match __t with
        | "Hours" ->
            dReq "amount" __fs dFloat |> Result.bind (fun amount ->
            Ok(Measure.Hours(amount)))
        | "Days" ->
            dReq "amount" __fs dFloat |> Result.bind (fun amount ->
            Ok(Measure.Days(amount)))
        | __other -> dUnknown "one of 'Hours', 'Days'" ("unknown Measure case: " + __other))
    | _ -> dFail DecodeCode.WrongKind "object" "expected a Measure object"

and private decOwner (j: JVal) : Result<Owner, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dOpt "name" __fs dStr |> Result.bind (fun name ->
    dDef "priority" __fs decPriority (Priority.Normal) |> Result.bind (fun priority ->
    Ok { Name = name; Priority = priority })))

and private decBranch (j: JVal) : Result<Branch<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "label" __fs dStr |> Result.bind (fun label ->
    dReq "body" __fs decNode |> Result.bind (fun body ->
    Ok { Label = label; Body = body })))

and private decGuard (j: JVal) : Result<Guard, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "rule" __fs decRule |> Result.bind (fun rule ->
    dOpt "note" __fs dStr |> Result.bind (fun note ->
    Ok { Rule = rule; Note = note })))

and private decSectionSpec (j: JVal) : Result<SectionSpec<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "title" __fs dStr |> Result.bind (fun title ->
    dReq "children" __fs (dList decNode) |> Result.bind (fun children ->
    dOpt "owner" __fs decOwner |> Result.bind (fun owner ->
    dOpt "estimate" __fs decMeasure |> Result.bind (fun estimate ->
    Ok { Title = title; Children = children; Owner = owner; Estimate = estimate })))))

and private decChoiceSpec (j: JVal) : Result<ChoiceSpec<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "branches" __fs (dList decBranch) |> Result.bind (fun branches ->
    dReq "otherwise" __fs decNode |> Result.bind (fun otherwise ->
    dReq "rule" __fs decRule |> Result.bind (fun rule ->
    Ok { Branches = branches; Otherwise = otherwise; Rule = rule }))))

and private decTaskSpec (j: JVal) : Result<TaskSpec<obj>, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "name" __fs dStr |> Result.bind (fun name ->
    dReq "due" __fs decRule |> Result.bind (fun due ->
    dReq "trigger" __fs decTrigger |> Result.bind (fun trigger ->
    dOpt "hint" __fs decNode |> Result.bind (fun hint ->
    dReq "extras" __fs (dMap decNode) |> Result.bind (fun extras ->
    dReq "owner" __fs decOwner |> Result.bind (fun owner ->
    Ok { Name = name; Due = due; Trigger = trigger; Hint = hint; Extras = extras; Owner = owner })))))))

and private decNoteSpec (j: JVal) : Result<NoteSpec, DecodeError> =
    dObj j |> Result.bind (fun __fs ->
    dReq "text" __fs dStr |> Result.bind (fun text ->
    dDef "priority" __fs decPriority (Priority.Normal) |> Result.bind (fun priority ->
    Ok { Text = text; Priority = priority })))

/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,
/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.
/// A refusal is Core's `DecodeError` (Phase 337): the code and path the IDL interpreter
/// reports for the same document, and this layer's sentence (`DecodeError.describe`).
let decodeNode (s: string) : Result<Node<obj>, DecodeError> =
    Decoder.parse s |> Result.bind decNode

/// The kind's wire tag — the discriminator it is encoded under.
let wireTag (n: Node<'Msg>) : string =
    match n.Kind with
    | NodeKind.Section _ -> "Section"
    | NodeKind.Choice _ -> "Choice"
    | NodeKind.Task _ -> "Task"
    | NodeKind.Note _ -> "Note"

/// Every wire tag this module's kinds are encoded under, in declaration order.
let allWireTags: string list = [ "Section"; "Choice"; "Task"; "Note" ]

/// The node's ordered structural children, in field order.
let children (n: Node<'Msg>) : Node<'Msg> list =
    match n.Kind with
    | NodeKind.Section s -> s.Children
    | NodeKind.Choice s -> [ s.Otherwise ]
    | _ -> []

/// The node with exactly this structural child list, its id and kind kept.
let withChildren (kids: Node<'Msg> list) (n: Node<'Msg>) : Node<'Msg> =
    match n.Kind with
    | NodeKind.Section s -> { n with Kind = NodeKind.Section { s with Children = kids } }
    | NodeKind.Choice s -> { n with Kind = NodeKind.Choice { s with Otherwise = List.head kids } }
    | _ -> n

let nodeWitness: NodeWitness<Node<'Msg>, string> =
    { Id = fun n -> n.Id
      KindTag = wireTag
      Children = children
      ReplaceChildren = fun n kids -> withChildren kids n }

// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side.
let runValidator (reg: Validator.Registry<Node<'Msg>, string>) (root: Node<'Msg>) : Defect<string> list =
    Validator.runAll nodeWitness reg root

// Smart constructors — required-without-default fields are parameters; IDL-declared
// defaults are filled, other optionals default to None.

let mkSection (id: string) (title: string) (children: Node<'Msg> list) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Section { Title = title; Children = children; Owner = None; Estimate = None }; Annotation = None }

let mkChoice (id: string) (branches: Branch<'Msg> list) (otherwise: Node<'Msg>) (rule: Rule) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Choice { Branches = branches; Otherwise = otherwise; Rule = rule }; Annotation = None }

let mkTask (id: string) (name: string) (due: Rule) (trigger: Trigger<'Msg>) (extras: Map<string, Node<'Msg>>) (owner: Owner) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Task { Name = name; Due = due; Trigger = trigger; Hint = None; Extras = extras; Owner = owner }; Annotation = None }

let mkNote (id: string) : Node<'Msg> =
    { Id = id; Kind = NodeKind.Note { Text = ""; Priority = Priority.Normal }; Annotation = None }

let rec private nodesInBranch (v: Branch<'Msg>) : Node<'Msg> list =
    [ v.Body ]

and private mapNodesInBranch (f: Node<'Msg> -> Node<'Msg>) (v: Branch<'Msg>) : Branch<'Msg> =
    let __m0 = f v.Body
    { v with Body = __m0 }

/// The nodes this node holds in keyed, non-structural positions, in declaration order.
let keyedChildren (n: Node<'Msg>) : Node<'Msg> list =
    let ofKind =
        match n.Kind with
        | NodeKind.Choice s -> List.collect (fun __x0 -> nodesInBranch __x0) s.Branches
        | NodeKind.Task s -> (match s.Hint with Some __o -> [ __o ] | None -> []) @ (s.Extras |> Map.toList |> List.collect (fun (_, __x0) -> [ __x0 ]))
        | _ -> []

    ofKind @ (match n.Annotation with Some __o -> [ __o ] | None -> [])

let private mapKeyed (f: Node<'Msg> -> Node<'Msg>) (n: Node<'Msg>) : Node<'Msg> =
    let kind =
        match n.Kind with
        | NodeKind.Choice s ->
            let __m0 = List.map (fun __x0 -> mapNodesInBranch f __x0) s.Branches
            NodeKind.Choice { s with Branches = __m0 }
        | NodeKind.Task s ->
            let __m0 = Option.map (fun __o -> f __o) s.Hint
            let __m1 = (s.Extras |> Map.toList |> List.map (fun (__k0, __x0) -> __k0, f __x0) |> Map.ofList)
            NodeKind.Task { s with Hint = __m0; Extras = __m1 }
        | k -> k

    let __e0 = Option.map (fun __o -> f __o) n.Annotation
    { n with Kind = kind; Annotation = __e0 }

/// The node with these nodes in its keyed positions, position for position (arity-preserving).
let withKeyedChildren (kids: Node<'Msg> list) (n: Node<'Msg>) : Node<'Msg> =
    let rest = ref kids

    let take (old: Node<'Msg>) =
        match rest.Value with
        | h :: t ->
            rest.Value <- t
            h
        | [] -> old

    mapKeyed take n

/// The generated full walk: every node position the vocabulary declares, structural and keyed.
let private idsUniqueFullWalk (root: Node<'Msg>) : bool =
    let rec go (seen: Set<string>) (stack: Node<'Msg> list) =
        match stack with
        | [] -> true
        | x :: rest ->
            if Set.contains x.Id seen then
                false
            else
                go (Set.add x.Id seen) (children x @ keyedChildren x @ rest)

    go Set.empty [ root ]

let keyedWitness: KeyedWitness<Node<'Msg>, string> =
    { Surface = "the generated full walk over every node position the vocabulary declares"
      KeyedChildren = keyedChildren
      ReplaceKeyedChildren = fun n kids -> withKeyedChildren kids n
      PlaceKeyedChild =
        fun n id ->
            match keyedChildren n with
            | first :: others -> Some(withKeyedChildren ({ first with Id = id } :: others) n)
            | [] -> None
      IdsUnique = idsUniqueFullWalk }

/// Every (field name, value) pair this node holds at the declared type `Rule`, kind fields first.
let slotsOfRule (n: Node<'Msg>) : (string * Rule) list =
    let ofKind =
        match n.Kind with
        | NodeKind.Choice s -> [ "rule", s.Rule ]
        | NodeKind.Task s -> [ "due", s.Due ]
        | _ -> []

    ofKind

/// Every (field name, value) pair this node holds at the declared type `Owner`, kind fields first.
let slotsOfOwner (n: Node<'Msg>) : (string * Owner) list =
    let ofKind =
        match n.Kind with
        | NodeKind.Section s -> (match s.Owner with Some __v -> [ "owner", __v ] | None -> [])
        | NodeKind.Task s -> [ "owner", s.Owner ]
        | _ -> []

    ofKind

/// The tree with its message type rewritten through `f`, every structure rebuilt.
let rec mapMsg<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (n: Node<'Msg>) : Node<'Msg2> =
    ({ Id = n.Id; Kind = mapMsgNodeKind f n.Kind; Annotation = Option.map (fun __o -> mapMsg f __o) n.Annotation }: Node<'Msg2>)

and private mapMsgNodeKind<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (k: NodeKind<'Msg>) : NodeKind<'Msg2> =
    match k with
    | NodeKind.Section s -> NodeKind.Section(mapMsgSectionSpec f s)
    | NodeKind.Choice s -> NodeKind.Choice(mapMsgChoiceSpec f s)
    | NodeKind.Task s -> NodeKind.Task(mapMsgTaskSpec f s)
    | NodeKind.Note s -> NodeKind.Note s

and private mapMsgBranch<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: Branch<'Msg>) : Branch<'Msg2> =
    ({ Label = v.Label; Body = mapMsg f v.Body }: Branch<'Msg2>)

and private mapMsgSectionSpec<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: SectionSpec<'Msg>) : SectionSpec<'Msg2> =
    ({ Title = v.Title; Children = List.map (fun __x0 -> mapMsg f __x0) v.Children; Owner = v.Owner; Estimate = v.Estimate }: SectionSpec<'Msg2>)

and private mapMsgChoiceSpec<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: ChoiceSpec<'Msg>) : ChoiceSpec<'Msg2> =
    ({ Branches = List.map (fun __x0 -> mapMsgBranch f __x0) v.Branches; Otherwise = mapMsg f v.Otherwise; Rule = v.Rule }: ChoiceSpec<'Msg2>)

and private mapMsgTaskSpec<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: TaskSpec<'Msg>) : TaskSpec<'Msg2> =
    ({ Name = v.Name; Due = v.Due; Trigger = mapMsgTrigger f v.Trigger; Hint = Option.map (fun __o -> mapMsg f __o) v.Hint; Extras = Map.map (fun _ __x0 -> mapMsg f __x0) v.Extras; Owner = v.Owner }: TaskSpec<'Msg2>)

and private mapMsgTrigger<'Msg, 'Msg2> (f: 'Msg -> 'Msg2) (v: Trigger<'Msg>) : Trigger<'Msg2> =
    match v with
    | Trigger.Timer(__f0, __f1, __f2) -> Trigger.Timer(__f0, __f1, (fun __a0 -> f ((__f2) __a0)))
    | Trigger.Signal(__f0, __f1, __f2) -> Trigger.Signal(__f0, __f1, (fun __a0 -> f ((__f2) __a0)))
    | Trigger.Manual(__f0) -> Trigger.Manual(__f0)

/// `NoteSpec` with every field at the value a caller need not pass.
let defaultNoteSpec: NoteSpec =
    { Text = ""; Priority = Priority.Normal }

/// `Owner` with every field at the value a caller need not pass.
let defaultOwner: Owner =
    { Name = None; Priority = Priority.Normal }

/// The kind tags of each declared category.
let kindCategories: Map<string, Set<string>> =
    Map.ofList [ "structure", set [ "Section"; "Choice" ]; "work", set [ "Task"; "Note" ] ]

/// The wire field names of each kind.
let kindFieldNames: Map<string, Set<string>> =
    Map.ofList [ "Section", set [ "title"; "children"; "owner"; "estimate" ]; "Choice", set [ "branches"; "otherwise"; "rule" ]; "Task", set [ "name"; "due"; "trigger"; "hint"; "extras"; "owner" ]; "Note", set [ "text"; "priority" ] ]

/// The wire field names of the node envelope.
let envelopeFieldNames: Set<string> =
    set [ "annotation" ]

/// The wire field names of each tree op.
let opFieldNames: Map<string, Set<string>> =
    Map.ofList [ "Move", set [ "target"; "index" ] ]

/// Derived members of `Rule`.
module Rule =
    /// Fold `folder` over this value and every nested `Rule` it holds, in preorder.
    let rec fold (folder: 'S -> Rule -> 'S) (state: 'S) (v: Rule) : 'S =
        let state = folder state v

        match v with
        | Rule.AllOf(__f0) -> List.fold (fun __s0 __x0 -> fold folder __s0 __x0) state __f0
        | Rule.Not(__f0) -> fold folder state __f0
        | Rule.Maybe(__f0) -> (match __f0 with Some __o0 -> fold folder state __o0 | None -> state)
        | Rule.Guarded(__f0) -> fold folder state __f0.Rule
        | Rule.Named(__f0) -> (__f0 |> Map.toList |> List.fold (fun __s0 (_, __x0) -> fold folder __s0 __x0) state)
        | _ -> state

/// Derived members of `Trigger`.
module Trigger =
    /// The `owner` field, where the case carries one.
    let owner (v: Trigger<'Msg>) : Owner option =
        match v with
        | Trigger.Timer(__v, _, _) -> Some __v
        | Trigger.Signal(__v, _, _) -> Some __v
        | _ -> None

/// Derived members of `Measure`.
module Measure =
    /// The `amount` field, which every case carries.
    let amount (v: Measure) : float =
        match v with
        | Measure.Hours(__v) -> __v
        | Measure.Days(__v) -> __v