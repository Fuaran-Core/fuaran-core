namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// Phase 539 (DECISIONS.md D190) — the generated F# side of the canonical-form declarations: the
/// host-side test of a presence condition the encoder wraps a member's piece in, and the decoder's
/// reading of a member under its aliases and its presence condition (`dSpelt` and `dMember` over the
/// raw object, as [[WireForm.readMember]] reads it in the interpreter). Read by [[FSharpCodec]].
///
/// What reads [[WireForm]] — internal to `Fuaran.Core.Idl` — is [[Decl.memberReading]]'s, behind
/// `NoInlining` (D129).
module internal FSharpCanonicalForm =

    /// Phase 539 — the host-side test of a presence condition: `srcOf` names the expression each
    /// sibling of `fields` is read from. A presence test reads an `Optional` sibling's option; an
    /// equality test compares the sibling's host value with the scalar's literal (wrapped in `Some`
    /// for an `Optional` sibling) — scalars and enum cases carry equality, which is why
    /// [[Declare.errors]] admits nothing else there. Phase 660 — a non-empty test reads the guarded
    /// member `self`, an `Optional` list, by pattern, so it needs no equality at the element type and
    /// `Some []` fails it as `None` does: the member is then written as absent.
    let presenceTest
        (idl: Idl)
        (fields: IdlField list)
        (srcOf: string -> string)
        (self: IdlField)
        (c: PresenceCondition)
        : Result<string, CodegenError> =
        match c with
        | WhenPresent s -> Ok(sprintf "Option.isSome (%s)" (srcOf s))
        | WhenAbsent s -> Ok(sprintf "Option.isNone (%s)" (srcOf s))
        | WhenNonEmpty -> Ok(sprintf "(match %s with | Some(_ :: _) -> true | _ -> false)" (srcOf self.Name))
        | WhenEquals(s, v) ->
            match fields |> List.tryFind (fun g -> g.Name = s) with
            | Some g ->
                fsDefaultLit idl g.Type v
                |> Result.map (fun l ->
                    match g.Opt with
                    | Optional -> sprintf "(%s) = Some(%s)" (srcOf s) l
                    | _ -> sprintf "(%s) = %s" (srcOf s) l)
            | None -> Error(CodegenError.UnsupportedDefault(TStr, v))

    /// Phase 539 — the generated reading of a member a vocabulary declares aliases or a presence
    /// condition for ([[Decl.memberReading]], the one reading every decoder prints), over the object under
    /// its spellings and with the condition's test over the RAW members, as [[WireForm.readMember]] reads
    /// it: the KEY the member is read under and the VIEW of the object it is read from (see
    /// [[canonicalFormHelpers]]). Phase 723 (DECISIONS.md D288) — the key is the spelling the object
    /// carries (`dSpelt`), so a refusal inside the member is located as written, and an absent member's is
    /// its canonical spelling. `None` for a member that declares neither, which reads the object as it
    /// always has.
    let memberRead (idl: Idl) (fields: IdlField list) (f: IdlField) : (string * string) option =
        let names (spellings: string list) =
            "[ " + (spellings |> List.map SourceLit.fsString |> String.concat "; ") + " ]"

        let found (spellings: string list) =
            sprintf "dFound %s __o" (names spellings)

        Decl.memberReading idl fields f
        |> Option.map (fun r ->
            let holds =
                match r.Presence with
                | PresenceReading.Unconditional -> "(fun _ -> true)"
                | PresenceReading.SiblingPresent s -> sprintf "(fun __o -> Option.isSome (%s))" (found s)
                | PresenceReading.SiblingAbsent s -> sprintf "(fun __o -> Option.isNone (%s))" (found s)
                | PresenceReading.SiblingEquals(s, values, absent) ->
                    sprintf
                        "(fun __o -> match %s with | Some(_, __v) -> List.contains __v [ %s ] | None -> %s)"
                        (found s)
                        (values |> List.map FSharpDefaults.fsJValLit |> String.concat "; ")
                        (if absent then "true" else "false")
                // Phase 660 — the member itself: an explicit EMPTY array is its absence, and anything
                // else (a member of the wrong kind included) reaches its decoder.
                | PresenceReading.SelfNonEmpty ->
                    sprintf "(fun __o -> match %s with | Some(_, JArr []) -> false | _ -> true)" (found r.Spellings)

            sprintf "(dSpelt %s %s __fs)" (names r.Spellings) holds,
            sprintf "(dMember %s %s __fs)" (names r.Spellings) holds)

    /// Whether any member of a field list reads through [[memberRead]].
    let declaresCanonicalForm (fields: IdlField list) : bool =
        fields
        |> List.exists (fun f -> not (List.isEmpty f.Aliases) || f.Presence.IsSome)

    /// Whether any field list of the vocabulary declares an alias or a presence condition.
    let declaresCanonicalFormAnywhere (idl: Idl) : bool =
        Decl.fieldLists idl |> List.exists declaresCanonicalForm

    /// The decode helpers [[memberRead]] calls (Phase 539), emitted only when the vocabulary declares
    /// an alias or a presence condition, so every other module is byte-for-byte what it was.
    let canonicalFormHelpers =
        """// Phase 539 — a member's canonical reading. `dFound` finds a member under its spellings, the
// canonical one first, then each alias, with the spelling it is found under; a presence test reads
// it too. Phase 723 — `dSpelt` is the key the member is read under: the spelling the object carries
// when its presence condition holds over the object, so a refusal inside it is located as written,
// and otherwise the canonical one, at which an absent member is missing. `dMember` keeps the member
// under that key and drops it when the condition fails, so it reaches the presence rules below
// exactly as the canonical form has it. Every other spelling is removed. None of these is named as
// a presence-keyed union's helpers are (`dPick`, `dSynth`), so one module may take both sets.
let private dFound (names: string list) (fs: (string * JVal) list) : (string * JVal) option =
    names |> List.tryPick (fun n -> fs |> List.tryPick (fun (k, v) -> if k = n then Some(k, v) else None))

let private dSpelt (names: string list) (holds: (string * JVal) list -> bool) (fs: (string * JVal) list) : string =
    match dFound names fs with
    | Some(k, _) when holds fs -> k
    | _ -> List.head names

let private dMember (names: string list) (holds: (string * JVal) list -> bool) (fs: (string * JVal) list) : (string * JVal) list =
    let rest = fs |> List.filter (fun (k, _) -> not (List.contains k names))

    match dFound names fs with
    | Some(k, v) when holds fs -> (k, v) :: rest
    | _ -> rest"""
