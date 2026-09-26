module Fuaran.Core.Tests.IdlFStarTargetTests

open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 150 — the F* PROOF-MODEL target, and the GENERATION DIFF that holds each committed
// `proofs/<M>.fst` model to a fresh generation from the vocabulary it is generated from.
//
// The diff is the same discipline the proof leg already applies to the extracted oracle —
// `proofs/oracle/<M>.fs` must be byte-identical to a fresh extraction of `<M>.fst`, or the
// oracle the suite runs is not the model the theorem is about. Here the pair is one step
// further up: a committed model must be byte-identical to a fresh generation from its
// vocabulary, or the vocabulary the theorem is about is not the one this repository
// certifies the backend on. A vocabulary that moves without a regeneration is VOCABULARY
// DRIFT and this is where it is caught.
//
// Phase 173 — WHICH vocabulary, and why it is no longer the pinned corpus's `idl.json`.
// Until 173 the one generated model was the UI vocabulary's, read from the shared corpus:
// a domain's proof running in the substrate's CI, which D14 (and Phase 114, completing it
// for the F# backend) had already ruled out for `tests/`. The generation source is now the
// CERTIFICATION SET — the engine's own reference vocabulary (`ReferenceIdl.refIdl`) and the
// two vendored non-UI samples — the same set `IdlCertificationTests` certifies the F# and
// TypeScript backends over. What the leg proves is therefore the F* BACKEND, and the corpus
// read has left this file entirely: nothing here resolves `SiblingCorpus`, so the leg is
// not gated on a sibling clone and `--emit-fstar` takes its vocabulary from this project.
// A domain proves its OWN vocabulary in its own repository with the same generator; the
// UI vocabulary's model, proofs and cost are `fuaran#1754`'s, the proof kit's first adopter.
//
// It lives in the Expecto suite rather than in `check.ps1`'s PowerShell because the
// generator is F#: the leg invokes it under the `Proofs.Vocabulary` filter, beside the two
// families it already runs, and the repository gate runs it with everything else.
// ---------------------------------------------------------------------------

let private repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let private lf (s: string) = s.Replace("\r\n", "\n")

/// One generated model: the F* module name (which is also the file name under `proofs/`),
/// the vocabulary it is generated from, and the provenance its header names.
type private Generated =
    { Module: string
      Idl: Idl
      Provenance: FStarTarget.Provenance }

/// What every header says about the remedy, once — the command and the family that holds
/// the committed file to it. The origin paragraph of each provenance ends with this.
let private remedyLines =
    [ "DO NOT EDIT: `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar` regenerates"
      "it, and the `Proofs.Vocabulary` family holds this file to a fresh generation from that"
      "source — a vocabulary that moves without a regeneration is VOCABULARY DRIFT, and"
      "`proofs/check.ps1` names it." ]

/// What every header says about the claim, once. The model is a property of the BACKEND
/// over the certification set, which is the whole of the Phase 173 change: which
/// vocabulary the theorem is ABOUT.
let private provesLines =
    [ "F* BACKEND (`Fuaran.Core.Idl.Codegen`'s `FStarTarget`) over the vocabulary the engine"
      "is certified on — the same set the F# and TypeScript backends are certified over — and"
      "not of any domain's vocabulary. A domain proves its own vocabulary in its own"
      "repository with the same generator; the UI vocabulary's model is `fuaran#1754`'s." ]

/// The certification set the F* backend is proved over, in the order the F# and TypeScript
/// backends meet it in `IdlCertificationTests`: the reference vocabulary carries the part of
/// the type model neither vendored sample uses, and the two samples carry the declared
/// NON-DEFAULT wire shape (bare-string discriminator, flat node envelope, declaration key
/// order) that the reference vocabulary, on `WireShape.Default`, does not reach.
let private generated: Generated list =
    [ { Module = "Vocabulary"
        Idl = ReferenceIdl.refIdl
        Provenance =
          { Origin =
              [ "the engine's REFERENCE vocabulary — `tests/Fuaran.Core.Tests/ReferenceIdl.fs`, the"
                "domain-neutral `Idl` Phase 114 authored to reach the part of the type model no"
                "vendored sample uses (fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "DocVocabulary"
        Idl = SecondDomainSpike.docIdl
        Provenance =
          { Origin =
              [ "the vendored SECOND-DOMAIN sample — `tests/Fuaran.Core.Tests/SecondDomainSpike.fs`,"
                "a document vocabulary with a corpus written outside this repository, on the"
                "declared non-default wire shape the reference vocabulary does not use"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } }
      { Module = "ScoreVocabulary"
        Idl = ScoreDomainSpike.scoreIdl
        Provenance =
          { Origin =
              [ "the vendored THIRD-DOMAIN sample — `tests/Fuaran.Core.Tests/ScoreDomainSpike.fs`,"
                "a notation vocabulary with a corpus written outside this repository: records and"
                "omit-at-default at scale, on the same declared non-default wire shape"
                "(fuaran-core Phase 173, applying D14 to `proofs/`)." ]
              @ remedyLines
            Proves = provesLines } } ]

let private modelPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", g.Module + ".fst")

/// The proof script beside a model: `<M>Proofs.fst`, opening `<M>`.
let private proofsName (g: Generated) = g.Module + "Proofs"

let private proofsPath (g: Generated) =
    Path.Combine(repoRoot, "proofs", proofsName g + ".fst")

/// The kinds a committed model covers: EVERY kind the backend can express, not the
/// `FStarTarget.proofKinds` slice. That rule is a cost control sized against a vocabulary whose
/// node envelope already carries dozens of declared types, where a kind reaching only those
/// costs one constructor and a kind bringing its own object is a measured decision (Phase 150,
/// over the UI vocabulary). Applied to the certification set it is the wrong instrument: the
/// reference vocabulary's envelope is two scalars, so the rule kept ONE of its five kinds and
/// dropped the four that carry the type-model remainder the vocabulary exists to reach —
/// checked before this was written, and the reason `selection` is not `proofKinds` here. The
/// cost of the whole set is measured in `proofs/modules.json`, where it is small enough that
/// there is nothing for a cost control to control. The rule itself is unchanged, because it is
/// the adopter's (`fuaran#1754`) instrument at the scale it was measured at.
let private selection (idl: Idl) : string list =
    FStarTarget.partition idl
    |> List.filter (fun v -> v.Refusal.IsNone)
    |> List.map _.Tag

/// A minimal vocabulary with one kind, used by the refusal cases. Written by hand rather
/// than cut down from a certification vocabulary, so a change there cannot quietly make a
/// refusal case stop exercising the construct it is about.
let private tinyIdl (fieldType: IdlType) (opt: Optionality) : Idl =
    { Kinds =
        [ { Tag = "Only"
            Category = "Display"
            Fields =
              [ { Name = "member"
                  Type = fieldType
                  Opt = opt
                  Annotations = Annotations.Empty } ]
            Annotations = Annotations.Empty } ]
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields = []
      Ops = []
      Wire = WireShape.Default
      // Phase 180 — this was `{ HardenPolicy.Default with TransparentUnions = [] }`:
      // an override that existed only to drop the default's one entry. With the
      // default retired the override has nothing to say, and the vocabulary declares
      // outright what it always meant — no gated kind, no transparent case.
      Harden = HardenPolicy.Undeclared }

/// Both generated files for one vocabulary, from one walk: the model and the theorems over it.
/// Phase 150 committed only the model, because the theorems did not discharge at the UI
/// vocabulary's scale; over the certification set they do (measured, `proofs/modules.json`),
/// so both are committed and both are held here.
let private pair (g: Generated) : (string * Result<string, CodegenError>) list =
    let kinds = selection g.Idl

    [ modelPath g, FStarTarget.vocabularyModuleFrom g.Provenance g.Module g.Idl kinds
      proofsPath g, FStarTarget.proofsModuleFrom g.Provenance (proofsName g) g.Module g.Idl kinds ]

/// `--emit-fstar` — rewrite every committed model and proof script from the certification set.
/// The command the generation diff names when it fails, and the only sanctioned way those files
/// change.
let emit () : int =
    let write (path: string) (result: Result<string, CodegenError>) =
        match result with
        | Error e ->
            eprintfn "--emit-fstar: %s" (CodegenError.describe e)
            2
        | Ok text ->
            File.WriteAllText(path, lf text, System.Text.UTF8Encoding false)
            printfn "regenerated %s" path
            0

    generated
    |> List.fold
        (fun worst g ->
            let written =
                pair g |> List.map (fun (path, result) -> write path result) |> List.max

            printfn
                "%s: %d of %d kinds modelled; the rest are named in the emitted header"
                g.Module
                (selection g.Idl |> List.length)
                (List.length g.Idl.Kinds)

            max worst written)
        0

let private refusalOf (t: IdlType) =
    match FStarTarget.vocabularyModule "M" (tinyIdl t Required) [ "Only" ] with
    | Ok _ -> None
    | Error e -> Some(CodegenError.describe e)

// ---------------------------------------------------------------------------
// Phase 168 — reading the SHAPE of an emitted proof script. The pins below are over the
// text the emitter produces, so these read it the way a reader does: by the lemma heads.
// ---------------------------------------------------------------------------

/// The proof script over a vocabulary, every expressible kind selected, or a failed test.
let private proofsOver (idl: Idl) : string =
    match FStarTarget.proofsModule "MP" "M" idl (selection idl) with
    | Ok text -> text
    | Error e -> failtestf "the target refused the vocabulary: %s" (CodegenError.describe e)

/// Every round-trip lemma head in emission order: its keyword (`let rec` / `and` / `let`)
/// and its name.
let private lemmaHeads (proofs: string) : (string * string) list =
    [ for m in Regex.Matches(proofs, @"(?m)^(let rec|and|let) (rt_[A-Za-z0-9_]+) ") ->
          m.Groups[1].Value, m.Groups[2].Value ]

/// Phase 168's presence-PATTERN lemmas — `…__p<bits>`, one per 2^k pattern. Phase 182 retired
/// the shape; this reader survives so the exponential count can be asserted ABSENT rather than
/// merely not looked for.
let private patternLemmas (proofs: string) : string list =
    lemmaHeads proofs
    |> List.map snd
    |> List.filter (fun n -> Regex.IsMatch(n, @"__p[01]+$"))

/// Phase 182's presence LOOKUP lemmas — `lk_<T>__<Ctor>__<member>` for a member that is always
/// emitted, `…__present` / `…__absent` for a conditional one. They are NOT in the mutual family,
/// so they are plain `let` and carry no `decreases`.
let private lookupLemmas (proofs: string) : string list =
    [ for m in Regex.Matches(proofs, @"(?m)^let (lk_[A-Za-z0-9_]+) ") -> m.Groups[1].Value ]

/// The scoped fuel one lookup lemma is pushed under, or a failed test.
let private lookupFuelOf (name: string) (proofs: string) : int =
    let m =
        Regex.Match(
            proofs,
            @"#push-options ""--fuel (\d+) --ifuel \d+""\s*\r?\nlet "
            + Regex.Escape name
            + " "
        )

    Expect.isTrue m.Success (sprintf "%s is pushed under its own --fuel" name)
    int m.Groups[1].Value

/// The constructor arms of one type's family — `rt_<T>__<Ctor>` — by constructor label.
let private armLemmas (family: string) (proofs: string) : string list =
    lemmaHeads proofs
    |> List.map snd
    |> List.choose (fun n ->
        let m = Regex.Match(n, "^" + Regex.Escape family + @"__([A-Za-z0-9]+)$")
        if m.Success then Some m.Groups[1].Value else None)

/// The right-hand sides of one lemma's `match` arms, trimmed — what the lemma proves with.
let private bodyOf (name: string) (proofs: string) : string list =
    let start = proofs.IndexOf(sprintf " %s (#num #flt: eqtype)" name)
    Expect.isTrue (start >= 0) (sprintf "the script declares %s" name)
    let rest = proofs.Substring(start)
    let stop = Regex.Match(rest.Substring(1), @"(?m)^(and|let) ")

    let text =
        if stop.Success then
            rest.Substring(0, stop.Index + 1)
        else
            rest

    [ for m in Regex.Matches(text, @"(?m)^  \| .*? -> (.+)$") -> m.Groups[1].Value.Trim() ]

/// How many conditional members one lemma's `requires` pins — one `None?` / `Some?` per optional
/// member, one equality (or its negation) per omit-at-default member. Phase 168's pattern lemmas
/// pinned ALL of them; Phase 182's lookup lemmas pin exactly the ONE the lemma is about, which is
/// the whole difference between 2^k lemmas and 2k.
let private conditionalsPinned (name: string) (proofs: string) : int =
    let m =
        Regex.Match(
            proofs,
            Regex.Escape name
            + @" \(#num #flt: eqtype\) \(x: [^)]+\) : Lemma \(requires \((.*?)\)\) \(ensures"
        )

    Expect.isTrue m.Success (sprintf "%s carries a `requires`" name)
    Regex.Matches(m.Groups[1].Value, @"None\? |Some\? |not \(f[0-9]+ = |(?<!not \()f[0-9]+ = ").Count

/// The conditional (optional or omit-at-default) members of a field list, host-only excluded.
let private conditionalCount (fs: IdlField list) =
    fs
    |> List.filter (fun f ->
        match f.Opt with
        | Optional
        | OmitDefault _ -> true
        | Required
        | HostOnly -> false)
    |> List.length

/// How many LOOKUP lemmas one constructor's field list should produce under Phase 182's shape:
/// nothing at all below the split threshold; otherwise one per member from the FIRST conditional
/// member in key order onwards, doubled for a conditional one. That is `2k + r'`, where `r'` is
/// the always-emitted members that sort after the first conditional — the members BEFORE it are
/// reached by `find_field` without meeting a branch and need no lemma.
///
/// Note the arithmetic against the phase's own `2k + 1`: that figure counted the conditional
/// members and the constructor's own round-trip lemma, and passed over the always-emitted members
/// whose key the conditionals before them move. Both numbers are pinned below.
let private expectedLookupCount (fs: IdlField list) : int =
    let conditional (f: IdlField) =
        match f.Opt with
        | Optional
        | OmitDefault _ -> true
        | Required
        | HostOnly -> false

    let sorted =
        fs
        |> List.filter (fun f ->
            match f.Opt with
            | HostOnly -> false
            | _ -> true)
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(a.Name, b.Name))

    if conditionalCount fs < FStarTarget.presenceSplitAt then
        0
    else
        let start = sorted |> List.findIndex conditional

        sorted
        |> List.skip start
        |> List.sumBy (fun f -> if conditional f then 2 else 1)

/// A vocabulary at the SCALE Phase 150 measured the one-lemma shape failing at — the UI
/// vocabulary's node envelope carried five optional members and its widest kind eleven members
/// with five conditional — under neutral names and scalar members, so the pin is about the
/// emitted shape and nothing else. Never checked by a prover here.
///
/// Phase 182 added `Grid`: SIXTEEN conditional members, the width `fuaran#1754` measured its
/// `DataGrid` at, where Phase 168's per-pattern split emitted 65,536 lemmas for that one kind.
/// It is the scale the linear shape exists for, and the pin below is what says the exponential
/// count is gone rather than merely smaller.
let private uiScaleIdl: Idl =
    let field name t opt =
        { Name = name
          Type = t
          Opt = opt
          Annotations = Annotations.Empty }

    let kind tag fields =
        { Tag = tag
          Category = "Display"
          Fields = fields
          Annotations = Annotations.Empty }

    { Kinds =
        [ kind "Leaf" [ field "text" TStr Required ]
          kind "Branch" [ field "children" (TList TNode) Required; field "title" TStr Optional ]
          kind
              "Wide"
              [ field "a" TStr Required
                field "b" TBool Required
                field "c" (TList TStr) Required
                field "d" TStr Required
                field "e" TBool Required
                field "f" (TList TNode) Required
                field "g" TStr Optional
                field "h" TBool Optional
                field "i" (TList TStr) Optional
                field "j" TStr (OmitDefault(VStr "x"))
                field "k" TBool (OmitDefault(VBool false)) ]
          // `Grid` — sixteen conditional members, `fuaran#1754`'s `DataGrid` width. The one
          // always-emitted member is named so it sorts AFTER every conditional one, which is the
          // `DataGrid` shape and is also what makes this kind's lookup count exactly `2k + 1`.
          kind
              "Grid"
              ([ for i in 0..15 -> field (sprintf "c%02d" i) TStr Optional ]
               @ [ field "rows" (TList TNode) Required ]) ]
      Unions = []
      Enums = []
      Records = []
      Defaults = []
      NodeFields =
        [ field "hidden" TBool Optional
          field "label" TStr Optional
          field "role" TStr Optional
          field "state" TStr Optional
          field "style" TStr Optional ]
      Ops = []
      Wire = WireShape.Default
      // Phase 180 — this was `{ HardenPolicy.Default with TransparentUnions = [] }`:
      // an override that existed only to drop the default's one entry. With the
      // default retired the override has nothing to say, and the vocabulary declares
      // outright what it always meant — no gated kind, no transparent case.
      Harden = HardenPolicy.Undeclared }

/// Phase 204 — the synthetic vocabulary Phase 182's probe measured the MODEL at: one kind, `Grid`,
/// carrying `k` optional string members and one always-emitted member that sorts after them.
/// `proofs/README.md` records the prover's verdict on it at k = 5, 8, 12, 16; the pin below is
/// about the emitted text alone.
let private gridAt (k: int) : Idl =
    let field name t opt =
        { Name = name
          Type = t
          Opt = opt
          Annotations = Annotations.Empty }

    { uiScaleIdl with
        Kinds =
            [ { Tag = "Grid"
                Category = "Display"
                Fields =
                  [ for i in 0 .. k - 1 -> field (sprintf "c%02d" i) TStr Optional ]
                  @ [ field "rows" (TList TNode) Required ]
                Annotations = Annotations.Empty } ]
        NodeFields = [] }

/// Phase 256 — a SUFFIXED constructor (two or more conditional members, so its links apply the
/// per-slot option encoders of Phase 222) carrying an omit-at-default member of every slot whose
/// encoder is a member of the encoder's mutual family: a list, a map, a record and a union, each
/// at its declared default. `flag` is the leaf control — a scalar default, whose wrapper was
/// never at fault — and `note` is an optional member beside them. The list's element is a node,
/// so the vocabulary is a tree and not a flat record. Written by hand, not cut down from a
/// consumer's vocabulary: `fuaran#1860` met the defect at `Embed.permissions` (a list of an enum,
/// defaulting to `[]`), and every slot below fails the pinned prover the same way against the
/// 0.31.0 emitter — measured one slot per run, `Error 19 ... Failed to prove: v << v`.
let private defaultedFamilyIdl: Idl =
    let field name t opt =
        { Name = name
          Type = t
          Opt = opt
          Annotations = Annotations.Empty }

    { uiScaleIdl with
        Kinds =
            [ { Tag = "Holder"
                Category = "Display"
                Fields =
                  [ field "items" (TList TNode) (OmitDefault(VList []))
                    field "table" (TMap TStr) (OmitDefault(VMap []))
                    field "at" (TRecord "Pt") (OmitDefault(VRecord [ "x", VStr "o" ]))
                    field "src" (TUnion("Src", [])) (OmitDefault(VUnion("Lit", [ "text", VStr "" ])))
                    field "flag" TBool (OmitDefault(VBool false))
                    field "note" TStr Optional ]
                Annotations = Annotations.Empty }
              { Tag = "Leaf"
                Category = "Display"
                Fields = [ field "text" TStr Required ]
                Annotations = Annotations.Empty } ]
        Records =
            [ { Name = "Pt"
                Fields = [ field "x" TStr Required ] } ]
        Unions =
            [ { Name = "Src"
                Params = []
                Cases =
                  [ { Tag = "Lit"
                      Fields = [ field "text" TStr Required ]
                      Annotations = Annotations.Empty } ] } ]
        NodeFields = [] }

/// The model text over a vocabulary, every expressible kind selected, or a failed test.
let private modelOver (idl: Idl) : string =
    match FStarTarget.vocabularyModule "M" idl (selection idl) with
    | Ok text -> text
    | Error e -> failtestf "the target refused the vocabulary: %s" (CodegenError.describe e)

/// One constructor's encoder arm in a model — the line after its `| C__vkind__<Kind> …` head.
let private encoderArm (tag: string) (model: string) : string =
    let lines = (lf model).Split('\n')

    let head =
        lines
        |> Array.findIndex (fun l -> l.StartsWith(sprintf "  | C__vkind__%s " tag) && l.TrimEnd().EndsWith "->")

    lines[head + 1]

[<Tests>]
let idlFStarTargetTests =
    testList
        "Proofs.Vocabulary"
        [

          // ---- the refusals ------------------------------------------------

          testCase "a tree-op slot is a NAMED refusal, not a dropped member"
          <| fun _ ->
              match refusalOf TOp with
              | None -> failtest "the F* target emitted a model over a slot it cannot express"
              | Some why ->
                  Expect.stringContains why "tree-op" "the refusal names the construct"
                  Expect.stringContains why "kind Only.member" "the refusal names where it was reached"

          testCase "a bare-kind slot is a named refusal"
          <| fun _ ->
              match refusalOf TKind with
              | None -> failtest "the F* target emitted a model over a bare-kind slot"
              | Some why -> Expect.stringContains why "bare kind" "the refusal names the construct"

          testCase "a wire-visible closure is MODELLED as the sentinel it carries, not refused"
          <| fun _ ->
              // The distinction the refusal set turns on: a closure is not *absent* from the
              // wire, it is a constant on it. Modelling it as `unit` is the statement that it
              // carries no information; refusing it would exclude nearly every kind.
              match FStarTarget.vocabularyModule "M" (tinyIdl TClosure Required) [ "Only" ] with
              | Error e -> failtestf "a wire-visible closure was refused: %s" (CodegenError.describe e)
              | Ok text ->
                  Expect.stringContains text "JStr \"<closure>\"" "the sentinel is what the model encodes"
                  Expect.stringContains text "member:(unit)" "and `unit` is what it carries"

          testCase "a HOST-ONLY member is absent from the model entirely"
          <| fun _ ->
              match
                  FStarTarget.vocabularyModule
                      "M"
                      (tinyIdl
                          (TFn
                              { FSharp = "int -> 'Msg"
                                TypeScript = "never"
                                Placeholder = "(fun _ -> ())" })
                          HostOnly)
                      [ "Only" ]
              with
              | Error e -> failtestf "a host-only member was refused: %s" (CodegenError.describe e)
              | Ok text ->
                  Expect.isFalse
                      (text.Contains "\"member\"")
                      "a member that is never on the wire has no place in a model OF the wire"

          testCase "the refusal is typed, and its case is the shared CodegenError channel"
          <| fun _ ->
              match FStarTarget.vocabularyModule "M" (tinyIdl TOp Required) [ "Only" ] with
              | Ok _ -> failtest "expected a refusal"
              | Error(CodegenError.UnmodellableInFStar _) -> ()
              | Error other -> failtestf "expected UnmodellableInFStar, got %A" other

          // ---- every refusal class is reached, and by whom -------------------

          testCase
              "every refusal class of the backend is reached at least once — by the reference vocabulary or by a hand-written case"
          <| fun _ ->
              // Phase 173's acceptance says the reference vocabulary reaches every refusal
              // class "by construction (it was authored to reach every IdlType)". Checked
              // against the backend, that premise is FALSE, and the correction is worth
              // pinning: reaching every type case is not reaching every refusal class,
              // because most of the backend's refusals are about an ILL-FORMED IDL — an
              // undeclared record, a union arity mismatch, an unresolved type parameter — that
              // a well-formed certification vocabulary cannot carry, and the one refusal a
              // well-formed vocabulary CAN reach at kind level is the numeric default. The
              // reference vocabulary reaches that one (`Measure.value` declares
              // `Fixed { value = 0.0 }` and the model's numeric carriers are opaque). Its
              // `TKind` / `TOp` slots live in its OP vocabulary, which the model — a model of
              // nodes — does not walk, so those two classes are reached by the hand-written
              // cases above. Each row below names the class and what reaches it; a class the
              // backend gains without a row here is a refusal nothing exercises.
              let reached (idl: Idl) (tags: string list) =
                  match FStarTarget.vocabularyModule "M" idl tags with
                  | Ok _ -> None
                  | Error e -> Some(CodegenError.describe e)

              let onlyTag = [ "Only" ]

              let withUnion (u: IdlUnion) (idl: Idl) = { idl with Unions = [ u ] }

              let classes: (string * string * (unit -> string option)) list =
                  [ "a numeric default the opaque model cannot spell",
                    "declared default the opaque numeric model cannot spell",
                    fun () ->
                        FStarTarget.partition ReferenceIdl.refIdl
                        |> List.tryFind (fun v -> v.Tag = "Measure")
                        |> Option.bind _.Refusal
                    "a tree-op slot", "tree-op slot", (fun () -> refusalOf TOp)
                    "a bare-kind slot", "bare kind slot", (fun () -> refusalOf TKind)
                    "a host codec with no declared host type",
                    "host codec with no declared host type",
                    fun () ->
                        refusalOf (
                            THosted
                                { FSharp = ""
                                  Encode = "enc"
                                  Decode = "dec" }
                        )
                    "an unresolved type parameter",
                    "unresolved type parameter",
                    fun () -> reached (tinyIdl (TVar "T") Required) onlyTag
                    "an undeclared record",
                    "undeclared record",
                    (fun () -> reached (tinyIdl (TRecord "Missing") Required) onlyTag)
                    "an undeclared union",
                    "undeclared union",
                    fun () -> reached (tinyIdl (TUnion("Missing", [])) Required) onlyTag
                    "a union arity mismatch",
                    "union arity mismatch",
                    fun () ->
                        tinyIdl (TUnion("Slot", [ TStr; TStr ])) Required
                        |> withUnion
                            { Name = "Slot"
                              Params = [ "T" ]
                              Cases =
                                [ { Tag = "Fixed"
                                    Fields =
                                      [ { Name = "value"
                                          Type = TVar "T"
                                          Opt = Required
                                          Annotations = Annotations.Empty } ]
                                    Annotations = Annotations.Empty } ] }
                        |> fun idl -> reached idl onlyTag
                    "an undeclared kind", "undeclared kind", (fun () -> reached (tinyIdl TStr Required) [ "Absent" ])
                    "a default omitting a required member",
                    "default omitting the required member",
                    fun () ->
                        let idl =
                            tinyIdl (TUnion("Text", [])) (OmitDefault(VUnion("Inline", [])))
                            |> withUnion
                                { Name = "Text"
                                  Params = []
                                  Cases =
                                    [ { Tag = "Inline"
                                        Fields =
                                          [ { Name = "text"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty } ]
                                        Annotations = Annotations.Empty } ] }

                        reached idl onlyTag
                    "a transparent union case that is not a single scalar member",
                    "transparent union case that is not a single scalar member",
                    fun () ->
                        let idl =
                            tinyIdl (TUnion("Text", [])) Required
                            |> withUnion
                                { Name = "Text"
                                  Params = []
                                  Cases =
                                    [ { Tag = "Inline"
                                        Fields =
                                          [ { Name = "text"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty }
                                            { Name = "lang"
                                              Type = TStr
                                              Opt = Required
                                              Annotations = Annotations.Empty } ]
                                        Annotations = Annotations.Empty } ] }

                        reached
                            { idl with
                                Harden =
                                    { idl.Harden with
                                        TransparentUnions = [ "Text", "Inline" ] } }
                            onlyTag ]

              for name, fragment, reach in classes do
                  match reach () with
                  | None -> failtestf "%s was NOT refused — the class is unreached, or the backend now admits it" name
                  | Some why ->
                      Expect.stringContains why fragment (sprintf "the refusal of %s names the construct" name)

          // ---- the partition, per certification vocabulary -----------------

          testCase "every kind of every certification vocabulary is either modelled or refused BY NAME"
          <| fun _ ->
              for g in generated do
                  let verdicts = FStarTarget.partition g.Idl

                  Expect.equal
                      (List.length verdicts)
                      (List.length g.Idl.Kinds)
                      (sprintf "%s: the partition is total over the vocabulary's kinds" g.Module)

                  for v in verdicts do
                      match v.Refusal with
                      | Some why ->
                          Expect.isTrue
                              (why.Length > 20)
                              (sprintf "%s: the refusal of '%s' says what could not be expressed" g.Module v.Tag)
                      | None -> ()

                  // The rule is a SUBSET of what the target can express, never a second opinion
                  // about expressibility — a kind it selects that the target then refuses would
                  // be a generator that contradicts its own partition.
                  let expressible = verdicts |> List.filter (fun v -> v.Refusal.IsNone)

                  for tag in FStarTarget.proofKinds g.Idl do
                      Expect.isTrue
                          (expressible |> List.exists (fun v -> v.Tag = tag))
                          (sprintf
                              "%s: the proof vocabulary selected '%s', which the target cannot express"
                              g.Module
                              tag)

          testCase
              "the certification set is a real slice of the backend: the two wire shapes, the whole of each sample, and one named boundary"
          <| fun _ ->
              // A partition that could express nothing would satisfy the clause above. What is
              // asserted here is what the set was CHOSEN for. The reference vocabulary carries
              // the type-model remainder and one BOUNDARY — `Measure`, refused for its numeric
              // default and nothing else — so a change that widened the refusal, or narrowed it
              // to nothing, reads as a change of coverage rather than as ordinary drift. The
              // two samples are on the declared non-default wire shape, and every kind of each
              // is modelled: a sample whose kinds started falling out of the proof vocabulary
              // would be a backend that had stopped covering the shape the reference one lacks.
              let refused (g: Generated) =
                  FStarTarget.partition g.Idl
                  |> List.filter (fun v -> v.Refusal.IsSome)
                  |> List.map _.Tag

              let byModule = generated |> List.map (fun g -> g.Module, g) |> Map.ofList

              Expect.equal
                  (refused byModule["Vocabulary"])
                  [ "Measure" ]
                  "the reference vocabulary's one boundary is the numeric-default kind, and only that"

              Expect.equal
                  (selection ReferenceIdl.refIdl |> List.length)
                  (List.length ReferenceIdl.refIdl.Kinds - 1)
                  "and every other reference kind is modelled"

              // The cost rule is NOT what selects here, and the difference is asserted so that a
              // reader who reaches for `proofKinds` meets the measurement rather than re-taking
              // it: over the reference vocabulary the rule keeps one kind of five.
              Expect.equal
                  (FStarTarget.proofKinds ReferenceIdl.refIdl)
                  [ "Group" ]
                  "`proofKinds`, the UI-scale cost control, would keep one reference kind — which is why the committed model does not use it"

              // The same boundary, and only that boundary, is what the third-domain sample
              // meets: `Note.voice` and `Chord.voice` declare the integer default `1`. Pinned as
              // a set rather than as "non-empty" so that a sample kind falling out for any OTHER
              // reason reads as the coverage change it is.
              for m, boundary in
                  [ "DocVocabulary", Set.empty
                    "ScoreVocabulary", Set.ofList [ "Chord"; "Note" ] ] do
                  let g = byModule[m]

                  Expect.equal
                      g.Idl.Wire.NodeEnvelope
                      NodeEnvelopeShape.FlatKind
                      (sprintf "%s is on the flat envelope" m)

                  Expect.equal
                      (refused g |> Set.ofList)
                      boundary
                      (sprintf "%s: the sample's only boundary is the numeric-default kind set" m)

                  Expect.equal
                      (selection g.Idl |> List.length)
                      (List.length g.Idl.Kinds - Set.count boundary)
                      (sprintf "%s: and every other kind of the sample is modelled" m)

              Expect.equal
                  ReferenceIdl.refIdl.Wire.NodeEnvelope
                  NodeEnvelopeShape.NestedKind
                  "the reference vocabulary is on the nested envelope, so the set reaches both"

          // ---- the generation diff ------------------------------------------

          testCase
              "every committed proofs/<M>.fst and <M>Proofs.fst IS a fresh generation from its certification vocabulary"
          <| fun _ ->
              for g in generated do
                  let remedy =
                      sprintf
                          "VOCABULARY DRIFT — %s has moved and the committed F* files have not.\n"
                          (match g.Module with
                           | "Vocabulary" -> "the reference vocabulary"
                           | m -> "the vendored sample behind " + m)
                      + "Regenerate them with `dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar`,\n"
                      + "commit them, and re-run the proof leg: the model must still check, and the round trip\n"
                      + "must still discharge, over the new vocabulary."

                  for path, result in pair g do
                      let name = Path.GetFileName path

                      match result with
                      | Error e ->
                          failtestf "%s: the F* target refused the vocabulary: %s" name (CodegenError.describe e)
                      | Ok text ->
                          Expect.isTrue (File.Exists path) (sprintf "proofs/%s is committed" name)

                          Expect.equal
                              (lf text)
                              (lf (File.ReadAllText path))
                              (sprintf "proofs/%s has drifted from a fresh generation.\n%s" name remedy)

          testCase "no committed model was generated from anything but this project — the header names its source"
          <| fun _ ->
              // The generation diff above already implies this; it is stated on its own because
              // it is the sentence Phase 173 exists for. A model whose header named the shared
              // corpus would be a domain's proof back in the substrate's leg.
              for g in generated do
                  for path in [ modelPath g; proofsPath g ] do
                      let name = Path.GetFileName path
                      let text = File.ReadAllText path

                      Expect.stringContains
                          text
                          "tests/Fuaran.Core.Tests/"
                          (sprintf "proofs/%s names a source inside this project" name)

                      Expect.isFalse
                          (text.Contains "pinned wire-format IDL" || text.Contains "idl.json")
                          (sprintf "proofs/%s does not name the shared corpus as its source" name)

          // ---- the theorems emitter -------------------------------------------

          testCase "the theorems emitter produces a proof script over the same walk"
          <| fun _ ->
              // The committed `<M>Proofs.fst` are held above; this pins the property the pair
              // needs at a vocabulary nothing else owns: the two emitters agree about WHICH
              // vocabulary they are talking about. (Phase 150 measured the emitted round trip
              // NOT discharging at the UI vocabulary's twenty kinds, for a structural reason
              // `proofs/README.md`'s theorem 1 records; at the certification set it discharges,
              // which is why the scripts are committed since Phase 173. The per-constructor lemma
              // shape that reaches UI scale is Phase 168's, pinned below.)
              match
                  FStarTarget.vocabularyModule "M" (tinyIdl TStr Required) [ "Only" ],
                  FStarTarget.proofsModule "MP" "M" (tinyIdl TStr Required) [ "Only" ]
              with
              | Error e, _
              | _, Error e -> failtestf "the target refused a one-scalar vocabulary: %s" (CodegenError.describe e)
              | Ok model, Ok proofs ->
                  Expect.stringContains model "let rec dec_node" "the model declares the node decoder"
                  Expect.stringContains proofs "open M" "the proof script opens the model it is about"

                  Expect.stringContains
                      proofs
                      "dec_node (enc_node #num #flt x) == Ok x"
                      "and states the round trip over the generated definitions"

          testCase "the un-suffixed entry points emit the generator's own provenance, and a named one replaces it"
          <| fun _ ->
              // `vocabularyModule` / `proofsModule` are the shipped entry points a caller with no
              // named source uses; `…From` is what a caller with one passes. The default must not
              // claim a source it does not have — until Phase 173 it named this repository's
              // corpus and its check script in a package a domain generates from.
              let idl = tinyIdl TStr Required

              match
                  FStarTarget.vocabularyModule "M" idl [ "Only" ],
                  FStarTarget.vocabularyModuleFrom generated.Head.Provenance "M" idl [ "Only" ]
              with
              | Error e, _
              | _, Error e -> failtestf "the target refused a one-scalar vocabulary: %s" (CodegenError.describe e)
              | Ok plain, Ok named ->
                  Expect.stringContains plain "the IDL its caller supplied" "the default names no source it cannot know"
                  Expect.isFalse (plain.Contains "proofs/check.ps1") "and no script of this repository"
                  Expect.stringContains named "ReferenceIdl.fs" "a named provenance is what the header carries"
                  Expect.stringContains named "F* BACKEND" "and what the model is a property of"

          // ---- the lemma SHAPE — one per constructor, the presence split LINEAR (Phase 182) ----
          //
          // These pin the emitted TEXT, not a prover result: the certification set's scripts are
          // checked by `proofs/check.ps1`, and the UI-scale fixture below is never checked here
          // at all. What is pinned is that the emitter isolates every constructor's object
          // shapes in its own query — the structural fix Phase 150 named for the failure it
          // measured at twenty UI kinds — so that an adopter at that scale (`fuaran#1754`)
          // inherits a generator already proved to emit the shape, and so that the certification
          // set itself exercises the split rather than leaving it to be met first elsewhere.

          testCase "the reference vocabulary's round trip is one lemma per kind, and `rt_vkind` is only the case split"
          <| fun _ ->
              let proofs = proofsOver ReferenceIdl.refIdl
              let kinds = selection ReferenceIdl.refIdl |> Set.ofList

              Expect.equal
                  (armLemmas "rt_vkind" proofs |> Set.ofList)
                  kinds
                  "one `rt_vkind__<Kind>` per modelled kind — the per-kind lemma the phase was cut for — and none for a refused kind"

              Expect.equal
                  (bodyOf "rt_vkind" proofs)
                  [ for k in selection ReferenceIdl.refIdl -> sprintf "rt_vkind__%s #num #flt x" k ]
                  "`rt_vkind`'s arms cite the per-kind lemmas and prove nothing themselves, so no query carries two kinds' shapes"

              Expect.isTrue
                  (proofs.Contains "\nlet rec rt_node (#num #flt: eqtype)")
                  "`rt_node` stays the family's first `let rec` — the top-level declaration the claims ladder resolves"

          testCase
              "a constructor with `presenceSplitAt` or more conditional members is proved one LOOKUP per member — and the certification set reaches the split"
          <| fun _ ->
              let proofs = proofsOver ReferenceIdl.refIdl

              Expect.isEmpty
                  (patternLemmas proofs)
                  "Phase 168's `__p<bits>` per-pattern lemmas are gone — the split is by member now, not by pattern"

              // The literal figure, so that a move in the reference vocabulary reads as the
              // coverage change it is: the node envelope (hidden, label) and `Embed`
              // (contentHash, props) each carry exactly two conditional members, and `Embed`
              // carries one always-emitted member (`moduleId`) that sorts after `contentHash`.
              Expect.equal
                  (lookupLemmas proofs |> List.length)
                  9
                  "the reference vocabulary reaches the split twice: the envelope's two conditional members (four lookups) and `Embed`'s two plus the always-emitted `moduleId` (five)"

              Expect.equal
                  (lookupLemmas proofs |> List.length)
                  (expectedLookupCount ReferenceIdl.refIdl.NodeFields
                   + ([ for k in ReferenceIdl.refIdl.Kinds do
                            if List.contains k.Tag (selection ReferenceIdl.refIdl) then
                                expectedLookupCount k.Fields ]
                      |> List.sum))
                  "and the figure is `2k + r'` per split constructor, computed the same way the emitter does"

              for name in lookupLemmas proofs do
                  if name.EndsWith "__present" || name.EndsWith "__absent" then
                      Expect.equal
                          (conditionalsPinned name proofs)
                          1
                          (sprintf
                              "%s pins exactly ONE conditional member — the others stay free, which is what makes the count linear"
                              name)

              Expect.stringContains
                  proofs
                  "lk_vkind__Embed__content_hash__present #num #flt x"
                  "the constructor's own lemma cites each member's lookup"

              for name in lookupLemmas proofs do
                  Expect.isGreaterThan
                      (lookupFuelOf name proofs)
                      2
                      (sprintf
                          "%s carries its own scoped fuel — `find_field` pushes through a key it is not looking for, and the default two unfoldings do not reach past the second"
                          name)

          testCase
              "at UI scale — a five-optional envelope and an eleven-member kind with five conditional — the split isolates every shape (shape only; no prover run)"
          <| fun _ ->
              // The scale Phase 150 measured the ONE-LEMMA shape failing at: the UI vocabulary's
              // node envelope carried five optional members (a 65-goal `rt_node` query that
              // failed a `--quake` seed) and its widest kind eleven members, five of them
              // conditional (an arm that failed outright). Authored here with neutral names
              // rather than read from the UI vocabulary's `idl.json`: this repository has no
              // reader for that artefact (`Artifact` renders one and parses none), Phase 123
              // removed the UI fixture from this project when Phase 114 cut the reference
              // vocabulary, and the property the adopter inherits is the SCALE, not the names.
              let proofs = proofsOver uiScaleIdl

              Expect.isEmpty
                  (patternLemmas proofs)
                  "not one `__p<bits>` lemma is emitted at any width — the 2^k shape is gone, not merely smaller"

              let under (prefix: string) =
                  lookupLemmas proofs |> List.filter (fun n -> n.StartsWith prefix) |> List.length

              Expect.equal (under "lk_node__Node__") 10 "the envelope's five conditional members, two lookups each"

              Expect.equal
                  (under "lk_vkind__Wide__")
                  (expectedLookupCount (uiScaleIdl.Kinds |> List.find (fun k -> k.Tag = "Wide")).Fields)
                  "the wide kind's five conditional members and the always-emitted members that sort after the first of them"

              // THE FIGURE THE PHASE WAS CUT FOR. Phase 168's shape emitted 2^16 = 65,536 lemmas
              // for this one kind (`fuaran#1754` measured 71,722 across the vocabulary, in a
              // 114 MB script). The linear shape emits 2*16 + 1: two per conditional member, plus
              // the one always-emitted member that sorts after them, and ONE round-trip lemma.
              Expect.equal
                  (under "lk_vkind__Grid__")
                  33
                  "a kind with sixteen conditional members emits 2*16 + 1 lookups, where the per-pattern split emitted 65,536"

              Expect.equal
                  (armLemmas "rt_vkind" proofs |> List.filter (fun n -> n = "Grid") |> List.length)
                  1
                  "and exactly one round-trip lemma for that kind"

              for name in lookupLemmas proofs do
                  if name.EndsWith "__present" || name.EndsWith "__absent" then
                      Expect.equal
                          (conditionalsPinned name proofs)
                          1
                          (sprintf "%s pins exactly the one conditional member it is about" name)

              Expect.equal
                  (bodyOf "rt_vkind" proofs)
                  [ "rt_vkind__Leaf #num #flt x"
                    "rt_vkind__Branch #num #flt x"
                    "rt_vkind__Wide #num #flt x"
                    "rt_vkind__Grid #num #flt x" ]
                  "the kind case split cites each kind's lemma, in declaration order"

              Expect.equal
                  (bodyOf "rt_vkind__Leaf" proofs |> List.length)
                  1
                  "a kind under the threshold is proved in one query, as before"

          testCase
              "the MODEL binds one suffix per conditional member and writes no tail twice — linear in k, where Phase 182's emitter was 2^k"
          <| fun _ ->
              // Phase 204. Phase 182's emitter wrote the member list's tail into BOTH arms of every
              // conditional member's test, so this kind's encoder was 2^16 tails long (5,318,686
              // characters on its probe; the pinned prover died loading it). Each assertion below
              // fails against that emitter — which is the go-red — and holds against this one.
              let model = modelOver uiScaleIdl
              let grid = encoderArm "Grid" model

              Expect.equal
                  (Regex.Matches(grid, @"\blet s[0-9]+ = sfx_vkind__Grid__c[0-9]+ ").Count)
                  16
                  "one `let` per conditional member, each binding that member's suffix"

              Expect.equal
                  (Regex.Matches(grid, "\"rows\"").Count)
                  1
                  "the always-emitted member after them is written ONCE — no tail is duplicated into both arms of a test"

              Expect.equal
                  (Regex.Matches(model, @"(?m)^let sfx_vkind__Grid__c[0-9]+ \(#num #flt: eqtype\)").Count)
                  16
                  "and each suffix is a top-level definition — a term a lemma can be stated about"

              Expect.equal
                  (Regex.Matches(model, @"(?m)^\[@@""opaque_to_smt""\]\r?\nlet sfx_").Count)
                  (Regex.Matches(model, @"(?m)^let sfx_").Count)
                  "every suffix is opaque to the solver, so no query sees through one except its own three lemmas"

              // Linear, measured on the emitted text: doubling k at most doubles the arm (plus the
              // one-character growth of the local names past s9), where 2^k squares it.
              let arm k =
                  (encoderArm "Grid" (modelOver (gridAt k))).Length

              Expect.isLessThan
                  (float (arm 16))
                  (2.2 * float (arm 8))
                  (sprintf
                      "the Grid arm grows linearly in k (k=4: %d, k=8: %d, k=16: %d characters)"
                      (arm 4)
                      (arm 8)
                      (arm 16))

              let proofs = proofsOver uiScaleIdl

              Expect.stringContains
                  proofs
                  "sk_vkind__Grid__c00__none #num #flt"
                  "the negative lookup is a chain over the named suffixes, citing the absent member's own `none` step"

              for step in [ "skip"; "hit"; "none" ] do
                  Expect.equal
                      (Regex.Matches(proofs, sprintf @"(?m)^let sk_vkind__Grid__c[0-9]+__%s " step).Count)
                      16
                      (sprintf "one `%s` step per suffix, proved by revealing it" step)

          testCase
              "every link of a suffix chain APPLIES a per-slot option encoder — no `match` or `if` sits in a chain argument for a lookup's VC to split on"
          <| fun _ ->
              // Phase 222. Phase 204's links carried the member's option encoding INLINE — a
              // `match` for an optional member, an `if` for an omit-at-default one — and a lookup
              // lemma re-binds the chain in its BODY, where each of those is a computation F*
              // splits the verification condition on: 58.6 units of rlimit for the first k=16
              // lookup. Each assertion below fails against that emitter (the go-red: 0 applications
              // where 16 are expected) and holds against this one, whose k=16 lookups discharge at
              // under half a unit.
              let model = modelOver uiScaleIdl
              let grid = encoderArm "Grid" model

              Expect.equal
                  (Regex.Matches(grid, @"= sfx_vkind__Grid__c[0-9]+ #num #flt \(enc_opt_str #num #flt f[0-9]+\) ").Count)
                  16
                  "each of the sixteen links applies `enc_opt_str` to its member"

              Expect.isFalse
                  (Regex.IsMatch(grid, @"#num #flt \((match|if) "))
                  "and no link's argument is a `match` or an `if`"

              let wide = encoderArm "Wide" model

              Expect.isTrue
                  (Regex.IsMatch(wide, @"sfx_vkind__Wide__j #num #flt \(enc_dflt_str #num #flt \(""x""\) f[0-9]+\)"))
                  (sprintf
                      "an omit-at-default member applies `enc_dflt_<slot>` with its default as an ARGUMENT: %s"
                      wide)

              for helper in [ "enc_opt_str"; "enc_opt_bool"; "enc_dflt_str"; "enc_dflt_bool" ] do
                  Expect.equal
                      (Regex.Matches(model, sprintf @"(?m)^and %s \(#num #flt: eqtype\)" helper).Count)
                      1
                      (sprintf "`%s` is emitted once, as a member of the encoder family" helper)

              Expect.stringContains
                  (proofsOver uiScaleIdl)
                  "sk_vkind__Grid__c00__hit #num #flt (enc_opt_str #num #flt f0)"
                  "and the lookups cite their steps at the same application, so the body re-binds no `match`"

          testCase
              "a defaulted list, map, record or union member's wrapper RECEIVES its encoding — the family recursion stays on the member, which is what lets F* see it terminate"
          <| fun _ ->
              // Phase 256. Phase 222's omit-at-default wrapper took the member and encoded it
              // itself — `if v = d then None else Some (JArr (enc_items_l_node v))` under
              // `(decreases v)` — which, for a slot whose encoder is in the mutual family, is a
              // recursive call on the SAME value the wrapper decreases on. The pinned prover
              // refuses it (`Error 19 ... Failed to prove: v << v`), so every vocabulary with such
              // a member in a suffixed constructor emitted a model that does not check; the first
              // was `fuaran#1860`'s. The encoding is now built at the CALL SITE, on the member
              // itself — a strict subterm of the value the calling encoder decreases on — and the
              // wrapper only chooses between it and absence. Each assertion below fails against
              // the 0.31.0 emitter (the go-red: the wrapper takes two value arguments and encodes
              // `v`) and holds against this one, whose model and proof script check under
              // `--report_assumes error` (STABILITY.md, 0.32.0).
              let model = modelOver defaultedFamilyIdl
              let holder = encoderArm "Holder" model

              for helper, fieldName, encoded, dflt in
                  [ "enc_dflt_l_node", "items", "JArr (enc_items_l_node %s)", "[]"
                    "enc_dflt_m_str", "table", "JObj (enc_entries_m_str %s)", "[]"
                    "enc_dflt_r_pt", "at", "enc_r_pt %s", "(C__r_pt__Mk (\"o\"))"
                    "enc_dflt_u_src", "src", "enc_u_src %s", "(C__u_src__Lit (\"\"))" ] do
                  let call =
                      Regex.Match(
                          holder,
                          sprintf
                              @"sfx_vkind__Holder__%s #num #flt \(%s #num #flt \(%s\) (f[0-9]+) \((.*?)\)\) "
                              fieldName
                              helper
                              (Regex.Escape dflt)
                      )

                  Expect.isTrue
                      call.Success
                      (sprintf
                          "`%s` is applied to its default, the member and the member's ENCODING: %s"
                          fieldName
                          holder)

                  Expect.equal
                      call.Groups[2].Value
                      (encoded.Replace("%s", call.Groups[1].Value))
                      (sprintf "`%s`'s encoding is built at the call site, on the member binder itself" fieldName)

                  let head =
                      Regex.Match(model, sprintf @"(?m)^and %s \(#num #flt: eqtype\) (.*) =\n(.*)$" helper)

                  Expect.isTrue head.Success (sprintf "`%s` is emitted as a member of the encoder family" helper)

                  Expect.stringContains
                      head.Groups[1].Value
                      "(e: jval num flt)"
                      (sprintf "`%s` takes the encoding as an argument" helper)

                  Expect.equal
                      head.Groups[2].Value
                      "  if v = d then None else Some e"
                      (sprintf "`%s` calls nothing: it only chooses between the encoding and absence" helper)

              // The leaf control: a scalar default's wrapper was never at fault — its encoding
              // calls nothing in the family — and keeps the Phase 222 shape byte for byte, which is
              // why every committed certification model regenerates unchanged (the generation diff
              // above is that assertion).
              Expect.isTrue
                  (Regex.IsMatch(
                      holder,
                      @"sfx_vkind__Holder__flag #num #flt \(enc_dflt_bool #num #flt \(false\) f[0-9]+\) "
                  ))
                  (sprintf "a leaf default keeps the two-argument application: %s" holder)

              Expect.stringContains
                  model
                  "and enc_dflt_bool (#num #flt: eqtype) (d: bool) (v: bool) : Tot (option (jval num flt)) (decreases v) =\n  if v = d then None else Some (JBool v)\n"
                  "and its wrapper still encodes the value itself"

              // The certification set reaches NONE of the four family-slot wrappers, which is why
              // Core's own proof leg stayed green over a defect every such consumer met. Pinned so
              // that the day a certification vocabulary does reach one, the committed model carries
              // the Phase 256 shape under the prover, and this line is the one to revisit.
              for g in generated do
                  match FStarTarget.vocabularyModule g.Module g.Idl (selection g.Idl) with
                  | Error e -> failtestf "%s: the target refused it: %s" g.Module (CodegenError.describe e)
                  | Ok text ->
                      Expect.isFalse
                          (Regex.IsMatch(text, @"(?m)^and enc_dflt_(l|m|r|u)_"))
                          (sprintf
                              "%s reaches no defaulted list, map, record or union member in a suffixed constructor"
                              g.Module)

          testCase
              "a suffixed constructor's leaf members are DECODED through named, opaque readers, and the round trip cites one value lemma per reader"
          <| fun _ ->
              // Phase 224. Phase 222's decoder wrote every member's read INLINE in the kind's arm,
              // so the round-trip arm's query unfolded sixteen reads inside a seventeen-deep nest of
              // outcomes: 345 units of rlimit for `rt_vkind`'s Grid arm at k=16, red at 40, even
              // when handed each read's value. Each assertion below fails against that emitter (the
              // go-red: no reader is emitted, so 0 where 16 are expected) and holds against this
              // one, whose k=16 round-trip arm discharges at 0.33 units.
              let model = modelOver uiScaleIdl
              let proofs = proofsOver uiScaleIdl

              // The whole text of one top-level definition, up to the next `let` / `and`.
              let definition (name: string) (text: string) =
                  let text = lf text
                  let start = text.IndexOf(sprintf " %s (#num #flt: eqtype)" name)
                  Expect.isTrue (start >= 0) (sprintf "the module declares %s" name)
                  let rest = text.Substring start
                  let stop = Regex.Match(rest.Substring 1, @"(?m)^(and|let|\[@@) ")

                  if stop.Success then
                      rest.Substring(0, stop.Index + 1)
                  else
                      rest

              Expect.equal
                  (Regex
                      .Matches(
                          lf model,
                          @"(?m)^\[@@""opaque_to_smt""\]\nlet rd_vkind__Grid__c[0-9]+ \(#num #flt: eqtype\) \(el: jval num flt\) : Tot \(outcome \(option \(string\)\)\) ="
                      )
                      .Count)
                  16
                  "one top-level, OPAQUE reader per conditional member of the suffixed kind"

              let decoder = definition "dec_vkind" model

              Expect.equal
                  (Regex
                      .Matches(
                          decoder,
                          @"let o[0-9]+ : outcome \(option \(string\)\) = rd_vkind__Grid__c[0-9]+ #num #flt el in"
                      )
                      .Count)
                  16
                  "the decoder APPLIES each reader, where Phase 222's inlined the read"

              Expect.isFalse
                  (decoder.Contains "get_prop \"c00\"")
                  "and no hoisted member's read is left inline in the decoder"

              // A member whose read calls the decoder family (here Wide's list member `i`) cannot be
              // hoisted above the family, and is read inline exactly as before.
              Expect.isFalse
                  (model.Contains "rd_vkind__Wide__i ")
                  "a member whose read reaches the family is not hoisted"

              Expect.stringContains decoder "get_prop \"i\" el" "and is read inline, as Phase 222 read it"

              for m in [ "g"; "h"; "j"; "k" ] do
                  Expect.stringContains
                      model
                      (sprintf "let rd_vkind__Wide__%s (#num #flt: eqtype)" m)
                      (sprintf "Wide's leaf member `%s` has its reader, optional or omit-at-default alike" m)

              Expect.equal
                  (Regex.Matches(proofs, @"(?m)^let rv_vkind__Grid__c[0-9]+ \(#num #flt: eqtype\)").Count)
                  16
                  "one value lemma per reader"

              Expect.stringContains
                  (definition "rv_vkind__Grid__c00" proofs)
                  "reveal_opaque (`%rd_vkind__Grid__c00)"
                  "a value lemma is the only place its reader is looked inside"

              let arm = definition "rt_vkind__Grid" proofs

              Expect.equal
                  (Regex.Matches(arm, @"rv_vkind__Grid__c[0-9]+ #num #flt x;").Count)
                  16
                  "the round-trip arm cites each reader's value lemma"

              Expect.isFalse
                  (Regex.IsMatch(arm, @"\(match f[0-9]+ with \| None -> lk_"))
                  "and carries no presence case of its own for a hoisted member"

              Expect.isTrue
                  (Regex.IsMatch(
                      definition "rt_vkind__Wide" proofs,
                      @"\(match f[0-9]+ with \| None -> lk_vkind__Wide__i__absent"
                  ))
                  "an unhoisted member keeps its two-way citation of the lookups"

              // A vocabulary with no suffixed constructor emits no reader at all — the shape is
              // additive, which is what keeps `DocVocabulary` byte-identical.
              let small = modelOver (gridAt 1)
              Expect.isFalse (small.Contains "rd_") "below the split threshold nothing is hoisted"

          testCase
              "the family recurses on a lexicographic measure, and the rlimit precedent is retired with the shape that needed it"
          <| fun _ ->
              let proofs = proofsOver ReferenceIdl.refIdl

              let heads =
                  lemmaHeads proofs
                  |> List.filter (fun (_, name) -> not (name.StartsWith "rt_e_"))

              for kw, name in heads do
                  Expect.isTrue (kw = "let rec" || kw = "and") (sprintf "%s is in the one mutual family" name)

              let lex = Regex.Matches(proofs, @"\(decreases %\[[a-z]+; [0-9]\]\)").Count

              Expect.equal
                  lex
                  (List.length heads)
                  "every lemma of the family carries `decreases %[value; tier]` — the split lemmas recurse on the SAME value and differ only in tier"

              Expect.stringContains
                  proofs
                  "#set-options \"--ext context_pruning\""
                  "the one cost option is still in force"

              Expect.isFalse
                  (proofs.Contains "#set-options \"--z3rlimit")
                  "the in-file `--z3rlimit 200` Phase 150 measured at UI scale is gone: the leg's own rlimit is what the split is checked under" ]
