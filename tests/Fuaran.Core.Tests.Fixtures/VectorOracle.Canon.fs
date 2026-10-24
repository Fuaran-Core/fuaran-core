module Fuaran.Core.Tests.CanonOracle

// ============================================================================
//  Phase 563 — the canonical encoder's model (`proofs/WireCanon.fst`) as the exporters ask it: the
//  character and value bridges every model over `WireCanon.jval` reads (the column codec, the escape
//  table, the canon differentials). Moved from the test project's `ProofOracle.Bridges.fs`, so the
//  differentials and the exporters read one copy.
// ============================================================================

open Fuaran.Core

/// The model's hex nibble, by value. Used in both directions of the bridge.
let canonHexdOf (n: int) : WireCanon.hexd =
    match n with
    | 0 -> WireCanon.HD0
    | 1 -> WireCanon.HD1
    | 2 -> WireCanon.HD2
    | 3 -> WireCanon.HD3
    | 4 -> WireCanon.HD4
    | 5 -> WireCanon.HD5
    | 6 -> WireCanon.HD6
    | 7 -> WireCanon.HD7
    | 8 -> WireCanon.HD8
    | 9 -> WireCanon.HD9
    | 10 -> WireCanon.HDa
    | 11 -> WireCanon.HDb
    | 12 -> WireCanon.HDc
    | 13 -> WireCanon.HDd
    | 14 -> WireCanon.HDe
    | 15 -> WireCanon.HDf
    | _ -> failwithf "not a hex nibble: %d" n

let canonHexdChar (d: WireCanon.hexd) : char =
    match d with
    | WireCanon.HD0 -> '0'
    | WireCanon.HD1 -> '1'
    | WireCanon.HD2 -> '2'
    | WireCanon.HD3 -> '3'
    | WireCanon.HD4 -> '4'
    | WireCanon.HD5 -> '5'
    | WireCanon.HD6 -> '6'
    | WireCanon.HD7 -> '7'
    | WireCanon.HD8 -> '8'
    | WireCanon.HD9 -> '9'
    | WireCanon.HDa -> 'a'
    | WireCanon.HDb -> 'b'
    | WireCanon.HDc -> 'c'
    | WireCanon.HDd -> 'd'
    | WireCanon.HDe -> 'e'
    | WireCanon.HDf -> 'f'

/// A .NET `char` as one of the model's constructors. TOTAL and CLASSIFYING: every character the
/// canonical encoder distinguishes has its own constructor, and `CPlain` catches the rest
/// VERBATIM — so two different ordinary characters are never identified, and `WireCanon.bridged`
/// (the model's own statement of what this function has to be: a `CPlain` never carries a
/// spelling another constructor already denotes) holds of everything produced here by
/// construction. That is the level-3 assumption, written where it is discharged.
let canonToCh (c: char) : WireCanon.ch =
    match c with
    | '"' -> WireCanon.CQuote
    | '\\' -> WireCanon.CBackslash
    | '{' -> WireCanon.CLBrace
    | '}' -> WireCanon.CRBrace
    | '[' -> WireCanon.CLBrack
    | ']' -> WireCanon.CRBrack
    | ':' -> WireCanon.CColon
    | ',' -> WireCanon.CComma
    | '-' -> WireCanon.CMinus
    | '+' -> WireCanon.CPlus
    | '.' -> WireCanon.CDot
    | 'E' -> WireCanon.CUpE
    | 'u' -> WireCanon.CLu
    | c when c < ' ' -> WireCanon.CCtrl(int c >= 16, canonHexdOf (int c % 16))
    | c when c >= '0' && c <= '9' -> WireCanon.CHexCh(canonHexdOf (int c - int '0'))
    | c when c >= 'a' && c <= 'f' -> WireCanon.CHexCh(canonHexdOf (int c - int 'a' + 10))
    | c -> WireCanon.CPlain(string c)

let canonFromCh (c: WireCanon.ch) : string =
    match c with
    | WireCanon.CQuote -> "\""
    | WireCanon.CBackslash -> "\\"
    | WireCanon.CLBrace -> "{"
    | WireCanon.CRBrace -> "}"
    | WireCanon.CLBrack -> "["
    | WireCanon.CRBrack -> "]"
    | WireCanon.CColon -> ":"
    | WireCanon.CComma -> ","
    | WireCanon.CMinus -> "-"
    | WireCanon.CPlus -> "+"
    | WireCanon.CDot -> "."
    | WireCanon.CUpE -> "E"
    | WireCanon.CLu -> "u"
    | WireCanon.CHexCh d -> string (canonHexdChar d)
    | WireCanon.CCtrl(hi, lo) ->
        string (
            char (
                (if hi then 16 else 0)
                + int (
                    canonHexdChar lo
                    |> fun ch ->
                        if ch <= '9' then
                            int ch - int '0'
                        else
                            int ch - int 'a' + 10
                )
            )
        )
    | WireCanon.CPlain s -> s

let canonToChs (s: string) : WireCanon.ch list = s |> Seq.map canonToCh |> List.ofSeq

let canonFromChs (l: WireCanon.ch list) : string =
    l |> List.map canonFromCh |> String.concat ""

let rec canonToModel (v: JVal) : WireCanon.jval<int, float> =
    match v with
    | JStr s -> WireCanon.JStr(canonToChs s)
    | JInt i -> WireCanon.JInt i
    | JBool b -> WireCanon.JBool b
    | JFloat f -> WireCanon.JFloat f
    | JArr xs -> WireCanon.JArr(xs |> List.map canonToModel)
    | JObj fs -> WireCanon.JObj(fs |> List.map (fun (k, v) -> (canonToChs k, canonToModel v)))

let rec canonOfModel (v: WireCanon.jval<int, float>) : JVal =
    match v with
    | WireCanon.JStr s -> JStr(canonFromChs s)
    | WireCanon.JInt i -> JInt i
    | WireCanon.JBool b -> JBool b
    | WireCanon.JFloat f -> JFloat f
    | WireCanon.JArr xs -> JArr(xs |> List.map canonOfModel)
    | WireCanon.JObj fs -> JObj(fs |> List.map (fun (k, v) -> (canonFromChs k, canonOfModel v)))
