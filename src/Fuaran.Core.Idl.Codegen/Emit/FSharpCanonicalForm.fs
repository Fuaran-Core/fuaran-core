namespace Fuaran.Core.Idl.Emit

open System.Runtime.CompilerServices
open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FSharpDefaults

/// Phase 539 (DECISIONS.md D190) — the generated F# side of the canonical-form declarations: the
/// host-side test of a presence condition the encoder wraps a member's piece in, and the decoder's
/// reading of a member under its aliases and its presence condition (`dMember` over the raw object,
/// as [[WireForm.readMember]] reads it in the interpreter). Read by [[FSharpCodec]].
///
/// The functions that read [[WireForm]] — internal to `Fuaran.Core.Idl` — are `NoInlining` (D129):
/// a body that can be inlined into another assembly must not reach another assembly's internals.
module internal FSharpCanonicalForm =

    /// Phase 539 — the host-side test of a presence condition: `srcOf` names the expression each
    /// sibling of `fields` is read from. A presence test reads an `Optional` sibling's option; an
    /// equality test compares the sibling's host value with the scalar's literal (wrapped in `Some`
    /// for an `Optional` sibling) — scalars and enum cases carry equality, which is why
    /// [[Declare.errors]] admits nothing else there.
    let presenceTest
        (idl: Idl)
        (fields: IdlField list)
        (srcOf: string -> string)
        (c: PresenceCondition)
        : Result<string, CodegenError> =
        match c with
        | WhenPresent s -> Ok(sprintf "Option.isSome (%s)" (srcOf s))
        | WhenAbsent s -> Ok(sprintf "Option.isNone (%s)" (srcOf s))
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
    /// condition for: `dMember` (see [[canonicalFormHelpers]]) over the object, under its spellings
    /// and with the condition's test over the RAW members, as [[WireForm.readMember]] reads it. `None`
    /// for a member that declares neither, which reads the object as it always has.
    /// A member's spellings ([[WireForm.spellings]]), read where no closure can carry the internal
    /// reference into another assembly (D129).
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let private spellingsOf (f: IdlField) : string list = WireForm.spellings f

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    let memberView (idl: Idl) (fields: IdlField list) (f: IdlField) : string option =
        let names (g: IdlField) =
            "[ "
            + (spellingsOf g |> List.map SourceLit.fsString |> String.concat "; ")
            + " ]"

        let sibling (name: string) =
            fields |> List.tryFind (fun g -> g.Name = name)

        let pick (name: string) =
            match sibling name with
            | Some g -> sprintf "dPick %s __o" (names g)
            | None -> sprintf "dPick [ %s ] __o" (SourceLit.fsString name)

        let holds =
            match f.Presence with
            | None -> "(fun _ -> true)"
            | Some(WhenPresent s) -> sprintf "(fun __o -> Option.isSome (%s))" (pick s)
            | Some(WhenAbsent s) -> sprintf "(fun __o -> Option.isNone (%s))" (pick s)
            | Some(WhenEquals(s, v)) ->
                let spellings, absent =
                    match sibling s with
                    | Some g ->
                        WireForm.scalarSpellings idl g.Type v,
                        (match g.Opt with
                         | OmitDefault d -> d = v
                         | _ -> false)
                    | None -> [], false

                sprintf
                    "(fun __o -> match %s with | Some __v -> List.contains __v [ %s ] | None -> %s)"
                    (pick s)
                    (spellings |> List.map FSharpDefaults.fsJValLit |> String.concat "; ")
                    (if absent then "true" else "false")

        if List.isEmpty f.Aliases && f.Presence.IsNone then
            None
        else
            Some(sprintf "(dMember %s %s __fs)" (names f) holds)

    /// Whether any member of a field list reads through [[memberView]].
    let declaresCanonicalForm (fields: IdlField list) : bool =
        fields
        |> List.exists (fun f -> not (List.isEmpty f.Aliases) || f.Presence.IsSome)

    /// Whether any field list of the vocabulary declares an alias or a presence condition.
    let declaresCanonicalFormAnywhere (idl: Idl) : bool =
        Decl.fieldOwners idl |> List.exists (snd >> declaresCanonicalForm)

    /// The decode helpers [[memberView]] calls (Phase 539), emitted only when the vocabulary declares
    /// an alias or a presence condition, so every other module is byte-for-byte what it was.
    let canonicalFormHelpers =
        """// Phase 539 — a member's canonical reading. `dPick` reads a member under its spellings, the
// canonical one first, then each alias; `dMember` keys it by its canonical spelling and drops it
// when its presence condition fails over the object, so the member reaches the presence rules
// below exactly as the canonical form has it. Every spelling it was read under is removed.
let private dPick (names: string list) (fs: (string * JVal) list) : JVal option =
    names |> List.tryPick (fun n -> fs |> List.tryPick (fun (k, v) -> if k = n then Some v else None))

let private dMember (names: string list) (holds: (string * JVal) list -> bool) (fs: (string * JVal) list) : (string * JVal) list =
    let rest = fs |> List.filter (fun (k, _) -> not (List.contains k names))

    match dPick names fs with
    | Some v when holds fs -> (List.head names, v) :: rest
    | _ -> rest"""
