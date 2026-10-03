module Fuaran.Core.Tests.DigestTests

// Phase 314 — Merkle digests, the change classification, the defect-set gate, and Projection
// reading the digests.
//
// Four consumers each kept a digest module and a change classifier of their own, and two kept a
// merge gate: a per-node self digest and a Merkle subtree digest with a four-way diff over the maps
// (the document and presentation domains), a per-id Added / Removed / Moved / KindChanged / Changed
// classifier (the presentation and UI tiers), and "the defects the candidate has that no parent
// has" under a Lenient / Diagnostic / Gated policy (the UI merge and the presentation `mergeChecked`).
// The fixtures below are those shapes, written against the reference domain, and each test says
// which consumer's answer it reproduces. The property side is `Conformance.digestLaws`,
// `changeLaws` and `introducedLaws`, run from `ConformanceVacuityTests`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// A node's own content as an encoder reads it: kind and value, never the children.
let encode (n: RNode) = n.Kind + "|" + n.Value

let private leaf id value = RNode.leaf id "para" value
let private section id kids = RNode.node id "section" kids

/// root(doc) › s1(section: p1 "a", p2 "b"), s2(section: p3 "c").
let private fixture =
    RNode.node
        "root"
        "doc"
        [ section "s1" [ leaf "p1" "a"; leaf "p2" "b" ]
          section "s2" [ leaf "p3" "c" ] ]

let private isSha256Hex (s: string) =
    s.Length = 64
    && s |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

let private keysOf (m: Map<string, string>) = m |> Map.toList |> List.map fst

let private kinds (cs: Diff.Change<string> list) = cs |> List.map (fun c -> c.Id, c.Kind)

// ---- the reference rules the gate runs (the `ValidatorTests` pair, made public for the laws) ----

/// `REF001` (warning): an empty paragraph.
let emptyPara =
    Validator.perNode "REF001" (fun w n ->
        if w.KindTag n = "para" && n.Value = "" then
            [ Defect.create "REF001" Severity.Warning "empty paragraph" (Some n.Id) ]
        else
            [])

/// `REF002` (error): a section with no children.
let emptySection =
    Validator.perNode "REF002" (fun w n ->
        if w.KindTag n = "section" && List.isEmpty (w.Children n) then
            [ Defect.create "REF002" Severity.Error "empty section" (Some n.Id) ]
        else
            [])

/// The reference registry the gate and `introducedLaws` run.
let registry =
    Validator.ofFamilies [ emptyPara; emptySection ]
    |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

[<Tests>]
let digestTests =
    testList
        "Tree.digests (Phase 314)"
        [ test "every node has an own, a frame and a subtree digest, each 64 lowercase hex — never the 32-bit fnv1a" {
              let d = Tree.digests nodew idw encode fixture
              let ids = Tree.ids nodew fixture |> List.sort
              Expect.equal (keysOf d.Own) ids "own keys"
              Expect.equal (keysOf d.Frame) ids "frame keys"
              Expect.equal (keysOf d.Subtree) ids "subtree keys"

              for m in [ d.Own; d.Frame; d.Subtree ] do
                  for KeyValue(_, v) in m do
                      Expect.isTrue (isSha256Hex v) (sprintf "a SHA-256 digest, not %s" v)
          }

          test "the maps are the per-node forms, and the Merkle digest is built as documented" {
              let d = Tree.digests nodew idw encode fixture

              for n in Tree.preorder nodew fixture do
                  Expect.equal (Map.find n.Id d.Own) (Tree.ownDigest nodew idw encode n) ("own " + n.Id)
                  Expect.equal (Map.find n.Id d.Frame) (Tree.frameDigest nodew idw encode n) ("frame " + n.Id)

              // a leaf's subtree digest is SHA-256 over its own digest alone; a parent's is over its
              // own digest then its children's subtree digests, in order
              let own id = Map.find id d.Own
              let sub id = Map.find id d.Subtree
              Expect.equal (sub "p1") (Hash.sha256Hex (Hash.canonicalFields [ own "p1" ])) "leaf"
              Expect.equal (sub "s1") (Hash.sha256Hex (Hash.canonicalFields [ own "s1"; sub "p1"; sub "p2" ])) "section"

              Expect.equal
                  (sub "root")
                  (Hash.sha256Hex (Hash.canonicalFields [ own "root"; sub "s1"; sub "s2" ]))
                  "root"

              // the own digest is over the id key, the kind tag and the encoded SHELL
              Expect.equal
                  (own "s1")
                  (Hash.sha256Hex (Hash.canonicalFields [ "s1"; "section"; encode (section "s1" []) ]))
                  "own pre-image"
          }

          test
              "a grandchild's edit moves the root's subtree digest and nothing else of the root's; a child reorder moves the frame" {
              let d = Tree.digests nodew idw encode fixture

              let edited =
                  Tree.map nodew (fun n -> if n.Id = "p1" then { n with Value = "A" } else n) fixture

              let e = Tree.digests nodew idw encode edited
              Expect.equal (Map.find "root" e.Own) (Map.find "root" d.Own) "root own unchanged"
              Expect.equal (Map.find "root" e.Frame) (Map.find "root" d.Frame) "root frame unchanged"
              Expect.notEqual (Map.find "root" e.Subtree) (Map.find "root" d.Subtree) "root subtree moved"
              Expect.notEqual (Map.find "s1" e.Subtree) (Map.find "s1" d.Subtree) "s1 subtree moved"
              Expect.equal (Map.find "s2" e.Subtree) (Map.find "s2" d.Subtree) "s2 subtree unchanged"
              Expect.notEqual (Map.find "p1" e.Own) (Map.find "p1" d.Own) "p1 own moved"

              let reordered =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p2" "b"; leaf "p1" "a" ]
                        section "s2" [ leaf "p3" "c" ] ]

              let r = Tree.digests nodew idw encode reordered
              Expect.equal (Map.find "s1" r.Own) (Map.find "s1" d.Own) "s1 own unchanged by a reorder"
              Expect.notEqual (Map.find "s1" r.Frame) (Map.find "s1" d.Frame) "s1 frame sees the reorder"
              Expect.notEqual (Map.find "s1" r.Subtree) (Map.find "s1" d.Subtree) "s1 subtree sees the reorder"
          }

          test "Digests.diff reproduces the document domain's four-way delta, every id in exactly one list, ascending" {
              // before: the fixture; after: p2 removed, p4 added under s2, p3's text changed, the rest kept
              let after =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p1" "a" ]
                        section "s2" [ leaf "p3" "C"; leaf "p4" "d" ] ]

              let delta =
                  Tree.Digests.diff (Tree.digests nodew idw encode fixture) (Tree.digests nodew idw encode after)

              Expect.equal delta.Added [ "p4" ] "added"
              Expect.equal delta.Removed [ "p2" ] "removed"

              Expect.equal
                  delta.Changed
                  [ "p3" ]
                  "changed: own content only — s1 and s2 lost or gained a child and are not here"

              Expect.equal delta.Unchanged [ "p1"; "root"; "s1"; "s2" ] "unchanged, ascending"
          }

          test
              "diff over one map is all Unchanged; the subtree-equal fast path reads a key Unchanged without its own digest" {
              let d = Tree.digests nodew idw encode fixture
              let same = Tree.Digests.diff d d
              Expect.isEmpty same.Added "added"
              Expect.isEmpty same.Removed "removed"
              Expect.isEmpty same.Changed "changed"
              Expect.equal same.Unchanged (Tree.ids nodew fixture |> List.sort) "every key"
              Expect.isTrue (Tree.Digests.subtreeEqual d d "s1") "a subtree agrees with itself"

              // a map whose own digests were tampered but whose subtree digests agree: the fast path
              // answers Unchanged — subtree agreement is the stronger fact, so the own digest is not read
              let tampered =
                  { d with
                      Own = d.Own |> Map.add "p1" "tampered" }

              Expect.equal (Tree.Digests.diff d tampered).Changed [] "subtree-equal keys are never Changed"

              Expect.isFalse
                  (Tree.Digests.subtreeEqual d (Tree.digests nodew idw encode (leaf "x" "")) "s1")
                  "absent on one side"
          }

          test "an id carried twice keeps its LAST preorder occurrence, as Tree.Index.build does" {
              let twice = RNode.node "root" "doc" [ leaf "p" "first"; leaf "p" "second" ]
              let d = Tree.digests nodew idw encode twice
              Expect.equal (Map.find "p" d.Own) (Tree.ownDigest nodew idw encode (leaf "p" "second")) "the last wins"
          } ]

[<Tests>]
let changeTests =
    testList
        "Diff.changes (Phase 314)"
        [ test "the presentation and UI classifiers' kinds, one pass, in canonical order" {
              // s1 reordered (p2 before p1); p3 moved from s2 to s1; p1's text changed; p2's kind
              // changed; p4 added under s2; s2's child p3 left; nothing removed
              let after =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ RNode.leaf "p2" "aside" "b"; leaf "p1" "A"; leaf "p3" "c" ]
                        section "s2" [ leaf "p4" "d" ] ]

              let cs =
                  Diff.changes encode nodew idw fixture after
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              Expect.equal
                  (kinds cs)
                  [ "p1", Diff.ChangeKind.Changed
                    "p2", Diff.ChangeKind.KindChanged("para", "aside")
                    "p3", Diff.ChangeKind.Moved("s2", "s1")
                    "p4", Diff.ChangeKind.Added
                    "s1", Diff.ChangeKind.Reordered ]
                  "the classification"
          }

          test "a removed region names every id in it, and a survivor that moved and changed carries both entries" {
              let after =
                  RNode.node "root" "doc" [ section "s2" [ leaf "p3" "c"; leaf "p1" "A" ] ]

              let cs =
                  Diff.changes encode nodew idw fixture after
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              Expect.equal
                  (kinds cs)
                  [ "p1", Diff.ChangeKind.Moved("s1", "s2")
                    "p1", Diff.ChangeKind.Changed
                    "p2", Diff.ChangeKind.Removed
                    "s1", Diff.ChangeKind.Removed ]
                  "p2 is below the removed s1 and is Removed in its own right; p1 moved AND changed"
          }

          test
              "a sibling shifting position because a neighbour left is NOT a reorder; kept children changing relative order IS" {
              let shifted =
                  RNode.node "root" "doc" [ section "s1" [ leaf "p2" "b" ]; section "s2" [ leaf "p3" "c" ] ]

              Expect.equal
                  (Diff.changes encode nodew idw fixture shifted |> Result.map kinds)
                  (Ok [ "p1", Diff.ChangeKind.Removed ])
                  "p2 moved from index 1 to 0 under one parent, and that is p1's removal, not a reorder"

              let swapped =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p2" "b"; leaf "p1" "a" ]
                        section "s2" [ leaf "p3" "c" ] ]

              Expect.equal
                  (Diff.changes encode nodew idw fixture swapped |> Result.map kinds)
                  (Ok [ "s1", Diff.ChangeKind.Reordered ])
                  "the kept children stand in a new order: the parent is Reordered, the children are not Moved"
          }

          test "the ids the classification names are the ids toOpsWith's script targets" {
              let after =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p0" "z"; leaf "p1" "a"; leaf "p3" "c" ]
                        section "s2" [ leaf "p5" "e" ] ]

              let cs =
                  Diff.changes encode nodew idw fixture after
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let ops =
                  Diff.toOpsWith encode nodew idw fixture after
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let named k =
                  cs |> List.filter (fun c -> c.Kind = k) |> List.map _.Id |> Set.ofList

              Expect.equal
                  (named Diff.ChangeKind.Added)
                  (ops
                   |> List.choose (function
                       | InsertChild(_, n) -> Some n.Id
                       | _ -> None)
                   |> Set.ofList)
                  "Added = grafts"

              Expect.equal
                  (named (Diff.ChangeKind.Moved("s2", "s1")))
                  (ops
                   |> List.choose (function
                       | MoveNode(t, _) -> Some t
                       | _ -> None)
                   |> Set.ofList)
                  "Moved = MoveNode targets"

              Expect.equal (named Diff.ChangeKind.Removed) (set [ "p2" ]) "Removed = before minus after"

              // s1 gained p0 at the FRONT: the structural passes append, so the script restates s1's
              // order with a ReorderChildren although s1's kept children (p1) kept their order — the
              // reorder is explained by the Added child, and s1 is not Reordered
              Expect.isTrue
                  (ops
                   |> List.exists (function
                       | ReorderChildren("s1", _) -> true
                       | _ -> false))
                  "the script reorders s1"

              Expect.isFalse (cs |> List.exists (fun c -> c.Kind = Diff.ChangeKind.Reordered)) "nothing is Reordered"
          }

          test "the identity pair classifies to []; the refusals are toOps's, in its order" {
              Expect.equal (Diff.changes encode nodew idw fixture fixture) (Ok []) "identity"

              Expect.equal
                  (Diff.changes encode nodew idw fixture (RNode.node "other" "doc" []))
                  (Error(Diff.RootIdMismatch("root", "other")))
                  "root mismatch first"

              let dup = RNode.node "root" "doc" [ leaf "p" ""; leaf "p" "" ]

              Expect.equal
                  (Diff.changes encode nodew idw dup fixture)
                  (Error(Diff.DuplicateIdInTree "p"))
                  "before's repeat"

              Expect.equal
                  (Diff.changes encode nodew idw fixture dup)
                  (Error(Diff.DuplicateIdInTree "p"))
                  "after's repeat"
          } ]

[<Tests>]
let introducedTests =
    let defectKeys (ds: Defect<string> list) =
        ds |> List.map (fun d -> d.Code, d.Node)

    testList
        "Validator.introduced (Phase 314)"
        [ test
              "the UI merge's introduced-defect set: present in the merged tree, absent from BOTH parents, carried-through defects never flagged" {
              // parent A already has an empty section (s2 emptied); parent B is clean; the merge
              // empties s1 as well — only s1's error is introduced
              let parentA =
                  RNode.node "root" "doc" [ section "s1" [ leaf "p1" "a" ]; section "s2" [] ]

              let parentB = fixture
              let merged = RNode.node "root" "doc" [ section "s1" []; section "s2" [] ]

              Expect.equal
                  (Validator.introduced nodew idw registry [ parentA; parentB ] merged
                   |> defectKeys)
                  [ "REF002", Some "s1" ]
                  "s2's error was already in parent A"

              Expect.equal (Validator.runAll nodew registry merged |> List.length) 2 "the merged tree has both"

              Expect.isEmpty
                  (Validator.introduced nodew idw registry [ merged ] merged)
                  "a tree introduces nothing against itself"

              Expect.equal
                  (Validator.introduced nodew idw registry [] merged |> defectKeys)
                  [ "REF002", Some "s1"; "REF002", Some "s2" ]
                  "no baseline introduces everything, in (code, node) order"
          }

          test
              "the presentation merge's three policies: Lenient ignores, Diagnostic reports, Gated blocks on an error and never on a warning" {
              let warningOnly =
                  [ Defect.create "REF001" Severity.Warning "empty paragraph" (Some "p1") ]

              let withError =
                  Defect.create "REF002" Severity.Error "empty section" (Some "s1") :: warningOnly

              let lenient = Validator.verdict Validator.GatePolicy.Lenient withError
              Expect.isEmpty lenient.Introduced "lenient reports nothing"
              Expect.isFalse lenient.Blocked "lenient never blocks"

              let diagnostic = Validator.verdict Validator.GatePolicy.Diagnostic withError
              Expect.equal diagnostic.Introduced withError "diagnostic reports"
              Expect.isFalse diagnostic.Blocked "diagnostic never blocks"

              Expect.isTrue (Validator.verdict Validator.GatePolicy.Gated withError).Blocked "gated blocks on an error"
              Expect.isFalse (Validator.verdict Validator.GatePolicy.Gated warningOnly).Blocked "a warning never blocks"
              Expect.isFalse (Validator.verdict Validator.GatePolicy.Gated []).Blocked "nothing introduced never blocks"
          }

          test "gate composes the two, and Lenient consults no validator" {
              let candidate =
                  RNode.node "root" "doc" [ section "s1" []; section "s2" [ leaf "p3" "c" ] ]

              let throwing = Validator.perNode "BOOM" (fun _ _ -> failwith "the validator ran")

              let reg =
                  Validator.ofFamilies [ emptySection; throwing ]
                  |> Result.defaultWith (fun e -> failwithf "%A" e)

              let gated =
                  Validator.gate Validator.GatePolicy.Gated nodew idw registry [ fixture ] candidate

              Expect.isTrue gated.Blocked "s1 was emptied"
              Expect.equal (gated.Introduced |> defectKeys) [ "REF002", Some "s1" ] "the introduced error"

              Expect.equal
                  gated
                  (Validator.verdict
                      Validator.GatePolicy.Gated
                      (Validator.introduced nodew idw registry [ fixture ] candidate))
                  "gate = verdict over introduced"

              // a throwing family is a RULE-FAULT error in the baseline AND the candidate, so it is
              // carried through, never introduced; the block is s1's error. Lenient never runs it.
              let faulting =
                  Validator.gate Validator.GatePolicy.Gated nodew idw reg [ fixture ] candidate

              Expect.isTrue faulting.Blocked "blocked by REF002 at s1"

              Expect.equal
                  (faulting.Introduced |> defectKeys)
                  [ "REF002", Some "s1" ]
                  "the fault is in both and carried through"

              Expect.equal
                  (Validator.gate Validator.GatePolicy.Lenient nodew idw reg [ fixture ] candidate).Introduced
                  []
                  "lenient: not run"
          }

          test
              "the verdict encoding is canonical: the policy, the block, then each defect's code, location and severity" {
              let a = Defect.create "REF002" Severity.Error "empty section" (Some "s1")
              let b = Defect.create "REF001" Severity.Warning "empty paragraph" (Some "p1")
              let whole = Defect.create "RULE-FAULT" Severity.Error "a rule threw" None
              let v = Validator.verdict Validator.GatePolicy.Gated [ a; b; whole ]

              Expect.equal
                  (Validator.encodeVerdict idw v)
                  (Hash.canonicalFields
                      [ "gated"
                        "blocked"
                        "REF001"
                        "node"
                        "p1"
                        "warning"
                        "REF002"
                        "node"
                        "s1"
                        "error"
                        "RULE-FAULT"
                        "subject"
                        ""
                        "error" ])
                  "the bytes"

              Expect.equal
                  (Validator.encodeVerdict idw { v with Introduced = [ whole; b; a ] })
                  (Validator.encodeVerdict idw v)
                  "order-free"

              Expect.notEqual
                  (Validator.encodeVerdict idw (Validator.verdict Validator.GatePolicy.Diagnostic [ a; b; whole ]))
                  (Validator.encodeVerdict idw v)
                  "the policy is in it"

              Expect.isTrue (isSha256Hex (Hash.sha256Hex (Validator.encodeVerdict idw v))) "the verdict hash"
          } ]

[<Tests>]
let projectionTests =
    let pw = ProjectionTests.pw

    testList
        "Projection reads the digests (Phase 314)"
        [ test
              "the snapshot digest is Phase 298's bytes — id, kind, Encode, child count, child ids — now read from Tree.frameDigest" {
              for n in Tree.preorder nodew fixture do
                  let kids = n.Children

                  let phase298 =
                      n.Id
                      :: n.Kind
                      :: pw.Encode n
                      :: string (List.length kids)
                      :: (kids |> List.map _.Id)
                      |> Hash.canonicalFields
                      |> Hash.sha256Hex

                  Expect.equal (Projection.snapshotDigestOf pw n) phase298 ("the 298 bytes at " + n.Id)

                  Expect.equal
                      (Projection.snapshotDigestOf pw n)
                      (Tree.frameDigest nodew idw pw.Encode n)
                      "one definition"

              let snap = Projection.snapshot pw fixture
              let d = Tree.digests nodew idw pw.Encode fixture
              Expect.equal snap.Digests d.Frame "the Frame map (plain ids escape to themselves)"
              Expect.equal snap.Subtrees d.Subtree "the Merkle map"
          }

          test "ChangedSince reports a reorder and a move — the parents whose child order or membership moved" {
              let snap = Projection.snapshot pw fixture

              let reordered =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p2" "b"; leaf "p1" "a" ]
                        section "s2" [ leaf "p3" "c" ] ]

              Expect.equal
                  ((Projection.project pw (Scope.ChangedSince snap) reordered).Lines
                   |> List.map _.IdKey)
                  [ "s1" ]
                  "a reorder names the parent"

              let moved =
                  RNode.node
                      "root"
                      "doc"
                      [ section "s1" [ leaf "p1" "a"; leaf "p2" "b"; leaf "p3" "c" ]
                        section "s2" [] ]

              Expect.equal
                  ((Projection.project pw (Scope.ChangedSince snap) moved).Lines
                   |> List.map _.IdKey)
                  [ "s1"; "s2" ]
                  "a move names the parent left and the parent joined"

              Expect.isEmpty (Projection.project pw (Scope.ChangedSince snap) fixture).Lines "nothing changed"
          }

          test
              "the subtree-equal fast path skips an unchanged subtree whole; a snapshot without Subtrees reads every node" {
              let calls = ref 0

              let counting =
                  { pw with
                      Encode =
                          fun n ->
                              calls.Value <- calls.Value + 1
                              pw.Encode n }

              let snap = Projection.snapshot counting fixture

              let edited =
                  Tree.map nodew (fun n -> if n.Id = "p3" then { n with Value = "C" } else n) fixture

              let n = Tree.count nodew fixture
              calls.Value <- 0
              let withFastPath = Projection.project counting (Scope.ChangedSince snap) edited
              let fast = calls.Value
              calls.Value <- 0

              let without =
                  Projection.project counting (Scope.ChangedSince { snap with Subtrees = Map.empty }) edited

              let slow = calls.Value
              Expect.equal (withFastPath.Lines |> List.map _.IdKey) [ "p3" ] "the edited node"
              Expect.equal without.Lines withFastPath.Lines "the same answer either way"
              // one digests pass reads every node; the walk then re-reads only the nodes it visits —
              // root, s2 and p3 (s1's subtree is skipped) against every node without the fast path;
              // and rendering the one reported line encodes its node once more (`lineOf`)
              Expect.equal slow (2 * n + 1) "without: every node digested twice, plus the rendered line"

              Expect.equal
                  fast
                  (n + 3 + 1)
                  "with: the digests pass, the three nodes on the changed path, the rendered line"
          }

          test "BySubtreeDigest reads the subtree a Merkle digest names, and nothing for a digest no node has" {
              let d = Tree.digests nodew idw pw.Encode fixture

              let byDigest =
                  Projection.project pw (Scope.BySubtreeDigest(Map.find "s1" d.Subtree)) fixture

              Expect.equal
                  byDigest
                  (Projection.project pw (Scope.Subtree "s1") fixture)
                  "the Subtree slice, addressed by content"

              Expect.equal (byDigest.Lines |> List.map _.IdKey) [ "s1"; "p1"; "p2" ] "s1 and its descendants"

              Expect.isEmpty
                  (Projection.project pw (Scope.BySubtreeDigest(String.replicate 64 "0")) fixture).Lines
                  "unknown digest"
          } ]
