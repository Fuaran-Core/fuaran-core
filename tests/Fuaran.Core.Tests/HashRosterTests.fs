/// The KEY ROSTER (Phase 290) — held to the tree.
///
/// `Hash.canonicalFields` is the ONE injective pre-image on the spine, and the doc comment above it
/// lists every key that builds through it. This family reads that list out of `Hash.fs` and holds
/// it to `src/` both ways:
///
///   1. every call site of `canonicalFields` under `src/` sits inside a definition the roster
///      names, so a new key cannot be minted through the encoding without joining the list; and
///   2. every definition the roster names is found calling it, so a listed key cannot quietly
///      leave the encoding; and
///   3. no definition under `src/` joins fields on the bare separator any more — `String.concat
///      Hash.foldSep`, a `+ Hash.foldSep +` splice, or the `U+0001` literal itself — which is the
///      shape all four of Phase 290's keys had (`Function.memoKey`, `Projection.digestOf`,
///      `Validator.canonicalCodes`, `Tree.Index.fingerprintOf`) and the shape a fifth would
///      most naturally take.
///
/// Source-reading, like the publication-boundary and compute-boundary families: the claim is about
/// text under `src/`, and the compiler cannot make it. The enclosing definition of a call site is
/// the nearest preceding top-level `let` of the nearest preceding `module`, qualified through every
/// enclosing module (`Tree.Index.fingerprintOf`) — a call inside a local `let` or a lambda belongs to
/// the top-level definition that contains it.
module Fuaran.Core.Tests.HashRosterTests

open System.IO
open System.Text.RegularExpressions
open Expecto

let private srcDir () = Snapshots.repoFile "src"

let private hashFile () =
    Path.Combine(srcDir (), "Fuaran.Core.Tree", "Hash.fs")

/// Every `.fs` under `src/`, build residue excluded, repository-relative with `/` separators.
let private sourceFiles () : (string * string[]) list =
    Directory.EnumerateFiles(srcDir (), "*.fs", SearchOption.AllDirectories)
    |> Seq.filter (fun f ->
        let rel = f.Replace('\\', '/')
        not (rel.Contains "/bin/" || rel.Contains "/obj/"))
    |> Seq.sort
    |> Seq.map (fun f -> f, File.ReadAllLines f)
    |> Seq.toList

let private relOf (file: string) =
    Path.GetRelativePath(Snapshots.repoFile "", file).Replace('\\', '/')

// ---------------------------------------------------------------------------
//  the roster, as written
// ---------------------------------------------------------------------------

let private rosterHead = "THE KEY ROSTER"

let private rosterEntry =
    Regex(@"^\s*///\s+-\s+`([A-Za-z_][\w.]*)`", RegexOptions.Compiled)

/// The names between the `THE KEY ROSTER` paragraph and the `canonicalFields` definition, in
/// document order.
let private roster () : string list =
    let lines = File.ReadAllLines(hashFile ())

    let start =
        lines
        |> Array.tryFindIndex (fun l -> l.Contains rosterHead)
        |> Option.defaultValue -1

    let stop =
        lines
        |> Array.tryFindIndex (fun l -> l.TrimStart().StartsWith "let canonicalFields")
        |> Option.defaultValue -1

    if start < 0 || stop < start then
        []
    else
        lines[start..stop]
        |> Array.choose (fun l ->
            let m = rosterEntry.Match l
            if m.Success then Some m.Groups[1].Value else None)
        |> Array.toList

// ---------------------------------------------------------------------------
//  the call sites, as found
// ---------------------------------------------------------------------------

let private indentOf (line: string) = line.Length - line.TrimStart().Length

let private moduleLine =
    Regex(@"^(\s*)module\s+(?:rec\s+)?([A-Za-z_]\w*)\s*=?\s*$", RegexOptions.Compiled)

let private letLine =
    Regex(@"^(\s*)let\s+(?:rec\s+|private\s+|internal\s+|inline\s+)*([A-Za-z_]\w*)", RegexOptions.Compiled)

/// The qualified name of the top-level definition enclosing line `i`: the nearest preceding
/// `module` (at any indentation smaller than the call), then the nearest `let` at that module's
/// member indentation, prefixed by every enclosing module outward.
let private enclosingDefinition (lines: string[]) (i: int) : string option =
    let rec modulesOutward (upto: int) (maxIndent: int) (acc: (int * string * int) list) =
        // Collect (line, name, indent) of each enclosing module, innermost first.
        let mutable j = upto
        let mutable found = None

        while found.IsNone && j >= 0 do
            let m = moduleLine.Match lines[j]

            if m.Success && m.Groups[1].Value.Length < maxIndent then
                found <- Some(j, m.Groups[2].Value, m.Groups[1].Value.Length)

            j <- j - 1

        match found with
        | Some((line, _, indent) as entry) -> modulesOutward (line - 1) indent (entry :: acc)
        | None -> acc

    let callIndent = indentOf lines[i]

    match modulesOutward (i - 1) (callIndent + 1) [] with
    | [] -> None
    | modules ->
        // `modules` is outermost first; the innermost is the last.
        let innerLine, _, innerIndent = List.last modules
        let memberIndent = innerIndent + 4
        let mutable j = i
        let mutable name = None

        while name.IsNone && j > innerLine do
            let m = letLine.Match lines[j]

            if m.Success && m.Groups[1].Value.Length = memberIndent then
                name <- Some m.Groups[2].Value

            j <- j - 1

        name
        |> Option.map (fun n -> (modules |> List.map (fun (_, mn, _) -> mn)) @ [ n ] |> String.concat ".")

let private callSite = Regex(@"\bcanonicalFields\b", RegexOptions.Compiled)

/// Every definition under `src/` (outside `Hash.fs`) that calls `canonicalFields`, with the file
/// and line of one call, keyed by qualified name.
let private callers () : Map<string, string> =
    let hash = Path.GetFullPath(hashFile ())

    sourceFiles ()
    |> List.filter (fun (f, _) ->
        Path.GetFullPath f <> hash
        && relOf f <> "src/Fuaran.Core.Conformance/ParityVectors.fs")
    |> List.collect (fun (f, lines) ->
        lines
        |> Array.mapi (fun i l -> i, l)
        |> Array.filter (fun (_, l) ->
            let code = (l.Split("//").[0])
            callSite.IsMatch code)
        |> Array.map (fun (i, _) ->
            let name =
                enclosingDefinition lines i
                |> Option.defaultValue (sprintf "<no enclosing definition at %s:%d>" (relOf f) (i + 1))

            name, sprintf "%s:%d" (relOf f) (i + 1))
        |> Array.toList)
    |> List.fold
        (fun acc (name, where) ->
            if Map.containsKey name acc then
                acc
            else
                Map.add name where acc)
        Map.empty

// ---------------------------------------------------------------------------
//  the bare joins
// ---------------------------------------------------------------------------

/// The shapes a bare join takes: the separator as a `String.concat` delimiter, a `+` splice around
/// it, or the `U+0001` literal (escaped or raw) used as a delimiter directly.
let private bareJoin =
    Regex(
        @"String\.concat\s+(Hash\.)?foldSep|\+\s*(Hash\.)?foldSep\s*\+|String\.concat\s+""\\u0001""|String\.concat\s+""\u0001""",
        RegexOptions.Compiled
    )

/// Files whose separator splices are ADVERSARIAL INPUTS rather than keys, and mint no key: the
/// parity-vector table builds strings that CONTAIN the separator on purpose, to pin what the
/// encoders do with them, and the conformance kit's function-law module builds the values a key
/// must survive (`memoLaws`' separator-in-value case, in `FunctionLaws.fs` since the Phase 297
/// split). Exempt from the bare-join scan only; the call-site scan above still reads the kit, so
/// a key minted there would still have to join the roster.
let private adversarialFixtures =
    [ "src/Fuaran.Core.Conformance/ParityVectors.fs"
      "src/Fuaran.Core.Conformance/FunctionLaws.fs" ]

/// The ONE bare join the scan knowingly carries, by file and count, so a second one in the same
/// file still fails: `Schema.fingerprint` (`Fuaran.Core.Column`) joins `name:type` cells on the
/// raw `U+0001` byte. It cannot call `Hash.canonicalFields` without a package edge from `Column`
/// to `Tree` that its own comment declines, it lives outside Phase 290's declared files, and its
/// bytes are pinned by the `hashSweep/*` parity rows — so it is NAMED here as residue for the
/// compute strand to resolve, not silently allowed. Remove this entry when it does.
let private knownBareJoins = [ "src/Fuaran.Core.Column/Column.fs", 1 ]

let private bareJoins () : string list =
    let hash = Path.GetFullPath(hashFile ())

    sourceFiles ()
    |> List.filter (fun (f, _) -> Path.GetFullPath f <> hash && not (List.contains (relOf f) adversarialFixtures))
    |> List.collect (fun (f, lines) ->
        let hits =
            lines
            |> Array.mapi (fun i l -> i, l)
            |> Array.filter (fun (_, l) -> bareJoin.IsMatch(l.Split("//").[0]))
            |> Array.map (fun (i, l) -> sprintf "%s:%d: %s" (relOf f) (i + 1) (l.Trim()))
            |> Array.toList

        match List.tryFind (fun (file, _) -> file = relOf f) knownBareJoins with
        | Some(_, allowed) when List.length hits = allowed -> []
        | _ -> hits)

[<Tests>]
let tests =
    testList
        "Hash.Roster"
        [ testCase "the roster is written where the encoding is, and is not empty"
          <| fun _ ->
              let r = roster ()
              Expect.isNonEmpty r "Hash.fs carries the `THE KEY ROSTER` list above canonicalFields"

              Expect.equal
                  (List.length (List.distinct r))
                  (List.length r)
                  (sprintf "a roster names each key once — got %A" r)

          testCase "every call site of canonicalFields under src/ is a definition the roster names"
          <| fun _ ->
              let r = roster () |> Set.ofList
              let found = callers ()

              let unlisted =
                  found |> Map.toList |> List.filter (fun (name, _) -> not (Set.contains name r))

              Expect.isEmpty
                  unlisted
                  (sprintf
                      "these definitions build a pre-image through Hash.canonicalFields and are not in the roster — a new key joins the list in Hash.fs, beside the encoding it uses:\n%s"
                      (unlisted
                       |> List.map (fun (n, w) -> sprintf "  %s (%s)" n w)
                       |> String.concat "\n"))

          testCase "every definition the roster names is found calling canonicalFields"
          <| fun _ ->
              let found = callers ()

              let missing =
                  roster () |> List.filter (fun name -> not (Map.containsKey name found))

              Expect.isEmpty
                  missing
                  (sprintf
                      "the roster names %A and no definition of that name under src/ calls Hash.canonicalFields — the key left the encoding, or the roster is stale (found: %A)"
                      missing
                      (found |> Map.toList |> List.map fst))

          testCase "no definition under src/ joins fields on the bare separator"
          <| fun _ ->
              let joins = bareJoins ()

              Expect.isEmpty
                  joins
                  (sprintf
                      "these lines join on the bare U+0001 separator, which a value can spell — build the pre-image through Hash.canonicalFields and add the key to the roster:\n%s"
                      (String.concat "\n" joins))

          testCase "the roster resolver reads a nested module and a local let correctly (go-red)"
          <| fun _ ->
              // The resolver's own teeth: a call inside a local `let` of a nested module's member
              // must resolve to the MEMBER, qualified outward — the shape `Tree.Index.fingerprintOf`
              // and `Function.memoKey`'s local `argCanon` both take.
              let sample =
                  [| "module Outer ="
                     ""
                     "    let unrelated (x: int) = x"
                     ""
                     "    module Inner ="
                     ""
                     "        let private key (xs: string list) : string ="
                     "            let canonical ="
                     "                xs |> Hash.canonicalFields"
                     ""
                     "            canonical" |]

              Expect.equal (enclosingDefinition sample 8) (Some "Outer.Inner.key") "nested module, local let"
              Expect.equal (enclosingDefinition sample 2) (Some "Outer.unrelated") "top-level member" ]
