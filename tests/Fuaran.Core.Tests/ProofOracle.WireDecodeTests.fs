module Fuaran.Core.Tests.ProofOracle.WireDecodeTests

// The `Proofs.Oracle` differential for `proofs/WireDecode.fst`: the extracted model
// (`proofs/oracle/WireDecode.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

type private MJValN = WireDecode.jvaln<int, float>

let rec private ofModel (v: MJVal) : JVal =
    match v with
    | WireDecode.JStr s -> JStr s
    | WireDecode.JInt i -> JInt i
    | WireDecode.JBool b -> JBool b
    | WireDecode.JFloat f -> JFloat f
    | WireDecode.JArr xs -> JArr(xs |> List.map ofModel)
    | WireDecode.JObj fields -> JObj(fields |> List.map (fun (k, v) -> k, ofModel v))

/// The BLIND bridge — the go-red instrument, and the decode family's counterpart to the blind
/// footprint the fold family hands its oracle. It reads every wire integer as a float, which is
/// exactly the numeric normalisation `JVal`'s doc warns a reader not to assume away, so the
/// oracle is asked about a different value from the one production was asked about and the
/// comparison must lose. A green report elsewhere is therefore known to be a comparison that CAN
/// fail rather than one that cannot.
let rec private toModelBlind (v: JVal) : MJVal =
    match v with
    | JInt i -> WireDecode.JFloat(float i)
    | JStr s -> WireDecode.JStr s
    | JBool b -> WireDecode.JBool b
    | JFloat f -> WireDecode.JFloat f
    | JArr xs -> WireDecode.JArr(xs |> List.map toModelBlind)
    | JObj fields -> WireDecode.JObj(fields |> List.map (fun (k, v) -> k, toModelBlind v))

/// `Decode.kindName`, which is private to that module — mirrored here because the reference
/// decoder below has to name a wrong shape the same way the shipped combinators do, and a
/// differential whose two sides spell a message differently measures the spelling.
let private kindWord (v: JVal) : string =
    match v with
    | JStr _ -> "string"
    | JInt _ -> "int"
    | JBool _ -> "bool"
    | JFloat _ -> "float"
    | JArr _ -> "array"
    | JObj _ -> "object"

// ---- one probe of the decode surface, both sides rendered the same way ----

type private Probe =
    { Name: string
      Production: string
      Oracle: string }

let private resR (render: 'a -> string) (r: Result<'a, string>) : string =
    match r with
    | Ok v -> "Ok " + render v
    | Error m -> "Error " + m

let private resO (render: 'a -> string) (o: WireDecode.outcome<'a>) : string =
    match o with
    | WireDecode.Ok v -> "Ok " + render v
    | WireDecode.Error m -> "Error " + m

let private asStr (s: string) = s
let private asFlt (f: float) = f.ToString("R", inv)
let private asStrs (xs: string list) = String.concat "" xs
let private asJson (v: JVal) = Json.render v
let private asModelJson (v: MJVal) = Json.render (ofModel v)

/// Every combinator in `Decode`, asked the same question of production and of the model.
/// `bridge` is the projection the oracle is handed — the identity in every real run and the
/// blind one only in the go-red case.
let private decodeProbes (bridge: JVal -> MJVal) (el: JVal) : Probe list =
    let m = bridge el

    let p name prod orac =
        { Name = name
          Production = prod
          Oracle = orac }

    [ p "asString" (resR asStr (Decoder.describing Decoder.str el)) (resO asStr (WireDecode.as_string m))
      p "asInt" (resR string (Decoder.describing Decoder.int el)) (resO string (WireDecode.as_int m))
      p "asBool" (resR string (Decoder.describing Decoder.bool el)) (resO string (WireDecode.as_bool m))
      p
          "asFloat"
          (resR asFlt (Decoder.describing Decoder.float el))
          (resO asFlt (WireDecode.as_float (fun (i: int) -> float i) m))
      p
          "kindOf"
          (resR asStr (Decoder.describing (Decoder.field "kind" Decoder.str) el))
          (resO asStr (WireDecode.kind_of m))
      p
          "getProp kind"
          (resR asJson (Decoder.describing (Decoder.field "kind" Decoder.json) el))
          (resO asModelJson (WireDecode.get_prop "kind" m))
      p
          "getProp $type"
          (resR asJson (Decoder.describing (Decoder.field "$type" Decoder.json) el))
          (resO asModelJson (WireDecode.get_prop "$type" m))
      p
          "getProp id"
          (resR asJson (Decoder.describing (Decoder.field "id" Decoder.json) el))
          (resO asModelJson (WireDecode.get_prop "id" m))
      p
          "getProp absent"
          (resR asJson (Decoder.describing (Decoder.field "no-such-member" Decoder.json) el))
          (resO asModelJson (WireDecode.get_prop "no-such-member" m))
      p
          "strField id"
          (resR asStr (Decoder.describing (Decoder.field "id" Decoder.str) el))
          (resO asStr (WireDecode.str_field "id" m))
      p
          "intField n"
          (resR string (Decoder.describing (Decoder.field "n" Decoder.int) el))
          (resO string (WireDecode.int_field "n" m))
      p
          "mapList asString"
          (resR asStrs (Decoder.describing (Decoder.list Decoder.str) el))
          (resO asStrs (WireDecode.map_list (fun (x: MJVal) -> WireDecode.as_string x) m)) ]

/// Every value in a document, root first — the combinators are asked about the scalars and the
/// nested objects too, not only the root, since that is where their refusal arms live.
let rec private everyValue (v: JVal) : JVal list =
    match v with
    | JArr xs -> v :: (xs |> List.collect everyValue)
    | JObj fields -> v :: (fields |> List.collect (fun (_, x) -> everyValue x))
    | _ -> [ v ]

let private emptyTally =
    { Disagreements = []
      Accepted = 0
      Refused = 0 }

/// Run every probe over every value of `el`, folding the disagreements and the two outcome
/// classes into `tally`.
let private runProbes (bridge: JVal -> MJVal) (label: string) (el: JVal) (tally: Tally) : Tally =
    everyValue el
    |> List.fold
        (fun acc v ->
            decodeProbes bridge v
            |> List.fold
                (fun (a: Tally) probe ->
                    let a =
                        if probe.Production.StartsWith "Ok" then
                            { a with Accepted = a.Accepted + 1 }
                        else
                            { a with Refused = a.Refused + 1 }

                    if probe.Production <> probe.Oracle then
                        { a with
                            Disagreements =
                                a.Disagreements
                                @ [ sprintf
                                        "%s: %s on %s\n  production: %s\n  oracle:     %s"
                                        label
                                        probe.Name
                                        (Json.render v)
                                        probe.Production
                                        probe.Oracle ] }
                    else
                        a)
                acc)
        tally

let private expectProbeAgreement (label: string) (t: Tally) =
    match t.Disagreements with
    | d :: _ -> failtestf "%s: the decode oracle and Wire.Decode DISAGREE\n%s" label d
    | [] ->
        // Both classes must have been reached, or the agreement certifies one arm only — the
        // same vacuity posture the fold family's `expectAgreement` takes.
        Expect.isGreaterThan
            t.Accepted
            0
            (sprintf "%s: no probe was ACCEPTED (accepted=%d refused=%d)" label t.Accepted t.Refused)

        Expect.isGreaterThan
            t.Refused
            0
            (sprintf "%s: no probe was REFUSED (accepted=%d refused=%d)" label t.Accepted t.Refused)

// ---- the reference vocabulary, on the production side ----

/// The vocabulary `WireDecode.rnode` models: one case per combinator the model exercises.
type RefNode =
    | RefText of string
    | RefFlag of bool
    | RefTags of string list
    | RefGroup of string * RefNode list

/// F#: `Json.kindObj` — the model's `encode`, on the production side.
let rec private encodeRef (n: RefNode) : JVal =
    match n with
    | RefText s -> Json.kindObj "text" [ "value", JStr s ]
    | RefFlag b -> Json.kindObj "flag" [ "on", JBool b ]
    | RefTags ts -> Json.kindObj "tags" [ "tags", JArr(ts |> List.map JStr) ]
    | RefGroup(id, items) -> Json.kindObj "group" [ "id", JStr id; "items", JArr(items |> List.map encodeRef) ]

/// The kind-dispatch node decoder a domain writes from the shipped combinators — the production
/// side of the model's `decode_node`, written against `Wire.Decode` and nothing else.
let rec private decodeRef (el: JVal) : Result<RefNode, string> =
    match Decoder.describing (Decoder.field "kind" Decoder.str) el with
    | Error m -> Error m
    | Ok tag ->
        if tag = "text" then
            Decoder.describing (Decoder.field "value" Decoder.str) el |> Result.map RefText
        elif tag = "flag" then
            Decoder.describing (Decoder.field "on" Decoder.json) el
            |> Result.bind (Decoder.describing Decoder.bool)
            |> Result.map RefFlag
        elif tag = "tags" then
            Decoder.describing (Decoder.field "tags" Decoder.json) el
            |> Result.bind (Decoder.describing (Decoder.list Decoder.str))
            |> Result.map RefTags
        elif tag = "group" then
            match Decoder.describing (Decoder.field "id" Decoder.str) el with
            | Error m -> Error m
            | Ok id ->
                match Decoder.describing (Decoder.field "items" Decoder.json) el with
                | Error m -> Error m
                | Ok(JArr ys) ->
                    let rec go acc rest =
                        match rest with
                        | [] -> Ok(List.rev acc)
                        | x :: t ->
                            match decodeRef x with
                            | Ok n -> go (n :: acc) t
                            | Error m -> Error m

                    go [] ys |> Result.map (fun ns -> RefGroup(id, ns))
                | Ok other -> Error("expected array, got " + kindWord other)
        else
            Error("unknown kind: " + tag)

let rec private renderRef (n: RefNode) : string =
    match n with
    | RefText s -> "text(" + s + ")"
    | RefFlag b -> "flag(" + string b + ")"
    | RefTags ts -> "tags[" + String.concat ";" ts + "]"
    | RefGroup(id, items) -> "group(" + id + ")[" + (items |> List.map renderRef |> String.concat ";") + "]"

let rec private renderRnode (n: WireDecode.rnode) : string =
    match n with
    | WireDecode.RText s -> "text(" + s + ")"
    | WireDecode.RFlag b -> "flag(" + string b + ")"
    | WireDecode.RTags ts -> "tags[" + String.concat ";" ts + "]"
    | WireDecode.RGroup(id, items) ->
        "group("
        + id
        + ")["
        + (items |> List.map renderRnode |> String.concat ";")
        + "]"

/// Production's node decoder and the model's, on the same document.
let private refDiff (el: JVal) : string option =
    let production = resR renderRef (decodeRef el)
    let oracle = resO renderRnode (WireDecode.decode_node (toModel el))

    if production <> oracle then
        Some(
            sprintf
                "the node decoders disagree on %s\n  production: %s\n  oracle:     %s"
                (Json.render el)
                production
                oracle
        )
    else
        None

// ---- generators ----

let rec private genJ (depth: int) (r: ConfRng.T) : JVal * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 4 else 6) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 8 r1
        JStr("s" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        JInt(n - 500), r2
    | 2 ->
        let n, r2 = ConfRng.intBelow 2 r1
        JBool(n = 0), r2
    | 3 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        JFloat(float n + 0.25), r2
    | 4 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let items, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let v, rr' = genJ (depth + 1) rr
                    acc @ [ v ], rr')
                ([], r2)
                [ 1..count ]

        JArr items, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 4 r1

        let fields, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let k, rr1 = ConfRng.intBelow 6 rr
                    let v, rr2 = genJ (depth + 1) rr1
                    // The key alphabet deliberately includes the names the probes and the
                    // reference decoder ask for, so the hit arms are reached as often as the
                    // miss arms.
                    let name = [| "kind"; "value"; "on"; "tags"; "id"; "items" |][k]
                    acc @ [ name, v ], rr2)
                ([], r2)
                [ 1..count ]

        JObj fields, r3

let rec private genRef (depth: int) (r: ConfRng.T) : RefNode * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 3 else 4) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 20 r1
        RefText("t" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 2 r1
        RefFlag(n = 0), r2
    | 2 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let ts, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let n, rr' = ConfRng.intBelow 20 rr
                    acc @ [ "g" + string n ], rr')
                ([], r2)
                [ 1..count ]

        RefTags ts, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 3 r1
        let idn, r3 = ConfRng.intBelow 20 r2

        let items, r4 =
            List.fold
                (fun (acc, rr) _ ->
                    let n, rr' = genRef (depth + 1) rr
                    acc @ [ n ], rr')
                ([], r3)
                [ 1..count ]

        RefGroup("i" + string idn, items), r4

// ---- the Phase 102 read policy ----

/// A document carrying the `null` token, rendered as the wire text production's two policies
/// actually read. Floats keep a fractional part: an integral `JFloat` renders without a point
/// and re-parses as `JInt` (the documented numeric normalisation), which would make the
/// comparison measure that normalisation rather than the policy.
let rec private renderN (d: MJValN) : string =
    match d with
    | WireDecode.NNull -> "null"
    | WireDecode.NStr s -> "\"" + Json.escape s + "\""
    | WireDecode.NInt i -> string i
    | WireDecode.NBool b -> (if b then "true" else "false")
    | WireDecode.NFloat f -> f.ToString("R", inv)
    | WireDecode.NArr xs -> "[" + (xs |> List.map renderN |> String.concat ",") + "]"
    | WireDecode.NObj fields ->
        "{"
        + (fields
           |> List.map (fun (k, v) -> "\"" + Json.escape k + "\":" + renderN v)
           |> String.concat ",")
        + "}"

let rec private genN (depth: int) (r: ConfRng.T) : MJValN * ConfRng.T =
    let pick, r1 = ConfRng.intBelow (if depth >= 2 then 5 else 7) r

    match pick with
    | 0 ->
        let n, r2 = ConfRng.intBelow 8 r1
        WireDecode.NStr("s" + string n), r2
    | 1 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        WireDecode.NInt(n - 500), r2
    | 2 ->
        let n, r2 = ConfRng.intBelow 2 r1
        WireDecode.NBool(n = 0), r2
    | 3 ->
        let n, r2 = ConfRng.intBelow 1000 r1
        WireDecode.NFloat(float n + 0.25), r2
    | 4 -> (WireDecode.NNull: MJValN), r1
    | 5 ->
        let count, r2 = ConfRng.intBelow 4 r1

        let items, r3 =
            List.fold
                (fun (acc, rr) _ ->
                    let v, rr' = genN (depth + 1) rr
                    acc @ [ v ], rr')
                ([], r2)
                [ 1..count ]

        WireDecode.NArr items, r3
    | _ ->
        let count, r2 = ConfRng.intBelow 4 r1

        // Phase 462: the keys are drawn WITHOUT replacement. The model reads a tree, where a
        // repeated key is two members; the parser reads text, where it is a refusal — so a
        // repeated key would measure the two models' different inputs, not their agreement.
        let fields, r3 =
            List.fold
                (fun (acc: (string * MJValN) list, rr) _ ->
                    let k, rr1 = ConfRng.intBelow 4 rr
                    let v, rr2 = genN (depth + 1) rr1
                    let key = "k" + string k

                    if acc |> List.exists (fun (k', _) -> k' = key) then
                        acc, rr2
                    else
                        acc @ [ key, v ], rr2)
                ([], r2)
                [ 1..count ]

        WireDecode.NObj fields, r3

type private PolicyTally =
    {
        Diffs: string list
        /// Documents the strict reader accepted.
        StrictOk: int
        /// Documents the strict reader refused.
        StrictErr: int
        /// Documents the POLICY fired on: strict refuses, tolerant accepts. Without these the
        /// tolerant leg would be a second run of the strict one.
        Fired: int
    }

/// The model's reader beside `Json.read` at the parser's own cap, under both policies, on the same
/// document — compared on the outcome class, the decoded value AND the message, the last of
/// which is the half that distinguishes the tolerant policy's "no absence to erase it to"
/// refusal from the strict policy's blanket one.
let private policyProbe (d: MJValN) (t: PolicyTally) : PolicyTally =
    let text = renderN d

    let side (policy: NullPolicy) (mp: WireDecode.null_policy) =
        let production =
            match
                Json.read
                    { ReadOptions.strict with
                        Null = policy }
                    text
            with
            | Ok v -> "Ok " + Json.render v
            | Error(JsonRefusal.Malformed e) -> "Error " + e.Message
            | Error(JsonRefusal.LimitExceeded b) -> sprintf "Breach %A" b

        let oracle =
            match WireDecode.read mp d with
            | WireDecode.Ok v -> "Ok " + Json.render (ofModel v)
            | WireDecode.Error m -> "Error " + m

        production, oracle

    let strictP, strictO = side RejectNull WireDecode.RejectNull
    let lenientP, lenientO = side EraseMemberNull WireDecode.EraseMemberNull

    let diffs =
        [ if strictP <> strictO then
              sprintf "strict read of %s\n  production: %s\n  oracle:     %s" text strictP strictO
          if lenientP <> lenientO then
              sprintf "tolerant read of %s\n  production: %s\n  oracle:     %s" text lenientP lenientO ]

    { Diffs = t.Diffs @ diffs
      StrictOk = t.StrictOk + (if strictP.StartsWith "Ok" then 1 else 0)
      StrictErr = t.StrictErr + (if strictP.StartsWith "Ok" then 0 else 1)
      Fired =
        t.Fired
        + (if not (strictP.StartsWith "Ok") && lenientP.StartsWith "Ok" then
               1
           else
               0) }

// ---------------------------------------------------------------------------
//  Phase 152 — KEY ORDER, differentially.
//
//  WIRE_FORMAT §2 rule 2 obliges a decoder to accept an object's members in any order, and §20
//  ratifies that the same bytes decode to the same tree on every conformant host. Every fixture
//  in the corpus is canonically ordered, so a decoder that silently read member order would pass
//  all of them — which is what makes this family different from the Phase 135 one above. It
//  SHUFFLES: every object of every document, at every depth, seeded and replayable, and then asks
//  three questions of each shuffle.
//
//    1. Is the shuffle an instance of the theorem at all? The extracted `member_perm` — the model's
//       own relation, not a second one written here — must hold of the pair. A shuffle the
//       relation does not relate would be measuring something the lemma never claimed.
//    2. Does PRODUCTION answer the same on the shuffle as on the original? Every combinator whose
//       result carries no members is compared for equality, message included; `getProp`, whose
//       result is a subtree and so is itself reordered, is compared through the model's
//       `outcome_perm`.
//    3. Does the oracle still agree with production on the shuffled document? The Phase 135 probes,
//       on documents no fixture contains.
//
//  Duplicate-keyed documents are FILTERED OUT, using the extracted `keys_unique_deep` rather than
//  a predicate written here, because on a repeated key `List.tryFind` genuinely does depend on
//  order — see `duplicate_keys_break_order_invariance`, which proves the premise necessary rather
//  than assuming it away. Arrays are NOT shuffled: an array's order is content.
// ---------------------------------------------------------------------------

/// Fisher–Yates over the suite's own replayable generator.
let private shuffleList (xs: 'a list) (r0: ConfRng.T) : 'a list * ConfRng.T =
    let arr = List.toArray xs
    let mutable r = r0

    for i in (arr.Length - 1) .. -1 .. 1 do
        let j, r' = ConfRng.intBelow (i + 1) r
        r <- r'
        let tmp = arr[i]
        arr[i] <- arr[j]
        arr[j] <- tmp

    List.ofArray arr, r

/// Reorder the members of EVERY object in the document, at every depth. Arrays keep their order.
let rec private shuffleDeep (v: JVal) (r0: ConfRng.T) : JVal * ConfRng.T =
    match v with
    | JArr xs ->
        let items, r =
            xs
            |> List.fold
                (fun (acc, rr) x ->
                    let x', rr' = shuffleDeep x rr
                    acc @ [ x' ], rr')
                ([], r0)

        JArr items, r
    | JObj fields ->
        let inner, r1 =
            fields
            |> List.fold
                (fun (acc, rr) (k, x) ->
                    let x', rr' = shuffleDeep x rr
                    acc @ [ k, x' ], rr')
                ([], r0)

        let reordered, r2 = shuffleList inner r1
        JObj reordered, r2
    | _ -> v, r0

/// The model's own deep duplicate-free predicate, asked of a production value.
let private keysUniqueDeep (v: JVal) : bool = WireDecode.keys_unique_deep (toModel v)

/// The model's own relation, asked of a pair of production values.
let private relatedByModel (a: JVal) (b: JVal) : bool =
    WireDecode.member_perm (toModel a) (toModel b)

/// A decoder — the parameter is `getProp`, so the go-red instrument below can be the SAME decoder
/// reading members by position instead of by name. Everything else is `Wire.Decode`.
type private GetProp = string -> JVal -> Result<JVal, string>

/// The production reading of a member, in the string-error shape the probes take: the typed
/// `Decoder.field` described (the `Decode.getProp` this stood for left at `0.37.0`, Phase 386).
let private getProp: GetProp =
    fun name el -> Decoder.describing (Decoder.field name Decoder.json) el

let rec private decodeRefWith (getProp: GetProp) (el: JVal) : Result<RefNode, string> =
    let strField name e =
        getProp name e |> Result.bind (Decoder.describing Decoder.str)

    match getProp "kind" el |> Result.bind (Decoder.describing Decoder.str) with
    | Error m -> Error m
    | Ok tag ->
        if tag = "text" then
            strField "value" el |> Result.map RefText
        elif tag = "flag" then
            getProp "on" el
            |> Result.bind (Decoder.describing Decoder.bool)
            |> Result.map RefFlag
        elif tag = "tags" then
            getProp "tags" el
            |> Result.bind (Decoder.describing (Decoder.list Decoder.str))
            |> Result.map RefTags
        elif tag = "group" then
            match strField "id" el with
            | Error m -> Error m
            | Ok id ->
                match getProp "items" el with
                | Error m -> Error m
                | Ok(JArr ys) ->
                    let rec go acc rest =
                        match rest with
                        | [] -> Ok(List.rev acc)
                        | x :: t ->
                            match decodeRefWith getProp x with
                            | Ok n -> go (n :: acc) t
                            | Error m -> Error m

                    go [] ys |> Result.map (fun ns -> RefGroup(id, ns))
                | Ok other -> Error("expected array, got " + kindWord other)
        else
            Error("unknown kind: " + tag)

/// THE GO-RED INSTRUMENT: a `getProp` that reads the FIRST member of an object rather than the
/// one it was asked for. On the canonically-ordered corpus it is very nearly right — which is the
/// point, and why the corpus alone could not catch it. Under a shuffle it must lose.
let private getPropByPosition (name: string) (el: JVal) : Result<JVal, string> =
    match el with
    | JObj((_, v) :: _) -> Ok v
    | JObj [] -> Error("missing property: " + name)
    | other -> Error("expected object, got " + kindWord other)

/// Production's answers to the questions the theorem says are order-insensitive. Each result
/// carries no members of its own, so these are compared for EQUALITY — message included.
let private orderFreeAnswers (getProp: GetProp) (el: JVal) : (string * string) list =
    [ "kindOf", resR asStr (getProp "kind" el |> Result.bind (Decoder.describing Decoder.str))
      "strField id", resR asStr (getProp "id" el |> Result.bind (Decoder.describing Decoder.str))
      "strField value", resR asStr (getProp "value" el |> Result.bind (Decoder.describing Decoder.str))
      "intField n", resR string (getProp "n" el |> Result.bind (Decoder.describing Decoder.int))
      "asString", resR asStr (Decoder.describing Decoder.str el)
      "asInt", resR string (Decoder.describing Decoder.int el)
      "asBool", resR string (Decoder.describing Decoder.bool el)
      "asFloat", resR asFlt (Decoder.describing Decoder.float el)
      "mapList asString", resR asStrs (Decoder.describing (Decoder.list Decoder.str) el)
      "decodeRef", resR renderRef (decodeRefWith getProp el) ]

/// `getProp`'s own result is a SUBTREE, so the two answers are related rather than equal — and
/// the relation they are compared by is the extracted model's, which is the one the lemma is
/// about.
let private getPropRelated (getProp: GetProp) (name: string) (a: JVal) (b: JVal) : bool =
    let toO (r: Result<JVal, string>) : WireDecode.outcome<MJVal> =
        match r with
        | Ok v -> WireDecode.Ok(toModel v)
        | Error m -> WireDecode.Error m

    WireDecode.outcome_perm (toO (getProp name a)) (toO (getProp name b))

type private ShuffleTally =
    {
        Diffs: string list
        /// Documents the filter admitted (duplicate-free at every depth).
        Admitted: int
        /// Shuffles that actually MOVED a member — without these the family re-runs Phase 135's.
        Moved: int
        /// Values the node decoder ACCEPTED, across all shuffles.
        Accepted: int
        /// Values it refused. Both arms must be reached, or the agreement certifies one of them.
        Refused: int
        /// Pairs the extracted relation did NOT relate — a defect in the shuffle, not in the tree.
        Unrelated: int
    }

let private emptyShuffleTally =
    { Diffs = []
      Admitted = 0
      Moved = 0
      Accepted = 0
      Refused = 0
      Unrelated = 0 }

/// One document, `trials` shuffles of it. `getProp` is `Decode.getProp` in every real run and the
/// positional instrument only in the go-red case.
let private shuffleProbe
    (getProp: GetProp)
    (label: string)
    (trials: int)
    (seed: int)
    (el: JVal)
    (tally: ShuffleTally)
    : ShuffleTally =
    if not (keysUniqueDeep el) then
        tally
    else
        let baseline = orderFreeAnswers getProp el
        let mutable r = ConfRng.ofSeed seed

        let mutable t =
            { tally with
                Admitted = tally.Admitted + 1 }

        for i in 1..trials do
            let shuffled, r' = shuffleDeep el r
            r <- r'

            if shuffled <> el then
                t <- { t with Moved = t.Moved + 1 }

            // 1. the shuffle is an instance of the relation the lemma is about
            if not (relatedByModel el shuffled) then
                t <-
                    { t with
                        Unrelated = t.Unrelated + 1
                        Diffs =
                            t.Diffs
                            @ [ sprintf
                                    "%s/%d: the extracted member_perm does NOT relate the document to its shuffle\n  original: %s\n  shuffled: %s"
                                    label
                                    i
                                    (Json.render el)
                                    (Json.render shuffled) ] }

            // 2. production answers the same on the shuffle as on the original
            let answers = orderFreeAnswers getProp shuffled

            for idx in 0 .. baseline.Length - 1 do
                let name, before = baseline[idx]
                let _, after = answers[idx]

                if before <> after then
                    t <-
                        { t with
                            Diffs =
                                t.Diffs
                                @ [ sprintf
                                        "%s/%d: %s MOVED under a member reordering\n  original: %s -> %s\n  shuffled: %s -> %s"
                                        label
                                        i
                                        name
                                        (Json.render el)
                                        before
                                        (Json.render shuffled)
                                        after ] }

            for name in [ "kind"; "id"; "value"; "items"; "tags"; "on"; "no-such-member" ] do
                if not (getPropRelated getProp name el shuffled) then
                    t <-
                        { t with
                            Diffs =
                                t.Diffs
                                @ [ sprintf
                                        "%s/%d: getProp %s answers an UNRELATED subtree under a member reordering\n  original: %s\n  shuffled: %s"
                                        label
                                        i
                                        name
                                        (Json.render el)
                                        (Json.render shuffled) ] }

            match decodeRefWith getProp shuffled with
            | Ok _ -> t <- { t with Accepted = t.Accepted + 1 }
            | Error _ -> t <- { t with Refused = t.Refused + 1 }

        t

/// The tally's own vacuity guards: a run that admitted nothing, or that never actually moved a
/// member, proves nothing whatever else it reports. Which arms of the node decoder a pool reaches
/// is a property of that pool, so each case asserts its own.
let private expectShuffleAgreement (label: string) (t: ShuffleTally) =
    match t.Diffs with
    | d :: _ -> failtestf "%s: a member reordering CHANGED a decode answer\n%s" label d
    | [] ->
        Expect.isGreaterThan t.Admitted 0 (sprintf "%s: no document passed the duplicate-free filter" label)

        Expect.isGreaterThan
            t.Moved
            0
            (sprintf "%s: no shuffle actually moved a member — the family re-ran the unshuffled probes" label)

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 135: the DECODE model beside Wire.Decode ----

          testCase "the decode oracle agrees with Wire.Decode over every nodes/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 50 "the nodes/ family carries a real corpus"

                  files
                  |> Array.fold
                      (fun acc path ->
                          let name = Path.GetFileNameWithoutExtension path

                          match Json.parse (File.ReadAllText path) with
                          | Error e -> failtestf "%s: not JSON (%s)" name e
                          | Ok v -> runProbes toModel name v acc)
                      emptyTally
                  |> expectProbeAgreement "nodes/ fixtures"

          testCase "the decode oracle agrees with Wire.Decode over every ops/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "ops"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 8 "the ops/ family carries a real corpus"

                  files
                  |> Array.fold
                      (fun acc path ->
                          let name = Path.GetFileNameWithoutExtension path

                          match Json.parse (File.ReadAllText path) with
                          | Error e -> failtestf "%s: not JSON (%s)" name e
                          | Ok v -> runProbes toModel name v acc)
                      emptyTally
                  |> expectProbeAgreement "ops/ fixtures"

          testCase "the decode oracle agrees with Wire.Decode over a generated JVal sample"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 8100
              let mutable tally = emptyTally

              for i in 1..400 do
                  let v, r' = genJ 0 rng
                  rng <- r'
                  tally <- runProbes toModel (sprintf "generated %d" i) v tally

              expectProbeAgreement "generated JVal sample" tally

          testCase "the model's node decoder inverts its encoder, on both sides"
          <| fun _ ->
              // `decode_encode_roundtrip` is proved on the model; this is that theorem's
              // instance on the EXTRACTED code, run beside the same decoder written from the
              // shipped combinators. The two together are what tie the proof to production.
              let mutable rng = ConfRng.ofSeed 6100
              let mutable decoded = 0

              for _ in 1..150 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  let el = encodeRef n

                  match refDiff el with
                  | Some d -> failtest d
                  | None -> ()

                  Expect.equal (decodeRef el) (Ok n) "production decodes its own encoding"
                  decoded <- decoded + 1

              Expect.isGreaterThan decoded 0 "the round-trip sample is non-empty"

          testCase "the model's node decoder agrees with production on documents it REFUSES"
          <| fun _ ->
              // The accept path is the round trip above; this is the refusal path, where the
              // MESSAGE is the whole content of the comparison. Corpus fixtures first (real
              // documents in a vocabulary this decoder does not know), then a generated sample
              // whose key alphabet is the reference vocabulary's own, so the near-misses — right
              // tag, wrong member type — are reached as well as the obvious rejects.
              let mutable refused = 0

              let check (el: JVal) =
                  match refDiff el with
                  | Some d -> failtest d
                  | None ->
                      match decodeRef el with
                      | Error _ -> refused <- refused + 1
                      | Ok _ -> ()

              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  for path in Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort do
                      match Json.parse (File.ReadAllText path) with
                      | Error e -> failtestf "%s: not JSON (%s)" (Path.GetFileNameWithoutExtension path) e
                      | Ok v ->
                          for el in everyValue v do
                              check el

              let mutable rng = ConfRng.ofSeed 6200

              for _ in 1..400 do
                  let v, r' = genJ 0 rng
                  rng <- r'

                  for el in everyValue v do
                      check el

              Expect.isGreaterThan refused 0 "documents were refused, so the message comparison ran"

          testCase "the model's null-policy reader agrees with the parser under BOTH policies"
          <| fun _ ->
              // The Phase 102 promise, tied to production. The model reads a document TREE where
              // the parser reads text, so each generated document is rendered to the wire bytes
              // the parser actually takes — see `renderN` and the boundary note in
              // proofs/README.md for what that comparison does and does not establish.
              let fixtures: MJValN list =
                  [ WireDecode.NNull
                    WireDecode.NArr [ WireDecode.NNull ]
                    WireDecode.NArr [ WireDecode.NInt 1; WireDecode.NNull ]
                    WireDecode.NObj [ "a", WireDecode.NNull ]
                    WireDecode.NObj [ "a", WireDecode.NNull; "b", WireDecode.NInt 1 ]
                    WireDecode.NObj [ "a", WireDecode.NObj [ "b", WireDecode.NNull ] ]
                    WireDecode.NObj [ "a", WireDecode.NArr [ WireDecode.NNull ] ] ]

              let mutable t =
                  { Diffs = []
                    StrictOk = 0
                    StrictErr = 0
                    Fired = 0 }

              for d in fixtures do
                  t <- policyProbe d t

              let mutable rng = ConfRng.ofSeed 7100

              for _ in 1..400 do
                  let d, r' = genN 0 rng
                  rng <- r'
                  t <- policyProbe d t

              match t.Diffs with
              | d :: _ -> failtestf "the read-policy oracle and the parser DISAGREE\n%s" d
              | [] ->
                  Expect.isGreaterThan t.StrictOk 0 "some documents were accepted"
                  Expect.isGreaterThan t.StrictErr 0 "some documents were refused"
                  Expect.isGreaterThan t.Fired 0 "the policy FIRED — strict refused where tolerant accepted"

          testCase "a decode oracle handed a blind bridge DISAGREES with Wire.Decode"
          <| fun _ ->
              // The teeth. The blind bridge reads every wire integer as a float, so the oracle is
              // asked about a different value from the one production was asked about: `asInt`
              // must lose. A green report above is therefore a comparison that CAN fail.
              let tally = runProbes toModelBlind "blind" (JObj [ "n", JInt 7 ]) emptyTally

              match tally.Disagreements with
              | [] ->
                  failtest
                      "a blind bridge reads every integer as a float, so asInt must disagree — this comparison cannot lose"
              | ds ->
                  Expect.isTrue
                      (ds |> List.exists (fun d -> d.Contains "asInt"))
                      (sprintf "the disagreement names the arm that moved — got:\n%s" (List.head ds))

                  // … and it loses over the generated sample too, not only on a hand-made value.
                  let mutable rng = ConfRng.ofSeed 9100
                  let mutable swept = emptyTally

                  for _ in 1..60 do
                      let v, r' = genJ 0 rng
                      rng <- r'
                      swept <- runProbes toModelBlind "blind" v swept

                  Expect.isNonEmpty swept.Disagreements "the blind bridge loses over the generated sample as well"

          // ---- Phase 152: the decode surface under a MEMBER REORDERING ----

          testCase "every combinator answers the same on every shuffle of every nodes/ fixture"
          <| fun _ ->
              // Real documents, in a real vocabulary, with their members reordered at every depth
              // — the case the corpus itself cannot present, because every fixture in it is
              // canonically ordered.
              match SiblingCorpus.resolve "nodes" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "nodes"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 50 "the nodes/ family carries a real corpus"

                  let tally =
                      files
                      |> Array.fold
                          (fun acc path ->
                              let name = Path.GetFileNameWithoutExtension path

                              match Json.parse (File.ReadAllText path) with
                              | Error e -> failtestf "%s: not JSON (%s)" name e
                              | Ok v ->
                                  everyValue v
                                  |> List.fold (fun a el -> shuffleProbe getProp name 4 15201 el a) acc)
                          emptyShuffleTally

                  expectShuffleAgreement "nodes/ fixtures, shuffled" tally

                  Expect.isGreaterThan
                      tally.Refused
                      0
                      "the reference decoder refused — the corpus is in a vocabulary it does not know, so this is its arm"

                  // … and the oracle still agrees with production on the shuffled documents,
                  // which is the Phase 135 comparison over inputs no fixture contains.
                  let probes =
                      files
                      |> Array.fold
                          (fun acc path ->
                              match Json.parse (File.ReadAllText path) with
                              | Error e -> failtestf "%s: not JSON (%s)" (Path.GetFileNameWithoutExtension path) e
                              | Ok v ->
                                  if keysUniqueDeep v then
                                      let shuffled, _ = shuffleDeep v (ConfRng.ofSeed 15202)
                                      runProbes toModel (Path.GetFileNameWithoutExtension path) shuffled acc
                                  else
                                      acc)
                          emptyTally

                  expectProbeAgreement "nodes/ fixtures, shuffled" probes

          testCase "every combinator answers the same on every shuffle of every ops/ fixture"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let files = Directory.GetFiles(Path.Combine(root, "ops"), "*.json") |> Array.sort
                  Expect.isGreaterThan files.Length 8 "the ops/ family carries a real corpus"

                  files
                  |> Array.fold
                      (fun acc path ->
                          let name = Path.GetFileNameWithoutExtension path

                          match Json.parse (File.ReadAllText path) with
                          | Error e -> failtestf "%s: not JSON (%s)" name e
                          | Ok v ->
                              everyValue v
                              |> List.fold (fun a el -> shuffleProbe getProp name 4 15203 el a) acc)
                      emptyShuffleTally
                  |> expectShuffleAgreement "ops/ fixtures, shuffled"

          testCase "the node decoder returns the SAME TREE on every shuffle — §20's one answer"
          <| fun _ ->
              // The accept path, which the corpus pools above cannot reach: documents in the
              // reference vocabulary, encoded, then reordered at every depth. `decode_node`'s
              // result carries no members of its own, so the lemma claims literal equality and
              // this is where that is measured.
              let mutable rng = ConfRng.ofSeed 15204
              let mutable tally = emptyShuffleTally
              let mutable exact = 0

              for i in 1..150 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  let el = encodeRef n
                  tally <- shuffleProbe getProp (sprintf "ref %d" i) 4 (15300 + i) el tally

                  // … and the tree it returns is the node that was encoded, not merely a stable
                  // answer: a decoder that answered `Error` consistently would pass the above.
                  let shuffled, r'' = shuffleDeep el rng
                  rng <- r''
                  Expect.equal (decodeRef shuffled) (Ok n) "the reordered encoding decodes to the same node"
                  exact <- exact + 1

              expectShuffleAgreement "reference vocabulary, shuffled" tally

              Expect.isGreaterThan tally.Accepted 0 "the node decoder ACCEPTED — this pool is its accept arm"
              Expect.isGreaterThan exact 0 "the round-trip-under-shuffle sample is non-empty"

              // The refusal arm under a shuffle, where the MESSAGE is the whole comparison.
              let mutable rng2 = ConfRng.ofSeed 15205
              let mutable refusals = emptyShuffleTally

              for i in 1..300 do
                  let v, r' = genJ 0 rng2
                  rng2 <- r'

                  for el in everyValue v do
                      refusals <- shuffleProbe getProp (sprintf "generated %d" i) 3 (15400 + i) el refusals

              expectShuffleAgreement "generated documents, shuffled" refusals
              Expect.isGreaterThan refusals.Refused 0 "the node decoder REFUSED — the message comparison ran"

          testCase "a decoder that reads the FIRST member rather than the named one LOSES on a shuffle"
          <| fun _ ->
              // The teeth. `getPropByPosition` is `Wire.Decode` with one clause changed: it takes
              // the first member of an object instead of the one asked for. On the canonically
              // ordered corpus it is very nearly right, and every Phase 135 pool would pass it —
              // which is precisely why this family exists. Under a reordering it must lose.
              let mutable rng = ConfRng.ofSeed 15206
              let mutable tally = emptyShuffleTally

              for i in 1..120 do
                  let n, r' = genRef 0 rng
                  rng <- r'
                  tally <- shuffleProbe getPropByPosition (sprintf "positional %d" i) 4 (15500 + i) (encodeRef n) tally

              Expect.isGreaterThan
                  tally.Moved
                  0
                  "the go-red run actually moved members — otherwise it proves nothing about order"

              match tally.Diffs with
              | [] ->
                  failtest
                      "a positional getProp reads whichever member came first, so a reordering MUST change its answer — this comparison cannot lose"
              | ds ->
                  Expect.isTrue
                      (ds |> List.exists (fun d -> d.Contains "MOVED under a member reordering"))
                      (sprintf "the disagreement is an answer that moved with the order — got:\n%s" (List.head ds))

                  // … and the SHUFFLE ITSELF is not what broke: every pair it drew is one the
                  // extracted relation relates, so the loss is the decoder's and not the probe's.
                  Expect.equal
                      tally.Unrelated
                      0
                      "every drawn pair is related by the extracted member_perm — the go-red measures the decoder, not a bad shuffle" ]
