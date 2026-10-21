module Fuaran.Core.Tests.ProofOracle.OpStreamLoadTests

// The `Proofs.Oracle` differential for `proofs/OpStreamLoad.fst`: the extracted model
// (`proofs/oracle/OpStreamLoad.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

// Phase 454 — the model's top-level module, aliased before `open Fuaran.Core` so that its
// `Ok` / `Error` and record names never meet production's.
module M = OpStreamLoad

open Expecto

open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges

// ---------------------------------------------------------------------------
//  Phase 454 — the OP-STREAM READERS: `OpStreamLoad` beside `OpStream.Jsonl` and the loads.
// ---------------------------------------------------------------------------
//
// `proofs/OpStreamLoad.fst` models the one JSONL line scanner, its field readers, the record
// and snapshot loads, the carrying witness and the verified load clause for clause over
// `JsonParse.fst`'s alphabet and `Chain.fst`'s records, and proves three theorems: every reader
// and load is total, every fault names a line that exists (and an undecodable op's site its op
// and hash), and loading what the checked writer wrote yields the records written. What runs
// here is the extraction beside production:
//   1. the scanner's building blocks over every line of a pool of written streams, their
//      mutations and a set of hostile lines, both sides rendered the same way;
//   2. every load over written streams — truncated, interleaved with snapshot and blank lines,
//      over-limit numerals, mutated characters — fault for fault and record for record;
//   3. the theorems' statements held to PRODUCTION over every one of those reads.
// A decoded string is compared through the host's rendering of the model's spelling (the model
// carries no code units, as `JsonParse.fst` does not); a `Refused` reason is compared as the
// text production renders for the model's structured case.

// ---- the alphabet bridges ----

let private toChs = JsonParseDiff.toChs
let private tokStr = JsonParseDiff.tokStr
let private chStr = JsonParseDiff.chStr

let private hexCh (d: int) : JsonParse.ch =
    JsonParseDiff.toCh ("0123456789abcdef".[d])

/// A production string as the DECODED string the model's writer spells: the three classes
/// `JsonString.quote` escapes are the model's escaped characters, everything else is copied.
let private toOchs (s: string) : JsonParse.och list =
    [ for c in s do
          if c = '"' then
              JsonParse.OEsc JsonParse.CQuote
          elif c = '\\' then
              JsonParse.OEsc JsonParse.CBackslash
          elif int c < 0x20 then
              JsonParse.OUni(JsonParse.CD0, JsonParse.CD0, hexCh (int c >>> 4), hexCh (int c &&& 0xF))
          else
              JsonParse.OLit(JsonParseDiff.toCh c) ]

let private hexVal (c: JsonParse.ch) : int = System.Convert.ToInt32(chStr c, 16)

/// The host's rendering of a decoded character — the code unit the model declines to compute.
let private ochStr (o: JsonParse.och) : string =
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
    | JsonParse.OUni(a, b, c, d) -> string (char ((hexVal a <<< 12) + (hexVal b <<< 8) + (hexVal c <<< 4) + hexVal d))

let private ochsStr (os: JsonParse.och list) : string =
    os |> List.map ochStr |> String.concat ""

/// `String.Trim`'s whitespace, for the characters the alphabet carries as `COther`.
let private hostWs (c: JsonParse.ch) : bool =
    match c with
    | JsonParse.COther s -> s.Length = 1 && System.Char.IsWhiteSpace s.[0]
    | _ -> false

let private mOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option =
    match o with
    | FStar_Pervasives_Native.Some v -> Some v
    | FStar_Pervasives_Native.None -> None

let private nat (n: int) : Prims.nat = bigint n

// ---- the value bridges ----

let private mActor (a: Actor) : M.actor =
    match a with
    | Human id -> M.AHuman(toOchs id)
    | Agent(model, version, id) -> M.AAgent(toOchs model, toOchs version, toOchs id)

let private mRecord (w: StreamWitness<'Op, 'S, 'R>) (r: OpRecord<'Op>) : M.lrec<'Op> =
    { M.lseq = bigint r.Seq
      M.lactor = mActor r.Actor
      M.lop = r.Op
      M.lprev = toOchs r.PrevHash
      M.lhash = toOchs r.Hash }

let private mWitness (w: StreamWitness<'Op, 'S, 'R>) : M.witness<'Op> =
    { M.enc = (fun o -> toChs (w.Encode o))
      M.dec =
        (fun raw ->
            match w.Decode(tokStr raw) with
            | Ok o -> M.Ok o
            | Error e -> M.Error e) }

/// Production's hash function, over the host's strings — the walk's `h`.
let private hashFn: HashFn = OpStream.defaultHash

/// Production's quote, the walk's `qh`: the model owns a loaded actor's shape, the host its
/// escaping, as it owns the hash.
let private quoteFn: string -> string = Jsonl.quote

/// A model actor as the production value it decodes to, rendered by `Actor.encode` — so the two
/// sides are compared through one encoder over the host's code units.
let private pActor (a: M.actor) : Actor =
    match a with
    | M.AHuman id -> Human(ochsStr id)
    | M.AAgent(model, version, id) -> Agent(ochsStr model, ochsStr version, ochsStr id)

// ---- the renderings both sides are compared through ----

let private pReason (r: JsonlFaultReason) : string =
    match r with
    | JsonlFaultReason.NotAnObject -> "NotAnObject"
    | JsonlFaultReason.Truncated -> "Truncated"
    | JsonlFaultReason.ExpectedKey -> "ExpectedKey"
    | JsonlFaultReason.ExpectedColon -> "ExpectedColon"
    | JsonlFaultReason.MissingValue -> "MissingValue"
    | JsonlFaultReason.ExpectedCommaOrBrace -> "ExpectedCommaOrBrace"
    | JsonlFaultReason.TrailingContent -> "TrailingContent"
    | JsonlFaultReason.UnterminatedString -> "UnterminatedString"
    | JsonlFaultReason.UnterminatedContainer -> "UnterminatedContainer"
    | JsonlFaultReason.InvalidEscape e -> "InvalidEscape(" + e + ")"
    | JsonlFaultReason.InvalidLiteral t -> "InvalidLiteral(" + t + ")"
    | JsonlFaultReason.ExpectedString f -> "ExpectedString(" + f + ")"
    | JsonlFaultReason.ExpectedInteger f -> "ExpectedInteger(" + f + ")"
    | JsonlFaultReason.ExpectedStringArray f -> "ExpectedStringArray(" + f + ")"
    | JsonlFaultReason.MissingField f -> "MissingField(" + f + ")"
    | JsonlFaultReason.SnapshotNotAtHead -> "SnapshotNotAtHead"
    | JsonlFaultReason.Refused why -> "Refused(" + why + ")"
    | JsonlFaultReason.ActorInvalid(f, ActorInvalid.EmptyId) -> "ActorInvalid(" + f + ",EmptyId)"
    | JsonlFaultReason.ExpectedCommaOrBracket -> "ExpectedCommaOrBracket"
    | JsonlFaultReason.RepeatedKey path -> "RepeatedKey(" + path + ")"
    | JsonlFaultReason.RawControl path -> "RawControl(" + path + ")"
    | JsonlFaultReason.LoneSurrogate path -> "LoneSurrogate(" + path + ")"
    | JsonlFaultReason.CheckpointNotAtSeqZero seq -> "CheckpointNotAtSeqZero(" + string seq + ")"
    | JsonlFaultReason.CheckpointWithoutNode -> "CheckpointWithoutNode"
    | JsonlFaultReason.CheckpointChainOnly -> "CheckpointChainOnly"
    | JsonlFaultReason.NotACheckpoint -> "NotACheckpoint"

/// A model path as production renders one (Phase 466): `$`, then each key under the host's quote
/// of its decoded spelling, or each index.
let private mPath (p: M.seg list) : string =
    "$"
    + (p
       |> List.map (fun seg ->
           match seg with
           | M.SKey k -> "[" + quoteFn (ochsStr k) + "]"
           | M.SIdx i -> "[" + string i + "]")
       |> String.concat "")

let private mReason (r: M.reason) : string =
    match r with
    | M.NotAnObject -> "NotAnObject"
    | M.Truncated -> "Truncated"
    | M.ExpectedKey -> "ExpectedKey"
    | M.ExpectedColon -> "ExpectedColon"
    | M.MissingValue -> "MissingValue"
    | M.ExpectedCommaOrBrace -> "ExpectedCommaOrBrace"
    | M.TrailingContent -> "TrailingContent"
    | M.UnterminatedString -> "UnterminatedString"
    | M.UnterminatedContainer -> "UnterminatedContainer"
    | M.InvalidEscape e -> "InvalidEscape(" + tokStr e + ")"
    | M.InvalidLiteral t -> "InvalidLiteral(" + tokStr t + ")"
    | M.ExpectedString f -> "ExpectedString(" + tokStr f + ")"
    | M.ExpectedInteger f -> "ExpectedInteger(" + tokStr f + ")"
    | M.ExpectedStringArray f -> "ExpectedStringArray(" + tokStr f + ")"
    | M.MissingField f -> "MissingField(" + tokStr f + ")"
    | M.SnapshotNotAtHead -> "SnapshotNotAtHead"
    | M.Refused why -> "Refused(" + why + ")"
    | M.RefusedNoKind -> "Refused(the actor carries no kind)"
    | M.RefusedKind k -> "Refused(unknown actor kind \"" + ochsStr k + "\")"
    | M.ActorInvalid f -> "ActorInvalid(" + tokStr f + ",EmptyId)"
    | M.ExpectedCommaOrBracket -> "ExpectedCommaOrBracket"
    | M.RepeatedKey p -> "RepeatedKey(" + mPath p + ")"
    | M.RawControl p -> "RawControl(" + mPath p + ")"
    | M.LoneSurrogate p -> "LoneSurrogate(" + mPath p + ")"

let private pFault (f: JsonlFault) : string =
    sprintf "line %d pos %d: %s" f.Line f.Position (pReason f.Reason)

let private mFault (f: M.fault) : string =
    sprintf "line %d pos %d: %s" (int f.fline) (int f.fpos) (mReason f.freason)

/// The go-red bridge: a model fault with its line number dropped.
let private dropLine (f: M.fault) : M.fault = { f with M.fline = nat 1 }

let private pRecords (w: StreamWitness<'Op, 'S, 'R>) (rs: OpRecord<'Op> list) : string =
    rs
    |> List.map (fun r -> sprintf "%d|%s|%s|%s|%s" r.Seq (Actor.encode r.Actor) (w.Encode r.Op) r.PrevHash r.Hash)
    |> String.concat "\n"

let private mRecords (w: StreamWitness<'Op, 'S, 'R>) (rs: M.lrec<'Op> list) : string =
    rs
    |> List.map (fun r ->
        sprintf
            "%s|%s|%s|%s|%s"
            (string r.lseq)
            (Actor.encode (pActor r.lactor))
            (w.Encode r.lop)
            (ochsStr r.lprev)
            (ochsStr r.lhash))
    |> String.concat "\n"

/// A chain break, rendered as the walk reports it. A sequence-number mismatch is compared without
/// its `got` text: the one field the model's unary position does not spell for a sequence number
/// outside the stream's indices (`op-stream-load-model-bridge`).
let private seqMismatch =
    ChainBreakReason.toString ChainBreakReason.SequenceMismatch

let private pBreak (b: ChainBreak) : string =
    let reason = ChainBreakReason.toString b.Reason
    sprintf "Broken %d %s %s %s" b.Index reason b.Expected (if reason = seqMismatch then "_" else b.Got)

let private mBreak (b: Chain.cbreak) : string =
    sprintf
        "Broken %d %s %s %s"
        (int (M.nat_of_pos b.cindex))
        b.creason
        b.cexpected
        (if b.creason = seqMismatch then "_" else b.cgot)

let private pLoad
    (w: StreamWitness<'Op, 'S, 'R>)
    (r: Result<OpRecord<'Op> list, StreamLoadFault<ChainBreak>>)
    : string =
    match r with
    | Ok rs -> "Ok " + pRecords w rs
    | Error(StreamLoadFault.Unreadable(lane, f)) ->
        Expect.isNone lane "a load of one text names no lane"
        "Unreadable " + pFault f
    | Error(StreamLoadFault.Broken b) -> pBreak b
    | Error(StreamLoadFault.Undecodable sites) ->
        for s in sites do
            Expect.isNone s.Lane "a load of one text names no lane"

        "Undecodable["
        + (sites
           |> List.map (fun s -> sprintf "%d %s %s" s.Line s.NodeId s.Reason)
           |> String.concat "; ")
        + "]"
    | Error other -> "other " + StreamLoadFault.toString other

let private mLoad (w: StreamWitness<'Op, 'S, 'R>) (r: M.res<M.lrec<'Op> list, M.lfault<Chain.cbreak>>) : string =
    match r with
    | M.Ok rs -> "Ok " + mRecords w rs
    | M.Error(M.LUnreadable f) -> "Unreadable " + mFault f
    | M.Error(M.LBroken b) -> mBreak b
    | M.Error(M.LUndecodable sites) ->
        "Undecodable["
        + (sites
           |> List.map (fun s -> sprintf "%d %s %s" (int s.uline) (ochsStr s.unode) s.ureason)
           |> String.concat "; ")
        + "]"

let private pSnaps
    (w: StreamWitness<'Op, 'S, 'R>)
    (r: Result<OpRecord<'Op> list * string list, StreamLoadFault<ChainBreak>>)
    : string =
    match r with
    | Ok(rs, snaps) -> pLoad w (Ok rs) + " snaps=[" + String.concat "|" snaps + "]"
    | Error e -> pLoad w (Error e)

let private mSnaps
    (w: StreamWitness<'Op, 'S, 'R>)
    (r: M.res<M.lrec<'Op> list * JsonParse.ch list list, M.lfault<Chain.cbreak>>)
    : string =
    match r with
    | M.Ok(rs, snaps) ->
        mLoad w (M.Ok rs)
        + " snaps=["
        + (snaps |> List.map tokStr |> String.concat "|")
        + "]"
    | M.Error e -> mLoad w (M.Error e)

let private mWreason (r: M.wreason) : string =
    match r with
    | M.MultiLine p -> JsonlWriteFault.reasonText (JsonlWriteFaultReason.MultiLine(int p))
    | M.NotTrimStable -> JsonlWriteFault.reasonText JsonlWriteFaultReason.NotTrimStable
    | M.Unreadable why -> "not one JSON value: " + mReason why

let private pWreason (r: JsonlWriteFaultReason) : string =
    match r with
    | JsonlWriteFaultReason.Unreadable why -> "not one JSON value: " + pReason why
    | other -> JsonlWriteFault.reasonText other

let private pWrite (r: Result<string, JsonlWriteFault>) : string =
    match r with
    | Ok text -> "Ok " + text
    | Error f -> sprintf "Error line %d member %s: %s" f.Line f.Member (pWreason f.Reason)

let private mWrite (r: M.res<JsonParse.ch list, M.wfault>) : string =
    match r with
    | M.Ok text -> "Ok " + tokStr text
    | M.Error f -> sprintf "Error line %d member %s: %s" (int f.wline) (tokStr f.wmember) (mWreason f.wreason)

let private pField (r: Result<string, JsonlFault>) : string =
    match r with
    | Ok s -> "Ok " + s
    | Error f -> "Error " + pFault f

// ---- the render pairs ----

/// The checked writer's answer: the text, or the refusal by line, member and reason.
let private writes: Differential<Result<string, JsonlWriteFault>, M.res<JsonParse.ch list, M.wfault>> =
    Differential.pair pWrite mWrite

/// `unquote`'s answer over one raw span.
let private unquoted: Differential<Result<string, JsonlFaultReason>, M.res<JsonParse.och list, M.reason>> =
    Differential.pair
        (function
        | Ok s -> "Ok " + s
        | Error r -> "Error " + pReason r)
        (function
         | M.Ok s -> "Ok " + ochsStr s
         | M.Error r -> "Error " + mReason r)

/// `checkRaw`'s answer over one raw span or line.
let private checkedRaw: Differential<Result<unit, JsonlWriteFaultReason>, M.res<unit, M.wreason>> =
    Differential.pair
        (function
        | Ok() -> "Ok"
        | Error r -> "Error " + pWreason r)
        (function
         | M.Ok() -> "Ok"
         | M.Error r -> "Error " + mWreason r)

/// A load's answer, and a load that also returns the snapshot lines. `blind` is the go-red
/// bridge: the model's unreadable fault rendered with its line number dropped.
let private loads
    (blind: bool)
    : Differential<
          Result<OpRecord<Counter.CounterOp> list, StreamLoadFault<ChainBreak>>,
          M.res<M.lrec<Counter.CounterOp> list, M.lfault<Chain.cbreak>>
       >
    =
    Differential.pair (pLoad Counter.witness) (function
        | M.Error(M.LUnreadable f) when blind -> "Unreadable " + mFault (dropLine f)
        | other -> mLoad Counter.witness other)

let private snapshotLoads
    (blind: bool)
    : Differential<
          Result<OpRecord<Counter.CounterOp> list * string list, StreamLoadFault<ChainBreak>>,
          M.res<M.lrec<Counter.CounterOp> list * JsonParse.ch list list, M.lfault<Chain.cbreak>>
       >
    =
    Differential.pair (pSnaps Counter.witness) (function
        | M.Error(M.LUnreadable f) when blind -> "Unreadable " + mFault (dropLine f)
        | other -> mSnaps Counter.witness other)

/// The tally every comparison here records into, and the class the theorem-2 statement held to
/// production records under (`faultsLocate`, production only).
let private loadTally = Tally.empty [] |> Tally.classes [ "locate" ]

// ---- generators, seeded ----

let private pick (rng: System.Random) (xs: 'a[]) : 'a = xs.[rng.Next xs.Length]

/// Strings a writer must escape, and strings it must not: quotes, backslashes, controls, a
/// line separator, raw surrogate pairs, non-ASCII, and the plain.
let private idPool: string[] =
    [| "alice"
       "b\"ob"
       "back\\slash"
       "tab\there"
       "nl\nhere"
       "cr\rhere"
       "nul\u0000x"
       "sep\u2028x"
       "émile"
       "😀"
       "x,y"
       "}"
       "a:b"
       "\u00A0"
       "" |]

let private actorPool (rng: System.Random) : Actor =
    let id =
        match pick rng [| 0; 1; 2 |] with
        | 0 -> "u" + string (rng.Next 9)
        | _ -> pick rng idPool

    let id = if id = "" then "named" else id

    if rng.Next 2 = 0 then
        Human id
    else
        Agent(pick rng [| "m"; "model\"x"; "" |], pick rng [| "1"; "2.0"; "v\\1" |], id)

/// A written stream: `append` builds it, so it verifies; `tryToJsonl` writes it.
let private writtenStream (rng: System.Random) (n: int) : OpRecord<Counter.CounterOp> list =
    let rec go i state recs =
        if i = n then
            recs
        else
            match
                OpStream.append
                    (OpStream.canonical hashFn Counter.witness)
                    (actorPool rng)
                    (Counter.Inc(rng.Next 1000))
                    state
                    recs
            with
            | Ok(state', recs') -> go (i + 1) state' recs'
            | Error e -> failwithf "append refused an Inc: %s" e

    go 0 0 []

let private writtenText (rs: OpRecord<Counter.CounterOp> list) : string =
    match Jsonl.tryWrite Counter.witness rs with
    | Ok text -> text
    | Error f -> failwithf "tryToJsonl refused a written stream: %s" (JsonlWriteFault.toString f)

/// The characters a mutation puts in a line: every structural one, the escape letter, a
/// digit, whitespace of both kinds, a letter, and a non-ASCII blank. Never `-`: a negative
/// sequence number is the dedicated case below.
let private mutationChars: char[] =
    [| '"'
       '\\'
       '{'
       '}'
       '['
       ']'
       ','
       ':'
       ' '
       '\t'
       '\n'
       '\r'
       'x'
       '0'
       'u'
       'D'
       'e'
       '.'
       '\u00A0'
       't'
       // Phase 466: a raw control and each raw surrogate half, which the strict reader refuses
       // unpaired.
       char 0x01
       char 0xD83D
       char 0xDE00 |]

/// One mutation of a text: a character replaced, dropped or inserted; the text truncated; a
/// line interleaved (blank, snapshot, a foreign object, a repeated-key record); a numeral
/// pushed over a limit; a key renamed; the line endings changed.
let private mutate (rng: System.Random) (text: string) : string =
    if text.Length = 0 then
        text
    else
        let i = rng.Next text.Length

        match rng.Next 12 with
        | 0 -> text.Substring(0, i) + string (pick rng mutationChars) + text.Substring(i + 1)
        | 1 -> text.Substring(0, i) + text.Substring(i + 1)
        | 2 -> text.Substring(0, i) + string (pick rng mutationChars) + text.Substring i
        | 3 -> text.Substring(0, i)
        | 4 -> text.Replace("\n", "\n\n")
        | 5 -> "{\"snapshot\":true}\n" + text
        | 6 -> text.Replace("\n", "\n{\"snapshot\":true}\n")
        | 7 -> text.Replace("\n", "\n{\"other\":[1,2,{\"a\":null}]}\n")
        | 8 -> text.Replace("\"seq\":", "\"seq\":\"")
        | 9 ->
            text.Replace(
                "\"seq\":",
                "\"seq\":"
                + pick rng [| "99999999999"; "2147483648"; "0"; "007"; "1e3"; "1.5" |]
                + ",\"seq\":"
            )
        | 10 -> text.Replace("\"prevHash\"", "\"prevhash\"")
        | _ -> text.Replace("\n", "\r\n")

/// Lines a record reader must refuse or admit by name: every prefix of a record line, bad
/// escapes, surrogate escapes, repeated keys, snapshot shapes, actor shapes, blank shapes.
let private hostileLines: string list =
    let full =
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"p\",\"hash\":\"h\"}"

    [ for i in 0 .. full.Length - 1 -> full.Substring(0, i) ]
    @ [ ""
        " "
        "\t \t"
        "\u00A0"
        "\u2028"
        "{}"
        " { } "
        "{} x"
        "{\"a\":1,}"
        "{,}"
        "{\"a\" 1}"
        "{\"a\":}"
        "{\"a\":,}"
        "{\"a\":1 \"b\":2}"
        "{\"a\":tru}"
        "{\"a\":nul}"
        "{\"a\":-}"
        "{\"a\":01}"
        "{\"a\":1.}"
        "{\"a\":1e}"
        "{\"a\":1E+5}"
        "{\"a\":[1,2}"
        "{\"a\":{\"b\":[}}"
        "{\"a\":\"\\q\"}"
        "{\"a\":\"\\u12\"}"
        "{\"a\":\"\\u12G4\"}"
        "{\"a\":\"\\uD83D\\uDE00\"}"
        "{\"a\":\"\\uD83Dx\"}"
        "{\"a\":\"\\uD83D\\u0041\"}"
        "{\"a\":\"\\uDE00\"}"
        "{\"a\":\"\\uD83D"
        "{\"a\":\"\\"
        "{\"a\":\"\\\"}"
        "{\"\\n\":1}"
        "{\"a\":1,\"a\":2}"
        "{\"a\":1,\"b\":2,\"a\":3}"
        // Phase 466 — the strict grammar at every depth: repeated keys by path, raw controls and
        // raw surrogate halves, pairs split between spellings, and nested container grammar.
        "{\"a\":{\"b\":1,\"b\":2}}"
        "{\"a\":[{\"b\":1},{\"b\":1,\"c\":[1,{\"d\":2,\"d\":3}]}]}"
        "{\"a\":{\"b\":1},\"b\":{\"a\":1}}"
        "{\"a\":1,\"a\":[1,,2]}"
        "{\"a\":\"x\ty\"}"
        "{\"a\":[\"x\", \"" + string (char 0x1F) + "\"]}"
        "{\"k" + string (char 0x01) + "\":1}"
        "{\"o\":{\"k" + string (char 0x01) + "\":1}}"
        "{\"a\":\"" + string (char 0xD83D) + "\"}"
        "{\"a\":\"" + string (char 0xD83D) + string (char 0xDE00) + "\"}"
        "{\"a\":\"" + string (char 0xDE00) + "\"}"
        "{\"a\":\"" + string (char 0xD83D) + "\\n\"}"
        "{\"a\":\"" + string (char 0xD83D) + "\\x\"}"
        "{\"a\":\"" + string (char 0xD83D) + string (char 0x01) + "\"}"
        "{\"a\":\"" + string (char 0xD83D) + "\\uDE00\"}"
        "{\"a\":\"" + string (char 0xD83D) + "\\u0041\"}"
        "{\"a\":\"" + string (char 0xD83D) + "\\u12\"}"
        "{\"a\":\"" + string (char 0xD83D)
        "{\"a\":\"" + string (char 0xD83D) + "\\"
        "{\"a\":\"\\uD83D" + string (char 0xDE00) + "\"}"
        "{\"a\":\"\\uD83D" + string (char 0xD83D) + "\"}"
        "{\"a\":[1 2]}"
        "{\"a\":[1,]}"
        "{\"a\":[,1]}"
        "{\"a\":[1}"
        "{\"a\":{\"b\":1]}"
        "{\"a\":{\"b\":1,}}"
        "{\"a\":{\"b\"}}"
        "{\"a\":{1:2}}"
        "{\"a\":[tru]}"
        "{\"a\":[[[[[]]]]]}"
        "{\"a\":[{\"b\":[1"
        "{\"a\":[1,2]]}"
        "{\"snapshot\":true}"
        "{\"snapshot\":true,\"op\":1}"
        "{\"op\":1,\"snapshot\":true}"
        "{\"snapshot\":\"true\"}"
        "{\"seq\":1,\"actor\":\"legacy\",\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"robot\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":1,\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"agent\",\"model\":\"m\",\"version\":\"v\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"agent\",\"model\":\"m\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":[],\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"dec\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"zip\"},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":12,\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":null,\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":12}"
        "{\"seq\":\"1\",\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":2147483647,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":2147483648,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":12345678901,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":0x1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"} "
        " {\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\"}"
        "{ \"seq\" : 1 , \"actor\" : { \"kind\" : \"human\" , \"id\" : \"a\" } , \"op\" : {\"kind\":\"inc\",\"n\":1} , \"prevHash\" : \"\" , \"hash\" : \"h\" }"
        "{\"seq\":1,\"actor\":{\"kind\":\"human\",\"id\":\"a\"},\"op\":{\"kind\":\"inc\",\"n\":1},\"prevHash\":\"\",\"hash\":\"h\",\"snapshot\":true}" ]

/// Array spans for `stringsField`.
let private arrayLines: string list =
    [ "[]"
      "[ ]"
      "[\"a\"]"
      "[\"a\",\"b\"]"
      "[ \"a\" , \"b\" ]"
      "[\"a\",]"
      "[,\"a\"]"
      "[\"a\" \"b\"]"
      "[1]"
      "[\"a\",1]"
      "[\"a]"
      "[\"\\q\"]"
      "[\"a\\u0041\"]"
      "[\"a\"\"b\"]"
      "\"a\""
      "[\"a\""
      "[]]"
      "[[\"a\"]]"
      "[\"]\"]"
      "[\"a\",,\"b\"]" ]
    |> List.map (fun v -> "{\"a\":" + v + ",\"b\":1}")

// ---- the building blocks, line by line ----

let private keys =
    [ "seq"
      "actor"
      "op"
      "prevHash"
      "hash"
      "snapshot"
      "a"
      "b"
      "kind"
      "id"
      "nope" ]

/// Every building block over one line, both sides rendered: `parseLine` / `topFields` /
/// `rawSpan`, and each field reader at each key; `unquote` and `checkRaw` over every raw span.
let private compareLine (blind: bool) (number: int) (line: string) (t: Tally) : Tally =
    let chs = toChs line
    let lineKey (k: string) = toChs k

    let mf (f: M.fault) =
        if blind then mFault (dropLine f) else mFault f

    /// A building block's answer, both sides already rendered — each reader answers in its own
    /// type, rendered by its own pair of matches below.
    let rendered (what: string) (p: string) (m: string) (t: Tally) =
        Differential.compareWith Differential.text (fun p m -> sprintf "%s: production %s, model %s" what p m) p m t

    let pLine =
        match Jsonl.parseLine number line with
        | Ok l ->
            let fields =
                match Jsonl.topFields line with
                | Ok fs -> fs |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ";"
                | Error f -> "topFields " + pFault f

            sprintf "Ok %d %s [%s]" (Jsonl.lineNumber l) (Jsonl.lineText l) fields
        | Error f -> "Error " + pFault f

    let mLine =
        match M.parse_line (nat number) chs with
        | M.Ok l ->
            let fields =
                match M.top_fields chs with
                | M.Ok fs -> fs |> List.map (fun (k, v) -> ochsStr k + "=" + tokStr v) |> String.concat ";"
                | M.Error f -> "topFields " + mf f

            sprintf "Ok %d %s [%s]" (int (M.line_number l)) (tokStr (M.line_text l)) fields
        | M.Error f -> "Error " + mf f

    let t =
        t
        |> rendered (sprintf "parseLine %A" line) pLine mLine
        |> Differential.over keys (fun k t ->
            let pSpan =
                match Jsonl.rawSpan k line with
                | Ok(Some v) -> "Some " + v
                | Ok None -> "None"
                | Error f -> "Error " + pFault f

            let mSpan =
                match M.raw_span (lineKey k) chs with
                | M.Ok o ->
                    match mOpt o with
                    | Some v -> "Some " + tokStr v
                    | None -> "None"
                | M.Error f -> "Error " + mf f

            rendered (sprintf "rawSpan %s %A" k line) pSpan mSpan t)

    let t =
        match Jsonl.parseLine number line, M.parse_line (nat number) chs with
        | Ok pl, M.Ok ml ->
            let t =
                t
                |> Differential.over keys (fun k t ->
                    let mk = lineKey k

                    let pairs =
                        [ "tryRawField",
                          (match Jsonl.tryRawField k pl with
                           | Some v -> "Some " + v
                           | None -> "None"),
                          (match mOpt (M.try_raw_field mk ml) with
                           | Some v -> "Some " + tokStr v
                           | None -> "None")
                          "rawField",
                          (match Jsonl.rawField k pl with
                           | Ok v -> "Ok " + v
                           | Error f -> "Error " + pFault f),
                          (match M.raw_field mk ml with
                           | M.Ok v -> "Ok " + tokStr v
                           | M.Error f -> "Error " + mf f)
                          "stringField",
                          pField (Jsonl.stringField k pl),
                          (match M.string_field mk ml with
                           | M.Ok s -> "Ok " + ochsStr s
                           | M.Error f -> "Error " + mf f)
                          "intField",
                          (match Jsonl.intField k pl with
                           | Ok i -> "Ok " + string i
                           | Error f -> "Error " + pFault f),
                          (match M.int_field mk ml with
                           | M.Ok i -> "Ok " + string i
                           | M.Error f -> "Error " + mf f)
                          "stringsField",
                          (match Jsonl.stringsField k pl with
                           | Ok xs -> "Ok [" + String.concat "|" xs + "]"
                           | Error f -> "Error " + pFault f),
                          (match M.strings_field mk ml with
                           | M.Ok xs -> "Ok [" + (xs |> List.map ochsStr |> String.concat "|") + "]"
                           | M.Error f -> "Error " + mf f)
                          "actorField",
                          (match Jsonl.actorField k pl with
                           | Ok a -> "Ok " + Actor.encode a
                           | Error f -> "Error " + pFault f),
                          (match M.actor_field mk ml with
                           | M.Ok a -> "Ok " + Actor.encode (pActor a)
                           | M.Error f -> "Error " + mf f) ]

                    Differential.over pairs (fun (name, p, m) t -> rendered (sprintf "%s %s %A" name k line) p m t) t)
                |> rendered (sprintf "refuse %A" line) (pFault (Jsonl.refuse pl "why")) (mf (M.refuse ml "why"))

            match Jsonl.topFields line with
            | Ok fs ->
                t
                |> Differential.over fs (fun (_, v) t ->
                    t
                    |> Differential.compare unquoted (sprintf "unquote %A" v) (Jsonl.unquote v) (M.unquote (toChs v))
                    |> Differential.compareWith
                        checkedRaw
                        (fun p m -> sprintf "checkRaw %A: production %s, model %s" v p m)
                        (Jsonl.checkRaw v)
                        (M.check_raw (toChs v)))
            | Error _ -> t
        | _ -> t

    t
    |> Differential.compareWith
        checkedRaw
        (fun p m -> sprintf "checkRaw %A: production %s, model %s" line p m)
        (Jsonl.checkRaw line)
        (M.check_raw chs)

/// Every load over one text, both sides rendered.
let private compareLoads (blind: bool) (text: string) (t: Tally) : Tally =
    let w = Counter.witness
    let mw = mWitness w
    let chs = toChs text

    let message (name: string) (p: string) (m: string) =
        sprintf "%s over %A: production %s, model %s" name text p m

    t
    |> Differential.compareWith (loads blind) (message "fromJsonl") (Jsonl.read w text) (M.from_jsonl hostWs mw chs)
    |> Differential.compareWith
        (snapshotLoads blind)
        (message "fromJsonlWithSnapshots")
        (Jsonl.readWithSnapshots w text)
        (M.from_jsonl_with_snapshots hostWs mw chs)
    |> Differential.compareWith
        (loads blind)
        (message "fromJsonlLegacyActor")
        (Jsonl.readLegacyActor w text)
        (M.from_jsonl_legacy_actor hostWs mw chs)
    |> Differential.compareWith
        (loads blind)
        (message "fromJsonlVerified")
        (Jsonl.readVerified (OpStream.canonical hashFn w) text)
        (M.from_jsonl_verified hashFn quoteFn ochsStr hostWs mw chs)

/// The lines of a text as `scanRecords` counts them.
let private linesOf (text: string) : string[] = text.Replace("\r\n", "\n").Split '\n'

/// `OpStreamLoad.fst`'s THEOREM 2 held to production: a fault names a line that exists, is not blank, and is that
/// line's own fault; a site names a line whose record the witness refused, by its hash. Each
/// breach is a `locate` finding.
let private faultsLocate (text: string) (t: Tally) : Tally =
    let w = Counter.witness
    let lines = linesOf text
    let found (message: string) (t: Tally) = Tally.record "locate" message t

    let checkFault (f: JsonlFault) (t: Tally) =
        if f.Line < 1 || f.Line > lines.Length then
            found (sprintf "fault %s names line %d of %d" (pFault f) f.Line lines.Length) t
        else
            let l = lines.[f.Line - 1]

            t
            |> (if l.Trim() = "" then
                    found (sprintf "fault %s names a blank line" (pFault f))
                else
                    id)
            |> (if f.Position > l.Length then
                    found (sprintf "fault %s sits past its line (%d chars)" (pFault f) l.Length)
                else
                    id)
            |> (match Jsonl.parseLine f.Line l with
                | Error g when g <> f ->
                    found (sprintf "fault %s is not the line's parse fault %s" (pFault f) (pFault g))
                | _ -> id)

    let checkSite (s: UndecodableOp) (t: Tally) =
        if s.Line < 1 || s.Line > lines.Length then
            found (sprintf "site at line %d of %d" s.Line lines.Length) t
        else
            match Jsonl.parseLine s.Line lines.[s.Line - 1] with
            | Error g -> found (sprintf "site at line %d, which does not parse: %s" s.Line (pFault g)) t
            | Ok l ->
                t
                |> (match Jsonl.rawField "op" l with
                    | Ok raw ->
                        match w.Decode raw with
                        | Error why when why = s.Reason -> id
                        | other ->
                            found (sprintf "site at line %d: the witness answers %A, not %s" s.Line other s.Reason)
                    | Error g -> found (sprintf "site at line %d has no op: %s" s.Line (pFault g)))
                |> (match Jsonl.stringField "hash" l with
                    | Ok h when h = s.NodeId -> id
                    | other ->
                        found (sprintf "site at line %d names node %s, the line's hash is %A" s.Line s.NodeId other))

    t
    |> Differential.over
        [ Jsonl.read w text
          Jsonl.readLegacyActor w text
          Jsonl.readVerified (OpStream.canonical hashFn w) text
          Jsonl.readWithSnapshots w text |> Result.map fst ]
        (fun r t ->
            match r with
            | Error(StreamLoadFault.Unreadable(_, f)) -> checkFault f t
            | Error(StreamLoadFault.Undecodable sites) -> Differential.over sites checkSite t
            | _ -> t)

let private pool
    (seed: int)
    (streams: int)
    (mutations: int)
    : (OpRecord<Counter.CounterOp> list * string * string list) list =
    let rng = System.Random seed

    [ for _ in 1..streams do
          let rs = writtenStream rng (rng.Next 5)
          let text = writtenText rs
          let muts = [ for _ in 1..mutations -> mutate rng (mutate rng text) ]
          yield rs, text, muts ]

/// The hostile texts: every hostile line alone, and spliced after a written record.
let private hostileTexts (written: string) : string list =
    hostileLines
    @ (hostileLines |> List.map (fun l -> written + "\n" + l))
    @ arrayLines

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase
              "the op-stream-load oracle agrees with the scanner's building blocks over generated and hostile lines"
          <| fun _ ->
              let p = pool 454 30 6
              let written = p |> List.map (fun (_, t, _) -> t)

              let texts =
                  written
                  @ (p |> List.collect (fun (_, _, ms) -> ms))
                  @ hostileTexts (List.head written)

              let lines =
                  texts |> List.collect (fun t -> linesOf t |> List.ofArray) |> List.distinct

              Expect.isGreaterThan lines.Length 300 "the line pool is wide"

              lines
              |> List.indexed
              |> fun ls -> Differential.over ls (fun (i, l) -> compareLine false (i % 7 + 1) l) loadTally
              |> Differential.expectAgrees "the scanner's building blocks"

          testCase "the op-stream-load oracle agrees with every load over written streams and their mutations"
          <| fun _ ->
              let p = pool 455 40 8
              let written = p |> List.map (fun (_, t, _) -> t)

              let texts =
                  written
                  @ (p |> List.collect (fun (_, _, ms) -> ms))
                  @ hostileTexts (List.head written)

              Differential.over texts (compareLoads false) loadTally
              |> Differential.expectAgrees "the loads"

              // The checked writer, both sides, over the written records and over records that
              // carry an op encoding the writer must refuse.
              let w = Counter.witness

              loadTally
              |> Differential.over p (fun (rs, _, _) t ->
                  t
                  |> Differential.compare
                      writes
                      "tryToJsonl agrees"
                      (Jsonl.tryWrite w rs)
                      (M.try_to_jsonl (mWitness w) (rs |> List.map (mRecord w)))
                  |> Differential.compare
                      Differential.text
                      "toJsonl agrees"
                      (Jsonl.write w rs)
                      (tokStr (M.to_jsonl (mWitness w) (rs |> List.map (mRecord w)))))
              |> Differential.expectAgrees "the checked writer"

              let multiLine: StreamWitness<Counter.CounterOp, int, string> =
                  { w with
                      Encode = (fun o -> w.Encode o + "\n") }

              let spaced: StreamWitness<Counter.CounterOp, int, string> =
                  { w with
                      Encode = (fun o -> " " + w.Encode o) }

              let two: StreamWitness<Counter.CounterOp, int, string> =
                  { w with
                      Encode = (fun o -> w.Encode o + " 1") }

              let rs = (p |> List.head |> (fun (rs, _, _) -> rs))

              loadTally
              |> Differential.over [ multiLine; spaced; two ] (fun bad t ->
                  Differential.compare
                      writes
                      "tryToJsonl refuses an embedding both sides alike"
                      (Jsonl.tryWrite bad rs)
                      (M.try_to_jsonl (mWitness bad) (rs |> List.map (mRecord bad)))
                      t)
              |> Differential.expectAgrees "the checked writer over a bad embedding"

              // Phase 473: the strings the writer quotes itself are checked too, by member, in line
              // order — an unpaired surrogate in the actor, prevHash or hash is refused alike.
              let lone = string (char 0xD83D)

              match p |> List.head |> (fun (rs, _, _) -> rs) with
              | r :: _ ->
                  loadTally
                  |> Differential.over
                      [ { r with Actor = Human("a" + lone) }
                        { r with PrevHash = lone }
                        { r with Hash = "h" + lone }
                        { r with
                            Actor = Human lone
                            Hash = lone } ]
                      (fun bad t ->
                          let pW = Jsonl.tryWrite w [ bad ]
                          Expect.stringStarts (pWrite pW) "Error" "production refuses a quoted unpaired surrogate"

                          Differential.compare
                              writes
                              "tryToJsonl refuses a quoted string both sides alike"
                              pW
                              (M.try_to_jsonl (mWitness w) [ mRecord w bad ])
                              t)
                  |> Differential.expectAgrees "the checked writer over a quoted unpaired surrogate"
              | [] -> failtest "no written record to edit"

              // A negative sequence number: the scan admits it, the walk refuses it. The one
              // field the model's unary position cannot spell is the break's `got` text.
              let neg = List.head written |> fun t -> t.Replace("\"seq\":0,", "\"seq\":-1,")

              loadTally
              |> Differential.compare
                  (loads false)
                  "an unverified load admits a negative sequence number on both sides"
                  (Jsonl.read w neg)
                  (M.from_jsonl hostWs (mWitness w) (toChs neg))
              |> Differential.compare
                  (loads false)
                  "the verified load refuses it at the same index for the same reason"
                  (Jsonl.readVerified (OpStream.canonical hashFn w) neg)
                  (M.from_jsonl_verified hashFn quoteFn ochsStr hostWs (mWitness w) (toChs neg))
              |> Differential.expectAgrees "a negative sequence number"

              match Jsonl.readVerified (OpStream.canonical hashFn w) neg with
              | Error(StreamLoadFault.Broken b) ->
                  Expect.equal b.Reason ChainBreakReason.SequenceMismatch "a sequence-number mismatch"
                  Expect.equal b.Got "-1" "production spells the number it read"
              | other -> failtestf "the verified load answered %A" other

          testCase "every production fault names a line that exists and every undecodable site its op; writes load back"
          <| fun _ ->
              let p = pool 456 40 8
              let w = Counter.witness
              let written = p |> List.map (fun (_, t, _) -> t)

              let texts =
                  written
                  @ (p |> List.collect (fun (_, _, ms) -> ms))
                  @ hostileTexts (List.head written)

              Differential.over texts faultsLocate loadTally
              |> Differential.expectAgrees "theorem 2 held to production"

              let faults =
                  texts
                  |> List.filter (fun t ->
                      match Jsonl.read w t with
                      | Error(StreamLoadFault.Unreadable _) -> true
                      | _ -> false)

              let sites =
                  texts
                  |> List.filter (fun t ->
                      match Jsonl.read w t with
                      | Error(StreamLoadFault.Undecodable _) -> true
                      | _ -> false)

              Expect.isGreaterThan faults.Length 100 "the pool holds many unreadable texts"
              Expect.isGreaterThan sites.Length 5 "the pool holds undecodable texts"

              // `OpStreamLoad.fst`'s THEOREM 3 held to production: what the checked writer wrote loads back, verified
              // or not, with no snapshot.
              for rs, text, _ in p do
                  Expect.equal (Jsonl.read w text) (Ok rs) "fromJsonl reads the written records"

                  Expect.equal
                      (Jsonl.readWithSnapshots w text)
                      (Ok(rs, []))
                      "fromJsonlWithSnapshots reads them, no snapshot"

                  Expect.equal
                      (Jsonl.readVerified (OpStream.canonical hashFn w) text)
                      (Ok rs)
                      "fromJsonlVerified reads them: an appended chain verifies"

          testCase "a model that drops a fault's line number loses - on exactly the faults past the first line"
          <| fun _ ->
              // The go-red: the BLIND bridge renumbers every model fault to line 1 — a scanner that
              // dropped the line's number — which loses on exactly the faults below the first line.
              let p = pool 457 30 6
              let w = Counter.witness
              let written = p |> List.map (fun (_, t, _) -> t)

              let texts =
                  written
                  @ (p |> List.collect (fun (_, _, ms) -> ms))
                  @ hostileTexts (List.head written)

              let unreadablePast (r: Result<'a, StreamLoadFault<ChainBreak>>) =
                  match r with
                  | Error(StreamLoadFault.Unreadable(_, f)) -> f.Line > 1
                  | _ -> false

              let pastFirst =
                  texts
                  |> List.filter (fun t ->
                      unreadablePast (Jsonl.read w t)
                      || unreadablePast (Jsonl.readWithSnapshots w t)
                      || unreadablePast (Jsonl.readLegacyActor w t)
                      || unreadablePast (Jsonl.readVerified (OpStream.canonical hashFn w) t))

              let blindLosses =
                  texts
                  |> List.filter (fun t -> not (List.isEmpty (Tally.disagreements (compareLoads true t loadTally))))

              Expect.isGreaterThan pastFirst.Length 50 "the pool holds faults past the first line"

              Expect.isGreaterThan (texts.Length - pastFirst.Length) 50 "and texts the blind bridge cannot tell apart"

              let only (xs: string list) (ys: string list) =
                  xs |> List.filter (fun x -> not (List.contains x ys)) |> List.truncate 3

              Expect.equal
                  (List.sort blindLosses, [], [])
                  (List.sort pastFirst, only blindLosses pastFirst, only pastFirst blindLosses)
                  "the blind comparison loses on exactly the texts a load refuses past line 1"

          testCase "an escaped key spelling is the model's boundary: production reads it, the model does not"
          <| fun _ ->
              // The model identifies a character with its spelling (JsonParse's `och`), so a key
              // spelled with an escape is another key there. Recorded as the bridge's boundary, and
              // pinned so that a model that starts decoding escapes is noticed here.
              let w = Counter.witness

              let text =
                  (List.head (pool 458 1 0) |> fun (_, t, _) -> t).Replace("\"seq\"", "\"s\\u0065q\"")

              Expect.isOk (Jsonl.read w text) "production reads the escaped key as `seq`"

              match M.from_jsonl hostWs (mWitness w) (toChs text) with
              | M.Error(M.LUnreadable f) ->
                  Expect.equal (mReason f.freason) "MissingField(seq)" "the model misses the member"
              | other -> failtestf "the model answered %s" (mLoad w other) ]
