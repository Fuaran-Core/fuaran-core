module Fuaran.Core.Tests.ProjectionTests

// Phase 58 — the projection seam: witness + scoped reads + parseBack, self-proven
// against the in-repo reference domain via `Conformance.projectionLaws`, plus a
// deliberately-broken witness whose failure is reproduced from a seed, and the
// GP6 content-free check (the core contributes only structural glue to a line).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// ---- the reference projection op: one line re-imports as one upsert ----

type RProjOp = Upsert of depth: int * id: string * kind: string * value: string

// ---- the reference projection witness ----

/// The per-node content encoder (no children): kind + value, `Hash.foldSep`-separated.
/// Injective over the reference node space the generator draws from.
let private rEncode (n: RNode) : string = n.Kind + Hash.foldSep + n.Value

/// One rendered line back to an upsert: `<indent><id> <kind> #<digest> [<value>]`.
let private rParseBack (line: string) : Result<RProjOp list, string> =
    let cell = line.TrimStart(' ')
    let indent = line.Length - cell.Length

    if indent % 2 <> 0 then
        Error(sprintf "odd indent in projection line: %s" line)
    else
        match cell.Split([| ' ' |], 4) with
        | [| id; kind; digest |] when digest.StartsWith "#" -> Ok [ Upsert(indent / 2, id, kind, "") ]
        | [| id; kind; digest; value |] when digest.StartsWith "#" -> Ok [ Upsert(indent / 2, id, kind, value) ]
        | _ -> Error(sprintf "unparseable projection line: %s" line)

let private pw: ProjectionWitness<RNode, string, RProjOp> =
    { Tree = nodew
      IdW = idw
      Encode = rEncode
      Snippet = _.Value
      ParseBack = rParseBack }

// ---- the reference re-import: preorder upserts (depth-threaded) back to a tree ----

let private applyOps (ops: RProjOp list) : Result<RNode, string> =
    let mk id kind value = RNode.leaf id kind value

    // stack of open frames, deepest first: (depth, node, children-reversed)
    let attach (stack: (int * RNode * RNode list) list) =
        match stack with
        | (_, n, kids) :: (pd, p, pkids) :: tl -> (pd, p, { n with Children = List.rev kids } :: pkids) :: tl
        | other -> other

    let rec collapse (stack: (int * RNode * RNode list) list) =
        match stack with
        | [ (_, n, kids) ] -> Ok { n with Children = List.rev kids }
        | [] -> Error "empty projection re-imports to no tree"
        | _ -> collapse (attach stack)

    let rec go stack ops =
        match ops with
        | [] -> collapse stack
        | Upsert(d, id, kind, value) :: tl ->
            let rec popTo st =
                match st with
                | (td, _, _) :: _ when td >= d -> popTo (attach st)
                | _ -> st

            match popTo stack with
            | (td, _, _) :: _ as st when td = d - 1 -> go ((d, mk id kind value, []) :: st) tl
            | _ -> Error(sprintf "projection line for %s has no parent at depth %d" id (d - 1))

    match ops with
    | Upsert(0, id, kind, value) :: rest -> go [ (0, mk id kind value, []) ] rest
    | _ -> Error "projection does not start at the root"

// ---- a random tree generator (values vary so the digest-iff law has bite) ----

let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "n%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()
        let v, r' = ConfRng.intBelow 5 r
        r <- r'

        if depth <= 0 then
            RNode.leaf id "para" (sprintf "v%d" v)
        else
            let nKids, r'' = ConfRng.intBelow 3 r
            r <- r''
            let kids = [ for _ in 1..nKids -> build (depth - 1) ]

            { RNode.node id "section" kids with
                Value = sprintf "s%d" v }

    let t = build 2
    t, r

// ---- the wire baseline for the compactness floor ----

let rec private wireEncode (n: RNode) : string =
    sprintf
        "{\"id\":\"%s\",\"kind\":\"%s\",\"value\":\"%s\",\"children\":[%s]}"
        n.Id
        n.Kind
        n.Value
        (n.Children |> List.map wireEncode |> String.concat ",")

// ---- a known fixture ----

let private fixture =
    RNode.node
        "root"
        "doc"
        [ RNode.node "s1" "section" [ RNode.leaf "p1" "para" "alpha"; RNode.leaf "p2" "para" "beta" ]
          RNode.leaf "p3" "para" "gamma" ]

[<Tests>]
let tests =
    testList
        "Projection (Phase 58)"
        [ test "projectionLaws certify the reference witness green" {
              let report = Conformance.projectionLaws pw applyOps wireEncode genTree 42 200

              for r in report do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)
          }

          test "ById projects exactly the one line" {
              let p = Projection.project pw (ById "p2") fixture

              match p.Lines with
              | [ l ] ->
                  Expect.equal l.IdKey "p2" "id"
                  Expect.equal l.Kind "para" "kind"
                  Expect.equal l.Snippet "beta" "snippet"
                  Expect.equal l.Depth 2 "absolute depth is preserved"
              | other -> failtestf "expected one line, got %d" (List.length other)
          }

          test "ById of an absent id is the empty projection (total, not an error)" {
              Expect.equal (Projection.project pw (ById "nope") fixture).Lines [] "empty"
          }

          test "Subtree projects the contiguous preorder slice at absolute depth" {
              let p = Projection.project pw (Subtree "s1") fixture
              Expect.equal (p.Lines |> List.map _.IdKey) [ "s1"; "p1"; "p2" ] "the subtree ids, preorder"
              Expect.equal (p.Lines |> List.map _.Depth) [ 1; 2; 2 ] "absolute depths"

              let whole = Projection.project pw Whole fixture |> Projection.render
              let scoped = Projection.render p

              for line in scoped.Split('\n') do
                  Expect.isTrue (whole.Contains line) "every scoped rendered line appears in the whole"
          }

          test "ChangedSince returns exactly the edited node's line" {
              let snap = Projection.snapshot pw fixture

              let edited =
                  Tree.map nodew (fun n -> if n.Id = "p1" then { n with Value = "ALPHA" } else n) fixture

              let p = Projection.project pw (ChangedSince snap) edited
              Expect.equal (p.Lines |> List.map _.IdKey) [ "p1" ] "only the edited node"
              Expect.equal (Projection.project pw (ChangedSince snap) fixture).Lines [] "unchanged tree ⇒ empty"
          }

          test "a structural move re-indents a line without changing its content cell" {
              let before = Projection.project pw Whole fixture

              // relocate p3 under s1: identical node contents everywhere, different shape
              let moved =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node
                            "s1"
                            "section"
                            [ RNode.leaf "p1" "para" "alpha"
                              RNode.leaf "p2" "para" "beta"
                              RNode.leaf "p3" "para" "gamma" ] ]

              let after = Projection.project pw Whole moved

              let cellOf id (p: Projection) =
                  p.Lines
                  |> List.tryFind (fun l -> l.IdKey = id)
                  |> Option.map Projection.lineText

              Expect.equal (cellOf "p3" after) (cellOf "p3" before) "p3's content cell is unchanged by the move"
          }

          test "the seam carries no domain content — a rendered line is witness cells + structural glue only (GP6)" {
              for node, depth in [ fixture, 0; fixture.Children.Head, 1; fixture.Children.Head.Children.Head, 2 ] do
                  let l = Projection.lineOf pw depth node

                  let expected =
                      String.replicate (depth * 2) " "
                      + idw.ToString(nodew.Id node)
                      + " "
                      + nodew.KindTag node
                      + " #"
                      + Hash.fnv1a (
                          idw.ToString(nodew.Id node)
                          + Hash.foldSep
                          + nodew.KindTag node
                          + Hash.foldSep
                          + rEncode node
                      )
                      + (if node.Value = "" then "" else " " + node.Value)

                  Expect.equal (Projection.renderLine l) expected "the core added nothing beyond indent / spaces / '#'"
          }

          test "the projection is strictly smaller than the wire form on the fixture" {
              let p = Projection.project pw Whole fixture
              Expect.isLessThan (Projection.sizeOf p) (String.length (wireEncode fixture)) "compactness floor"
          }

          test "a broken witness fails the laws with a seed-reproducible counterexample" {
              // a parseBack that drops the value: the re-imported tree projects differently
              let broken =
                  { pw with
                      ParseBack =
                          fun line ->
                              rParseBack line
                              |> Result.map (List.map (fun (Upsert(d, id, kind, _)) -> Upsert(d, id, kind, ""))) }

              let report = Conformance.projectionLaws broken applyOps wireEncode genTree 42 200
              let rt = report |> List.find (fun r -> r.Law.Contains "round-trip")
              Expect.isFalse rt.Passed "the round-trip law catches the lossy parseBack"
              Expect.isSome rt.Counterexample "with a reproducible seed+iteration counterexample"

              let rerun = Conformance.projectionLaws broken applyOps wireEncode genTree 42 200

              Expect.equal
                  (rerun |> List.find (fun r -> r.Law.Contains "round-trip")).Counterexample
                  rt.Counterexample
                  "the same seed reproduces the same counterexample"
          } ]
