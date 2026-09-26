namespace Fuaran.Core.Tests

// ============================================================================
//  The `apply/` conformance family — Phase 139.
//
//  WHAT WAS MISSING. `nodes/`, `ops/`, `reject/` and `lenient/` in the shared
//  corpus are DECODE fixtures: every one of them says "these bytes decode to
//  that value", and every `reject/*` is a decode refusal. Nothing anywhere said
//  "this op applied to this tree is ACCEPTED with this result" or "is REFUSED
//  with this class" — so each host certified its own apply semantics against its
//  own tests, and agreement between them was a claim nothing could falsify.
//
//  WHAT THIS FAMILY IS. One vector per validator clause per skeleton op, plus
//  `Batch` atomicity and the Phase 137 collision cases: an authored `(tree, op)`
//  pair, and the outcome computed by CALLING `Ops.apply` — never by restating
//  what it ought to answer. An accept carries the result's canonical bytes and
//  their SHA-256; a reject carries the rejection CLASS and, per host, the code
//  that host raises for the same clause.
//
//  ── three decisions worth reading before changing anything here ────────────
//
//  1. THE ENVELOPE IS THE WITNESS SURFACE, and nothing else. A vector's tree is
//     `{"children":[…],"id":…,"kind":…}` — exactly what `NodeWitness` exposes
//     (`Id`, `KindTag`, `Children`), because the skeleton ops are STRUCTURAL
//     ops and structure is all they read — `UpdateNode` (Phase 250) included,
//     whose vectors rewrite a node's `kind`, the one piece of content the
//     witness surface shows. No domain payload appears, so a host
//     with an entirely different node vocabulary can run the family by mapping
//     three members. That is what makes an apply family expressible at all in a
//     core that owns no node type.
//
//  2. THE FAMILY IS SELF-ENUMERATED — its own `apply/manifest.json`, deliberately
//     NOT indexed by the corpus root `manifest.json`. That is the shape `laws/`,
//     `dag/`, `merge-conformance/`, `devtools-relay/`, `decode-policy/` and
//     `sanitization/` already use, and the root manifest indexes the canonical
//     wire-format CODEC families only. It is also the only shape that does not
//     break a host on arrival: the root manifest's fixture list is read as a
//     whole by at least one host's certification kit, which reads every listed
//     `inputFile` and asserts that its per-kind leg tallies account for exactly
//     the manifest's fixture count. A new kind there is a red build in a
//     repository that has adopted nothing.
//
//  3. NO `kitVersion` STAMP, and that is a correction rather than an omission.
//     `laws/transform-laws.json` stamps the producing kit's version and is then
//     byte-compared whole, so ANY `<Version>` move reddens its freshness leg
//     until the corpus is re-emitted — including a draft-slot cut, which is made
//     once and ridden by several phases landing days apart. This file is not a
//     sample of one kit's answers; it is a specification of apply semantics, and
//     its answers are RECOMPUTED by the F# leg on every run, which is a stronger
//     guarantee than a stamp. A version-independent artefact cannot go stale for
//     a reason that has nothing to do with its content.
// ============================================================================

module ApplyVectorExport =

    open System.IO
    open System.Text
    open Fuaran.Core
    open Fuaran.Core.Tests.Reference

    /// The family directory inside the shared corpus and the two artefacts in it. The directory
    /// name is the interface — a host resolves `apply/` — so it is named once here.
    let familyDirName = "apply"
    let vectorsFileName = "skeleton-apply.json"
    let manifestFileName = "manifest.json"

    /// The family id a host reports by name while it is `proposed`.
    let familyId = "skeletonApply"

    /// The hosts the mapping below is stated for, in the corpus's own roster order.
    let hostRoster = [ "fuaran-ts"; "fuaran-py"; "fuaran-go"; "fuaran-rs" ]

    // -----------------------------------------------------------------------
    //  the host-neutral skeleton envelope
    // -----------------------------------------------------------------------

    /// A tree, as the witness surface sees it. `Canon.render` sorts object keys ordinally, so the
    /// member order is `children`, `id`, `kind` and is not a choice this module makes.
    let rec encodeTreeJ (n: RNode) : JVal =
        JObj
            [ "id", JStr n.Id
              "kind", JStr n.Kind
              "children", JArr(n.Children |> List.map encodeTreeJ) ]

    let encodeTree (n: RNode) : string = Canon.render (encodeTreeJ n)

    /// The skeleton ops, `$type`-discriminated on the corpus's own convention (`$type` sorts before
    /// every lower-case member, so it is always first).
    let rec encodeOpJ (op: SkeletonOp<RNode, string>) : JVal =
        match op with
        | InsertChild(parent, node) -> Canon.typed "insertChild" [ "parent", JStr parent; "node", encodeTreeJ node ]
        | RemoveNode target -> Canon.typed "removeNode" [ "target", JStr target ]
        | MoveNode(target, newParent) -> Canon.typed "moveNode" [ "target", JStr target; "newParent", JStr newParent ]
        | ReorderChildren(parent, order) ->
            Canon.typed "reorderChildren" [ "parent", JStr parent; "order", JArr(order |> List.map JStr) ]
        | Batch ops -> Canon.typed "batch" [ "ops", JArr(ops |> List.map encodeOpJ) ]
        | UpdateNode node -> Canon.typed "updateNode" [ "node", encodeTreeJ node ]

    let encodeOp (op: SkeletonOp<RNode, string>) : string = Canon.render (encodeOpJ op)

    // ---- the decoders, so a vector is read back the way a host reads it ----

    let private field (name: string) (v: JVal) : JVal option =
        match v with
        | JObj ms -> ms |> List.tryPick (fun (k, x) -> if k = name then Some x else None)
        | _ -> None

    let private str (name: string) (v: JVal) : string option =
        match field name v with
        | Some(JStr s) -> Some s
        | _ -> None

    let rec private decodeTreeJ (v: JVal) : Result<RNode, string> =
        match str "id" v, str "kind" v, field "children" v with
        | Some id, Some kind, Some(JArr cs) ->
            let decoded = cs |> List.map decodeTreeJ

            match
                decoded
                |> List.tryPick (function
                    | Error m -> Some m
                    | Ok _ -> None)
            with
            | Some m -> Error m
            | None ->
                Ok(
                    RNode.node
                        id
                        kind
                        [ for d in decoded do
                              match d with
                              | Ok n -> yield n
                              | Error _ -> () ]
                )
        | _ -> Error "a tree node needs string `id` and `kind` members and a `children` array"

    let decodeTree (json: string) : Result<RNode, string> =
        Json.parse json |> Result.bind decodeTreeJ

    let rec private decodeOpJ (v: JVal) : Result<SkeletonOp<RNode, string>, string> =
        match str "$type" v with
        | Some "insertChild" ->
            match str "parent" v, field "node" v with
            | Some p, Some n -> decodeTreeJ n |> Result.map (fun node -> InsertChild(p, node))
            | _ -> Error "insertChild needs `parent` and `node`"
        | Some "removeNode" ->
            match str "target" v with
            | Some t -> Ok(RemoveNode t)
            | None -> Error "removeNode needs `target`"
        | Some "moveNode" ->
            match str "target" v, str "newParent" v with
            | Some t, Some np -> Ok(MoveNode(t, np))
            | _ -> Error "moveNode needs `target` and `newParent`"
        | Some "reorderChildren" ->
            match str "parent" v, field "order" v with
            | Some p, Some(JArr xs) ->
                let ids =
                    xs
                    |> List.choose (function
                        | JStr s -> Some s
                        | _ -> None)

                if List.length ids = List.length xs then
                    Ok(ReorderChildren(p, ids))
                else
                    Error "reorderChildren's `order` must be an array of strings"
            | _ -> Error "reorderChildren needs `parent` and an `order` array"
        | Some "batch" ->
            match field "ops" v with
            | Some(JArr xs) ->
                let decoded = xs |> List.map decodeOpJ

                match
                    decoded
                    |> List.tryPick (function
                        | Error m -> Some m
                        | Ok _ -> None)
                with
                | Some m -> Error m
                | None ->
                    Ok(
                        Batch
                            [ for d in decoded do
                                  match d with
                                  | Ok o -> yield o
                                  | Error _ -> () ]
                    )
            | _ -> Error "batch needs an `ops` array"
        | Some "updateNode" ->
            match field "node" v with
            | Some n -> decodeTreeJ n |> Result.map UpdateNode
            | None -> Error "updateNode needs `node`"
        | Some other -> Error("unknown op `" + other + "`")
        | None -> Error "an op needs a string `$type` member"

    let decodeOp (json: string) : Result<SkeletonOp<RNode, string>, string> =
        Json.parse json |> Result.bind decodeOpJ

    // -----------------------------------------------------------------------
    //  the rejection projection
    // -----------------------------------------------------------------------

    /// A rejection as the family pins it: the CLASS, and the one address the class is about.
    ///
    /// Two members of the envelope are deliberately NOT carried. `UnknownNode.addressable`
    /// enumerates every id in the tree — it is orchestrator guidance rather than a classification,
    /// and no other host carries it at all. `ReorderMismatch`'s two orders are likewise the
    /// evidence for the class, not the class. Pinning either would ask a host to agree about
    /// something the contract has never claimed, which is the failure mode the transform-parity
    /// family's deliberately-unnamed refusal already records.
    let rejectionJ (r: Rejection<string>) : JVal =
        match r with
        | DuplicateId d -> Canon.typed "duplicateId" [ "id", JStr d ]
        | UnknownNode(target, _) -> Canon.typed "unknownNode" [ "target", JStr target ]
        | CannotRemoveRoot -> Canon.typed "cannotRemoveRoot" []
        | WouldNestUnderSelf t -> Canon.typed "wouldNestUnderSelf" [ "target", JStr t ]
        | NotAContainer(target, kindTag) ->
            Canon.typed "notAContainer" [ "target", JStr target; "kindTag", JStr kindTag ]
        | ReorderMismatch(parent, _, _) -> Canon.typed "reorderMismatch" [ "parent", JStr parent ]
        | Rejected(code, _) -> Canon.typed "rejected" [ "code", JStr code ]

    // -----------------------------------------------------------------------
    //  the authored cases
    // -----------------------------------------------------------------------

    /// `root(doc)[ a(section)[a1,a2], b(section)[b1] ]` — every node built through `RNode.node`, so
    /// the envelope carries the whole of it and a decoded vector is the tree the emitter encoded.
    let private baseTree () : RNode =
        RNode.node
            "root"
            "doc"
            [ RNode.node "a" "section" [ RNode.node "a1" "para" []; RNode.node "a2" "para" [] ]
              RNode.node "b" "section" [ RNode.node "b1" "para" [] ] ]

    /// The per-host code for a reject vector's clause, or `None` where no host raises one.
    ///
    /// **These are read off each host's source, never inferred from the shape of Core's own
    /// envelope.** All four non-reference hosts carry the same twelve-code `ApplyErrorCode`
    /// vocabulary, so the mapping is uniform; the two places it is NOT a translation are stated on
    /// the vectors that need them.
    type private Hosts =
        /// Every host in the roster raises this code for this clause.
        | All of string
        /// No host raises anything — Core refuses where they accept, or wraps differently.
        | NoneWith of note: string

    /// One authored case, before its expectation is computed.
    type private Case =
        { Id: string
          Op: SkeletonOp<RNode, string>
          Tree: RNode
          Hosts: Hosts
          Description: string }

    let private nodeOf id kind children = RNode.node id kind children

    let private cases () : Case list =
        let t = baseTree ()

        let case id op hosts description =
            { Id = id
              Op = op
              Tree = t
              Hosts = hosts
              Description = description }

        [
          // ---- InsertChild ----
          case
              "insert-accept-leaf"
              (InsertChild("b", nodeOf "n1" "para" []))
              (NoneWith "accept")
              "InsertChild APPENDS a fresh leaf to the named parent's child list."
          case
              "insert-accept-subtree"
              (InsertChild("root", nodeOf "s1" "section" [ nodeOf "s1a" "para" [] ]))
              (NoneWith "accept")
              "A whole subtree grafts in one op; every id in it is fresh."
          case
              "insert-reject-duplicate-own-id"
              (InsertChild("b", nodeOf "a1" "para" []))
              (All "DuplicateNodeId")
              "The graft's OWN id is already in the tree."
          case
              "insert-reject-duplicate-descendant-id"
              (InsertChild("b", nodeOf "n2" "section" [ nodeOf "a1" "para" [] ]))
              (All "DuplicateNodeId")
              "A DESCENDANT of the graft carries an id the tree already holds (Phase 137)."
          case
              "insert-reject-duplicate-within-graft"
              (InsertChild("b", nodeOf "n3" "section" [ nodeOf "t" "para" []; nodeOf "t" "para" [] ]))
              (NoneWith
                  "NO HOST MAPPING, and this is a real divergence rather than a gap in the table. fuaran-ts, fuaran-py, fuaran-go and fuaran-rs all seed the duplicate scan from the ROOT's ids alone, so a graft that repeats an id WITHIN ITSELF — with nothing in common with the tree — is ACCEPTED on all four, leaving the tree holding one id twice. Core refuses it. A host adopting this vector adds the check; it does not translate a code it has.")
              "The graft repeats an id within ITSELF; nothing in it is in the tree (Phase 137)."
          case
              "insert-reject-unknown-parent"
              (InsertChild("nope", nodeOf "n4" "para" []))
              (All "ParentNotFound")
              "The named parent is not in the tree."

          // ---- RemoveNode ----
          case "remove-accept" (RemoveNode "a1") (NoneWith "accept") "The target leaves its parent's child list."
          case
              "remove-reject-root"
              (RemoveNode "root")
              (All "KindMismatch")
              "The root cannot be removed — there would be no tree left to address."
          case "remove-reject-unknown" (RemoveNode "nope") (All "NodeNotFound") "The target is not in the tree."

          // ---- MoveNode ----
          case
              "move-accept"
              (MoveNode("a1", "b"))
              (NoneWith "accept")
              "The subtree is detached and APPENDED under the new parent; order is a separate op."
          case
              "move-reject-root"
              (MoveNode("root", "a"))
              (All "KindMismatch")
              "The root cannot be moved, for the reason it cannot be removed."
          case
              "move-reject-unknown-target"
              (MoveNode("nope", "a"))
              (All "NodeNotFound")
              "The subtree to move is not in the tree."
          case
              "move-reject-unknown-new-parent"
              (MoveNode("a1", "nope"))
              (All "ParentNotFound")
              "The destination parent is not in the tree."
          case "move-reject-under-self" (MoveNode("a", "a")) (All "KindMismatch") "A node cannot become its own parent."
          case
              "move-reject-under-descendant"
              (MoveNode("a", "a1"))
              (All "KindMismatch")
              "A node cannot move under one of its own descendants — that is a cycle, not a tree."

          // ---- ReorderChildren ----
          case
              "reorder-accept"
              (ReorderChildren("a", [ "a2"; "a1" ]))
              (NoneWith "accept")
              "Order is stated by naming ids, so it is checkable where an index would not be."
          case
              "reorder-reject-unknown-parent"
              (ReorderChildren("nope", [ "a1" ]))
              (All "ParentNotFound")
              "The named parent is not in the tree."
          case
              "reorder-reject-not-a-permutation"
              (ReorderChildren("a", [ "a1" ]))
              (All "OrderingMismatch")
              "The proposed order is not a permutation of the parent's current children."

          // ---- Batch ----
          case
              "batch-accept-insert-then-order"
              (Batch
                  [ InsertChild("b", nodeOf "n5" "para" [])
                    ReorderChildren("b", [ "n5"; "b1" ]) ])
              (NoneWith "accept")
              "Membership and order are separate concerns: placing a node anywhere but last is InsertChild then ReorderChildren."
          case
              "batch-first-op-alone-accept"
              (InsertChild("b", nodeOf "n6" "para" []))
              (NoneWith "accept")
              "The first op of the atomicity pair below, ON ITS OWN. It succeeds — which is what makes the pair evidence of atomicity rather than of a single refusal."
          case
              "batch-reject-all-or-nothing"
              (Batch [ InsertChild("b", nodeOf "n6" "para" []); RemoveNode "nope" ])
              (NoneWith
                  "NO DIRECT MAPPING: fuaran-ts, fuaran-py, fuaran-go and fuaran-rs WRAP an inner failure as `BatchAborted` carrying the inner op's index, discarding the inner class; Core returns the inner rejection itself. A host asserts the wrapper and the index — `BatchAborted` at index 1 here — and the tree unchanged; the inner class is not comparable across the two shapes.")
              "All-or-nothing: the first op would succeed alone (see the vector above), the second refuses, and the tree is the one the batch started from."

          // ---- UpdateNode (Phase 250) ----
          case
              "update-accept-leaf"
              (UpdateNode(nodeOf "a1" "heading" []))
              (NoneWith "accept")
              "The node whose id the payload carries takes the payload's content in place: same id, same position among its siblings."
          case
              "update-accept-keeps-children"
              (UpdateNode(nodeOf "a" "aside" []))
              (NoneWith "accept")
              "Content, not structure: the node keeps the children it has, and the payload's own (empty) child list is not read."
          case
              "update-accept-root"
              (UpdateNode(nodeOf "root" "article" []))
              (NoneWith "accept")
              "The root can be rewritten in place — it keeps its id and its children, so nothing is left unaddressable."
          case
              "update-reject-unknown"
              (UpdateNode(nodeOf "nope" "para" []))
              (NoneWith
                  "NO HOST MAPPING yet: none of fuaran-ts, fuaran-py, fuaran-go or fuaran-rs carries an in-place update op, so there is no code to translate. A host adopting `updateNode` raises its node-not-found code here.")
              "The payload's id names no node in the tree." ]

    // -----------------------------------------------------------------------
    //  the rendered artefact
    // -----------------------------------------------------------------------
    //  Hand-rolled for the reasons the sibling law-vector exporter records: an indented
    //  `Utf8JsonWriter` emits `Environment.NewLine`, so the same run would produce different bytes
    //  on Windows and Linux, and its default escaper rewrites characters this corpus should not
    //  inherit a framework's opinion about. The escaper below is the JSON minimum.

    let private jstr (s: string) : string =
        let sb = StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    let private jobj (members: (string * string) list) : string =
        match members with
        | [] -> "{}"
        | _ ->
            "{ "
            + (members |> List.map (fun (k, v) -> jstr k + ": " + v) |> String.concat ", ")
            + " }"

    /// The digest convention the estate already uses everywhere else: `sha256:` + lowercase hex,
    /// over the canonical result bytes exactly as they appear in `expected.tree`.
    let hashOf (canonicalTree: string) : string =
        "sha256:" + Hash.sha256Hex canonicalTree

    /// What the reference answered, in the terms a host compares against. Computed by CALLING
    /// `Ops.apply`; nothing here restates what it ought to say.
    let private expectedOf (c: Case) : string =
        match Ops.apply nodew idw c.Op c.Tree with
        | Ok result ->
            let bytes = encodeTree result

            jobj [ "verdict", jstr "accept"; "tree", jstr bytes; "hash", jstr (hashOf bytes) ]
        | Error r ->
            let hosts =
                match c.Hosts with
                | All code -> hostRoster |> List.map (fun h -> h, jstr code)
                | NoneWith _ -> []

            jobj (
                [ "verdict", jstr "reject"; "rejection", jstr (Canon.render (rejectionJ r)) ]
                @ [ "hosts", jobj hosts ]
                @ (match c.Hosts with
                   | NoneWith note -> [ "hostsNote", jstr note ]
                   | All _ -> [])
            )

    /// The op tag, for a reader scanning the file by op rather than by vector id.
    let private caseTag (op: SkeletonOp<RNode, string>) : string =
        match op with
        | InsertChild _ -> "InsertChild"
        | RemoveNode _ -> "RemoveNode"
        | MoveNode _ -> "MoveNode"
        | ReorderChildren _ -> "ReorderChildren"
        | Batch _ -> "Batch"
        | UpdateNode _ -> "UpdateNode"

    let private renderVector (c: Case) : string =
        jobj
            [ "id", jstr c.Id
              "case", jstr (caseTag c.Op)
              "description", jstr c.Description
              "input", jobj [ "tree", jstr (encodeTree c.Tree); "op", jstr (encodeOp c.Op) ]
              "expected", expectedOf c ]

    let private description =
        "Apply-engine conformance for the six skeleton tree ops (InsertChild, RemoveNode, "
        + "MoveNode, ReorderChildren, Batch, and since Phase 250 UpdateNode): one vector per "
        + "validator clause per op, Batch's "
        + "all-or-nothing atomicity, and the three id-collision shapes. Each vector carries an "
        + "`input.tree` and an `input.op` as canonical JSON STRINGS. The tree envelope is the "
        + "WITNESS SURFACE and nothing else — `{\"children\":[…],\"id\":…,\"kind\":…}` — because the "
        + "ops read structure and, for UpdateNode, the `kind` it rewrites in place and nothing "
        + "else; a host runs the family by "
        + "mapping those three members onto its own node type, not by decoding its own wire format. "
        + "An `accept` vector requires the host's applied tree to encode byte-for-byte to "
        + "`expected.tree`, whose `expected.hash` is `sha256:` plus lowercase hex over exactly those "
        + "bytes. A `reject` vector requires the host to refuse, with `expected.hosts` naming the "
        + "code each host raises for that clause — every non-reference host carries the same "
        + "twelve-code ApplyErrorCode vocabulary, so the mapping is a correspondence and never a "
        + "rename: Core says DuplicateId where they say DuplicateNodeId. Two vectors carry an "
        + "`expected.hostsNote` and NO mapping, because there is nothing to map: one is a refusal "
        + "the other hosts do not make at all, the other is a wrapper shape they do not share. "
        + "WHAT THIS FAMILY DOES NOT PIN, deliberately: the ORDER clauses are checked in (Core "
        + "checks the root and existence before the cycle guard; at least one host checks the cycle "
        + "guard first, so a vector that breaks two clauses at once would compare implementations "
        + "rather than the contract, and none does); `UnknownNode`'s enumeration of addressable ids "
        + "and `ReorderMismatch`'s two orders, which are evidence for a class rather than the class; "
        + "and the container-capability refusal (NotAContainer / ChildlessKind), which is a domain "
        + "predicate handed to the engine rather than a property of a wire document. There is no "
        + "kitVersion stamp: these are the apply SEMANTICS, not one kit's sampled answers, and the "
        + "reference recomputes every expectation on each run."

    let renderVectors () : string =
        let sb = StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "{"
        line ("  \"family\": " + jstr familyId + ",")
        line ("  \"description\": " + jstr description + ",")
        line "  \"vectors\": ["

        let rendered = cases () |> List.map renderVector
        let last = List.length rendered - 1

        rendered
        |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

        line "  ]"
        line "}"
        sb.ToString()

    /// The family index. EMITTED rather than hand-kept, unlike the `laws/` one — that index spans
    /// families its writer does not know about, so a wholesale renderer there would drop whatever it
    /// had not been told; this one indexes exactly the family beside it, so emitting it is what
    /// stops its vector count drifting from the file it counts.
    let renderManifest () : string =
        let sb = StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        let indexDescription =
            "Fuaran apply-engine conformance corpus. SELF-ENUMERATED: this family is deliberately "
            + "not indexed by the corpus root manifest.json, which indexes the canonical "
            + "wire-format codec families only — the laws/, dag/, merge-conformance/ and "
            + "devtools-relay/ precedent. `adoption` is per host and is DATA: a host whose entry "
            + "reads `proposed` reports the family BY NAME rather than skipping it silently, and "
            + "flips its own entry to `adopted` in the change-set that lands its leg. This "
            + "manifest is authoritative for the vector count; do not restate one in prose."

        line "{"
        line "  \"version\": 1,"
        line ("  \"description\": " + jstr indexDescription + ",")
        line "  \"families\": ["

        line (
            "    "
            + jobj
                [ "id", jstr familyId
                  "kind", jstr "apply-vectors"
                  "file", jstr vectorsFileName
                  "vectors", string (List.length (cases ()))
                  "adoption",
                  jobj (
                      ("fuaran", jstr "adopted")
                      :: (hostRoster |> List.map (fun h -> h, jstr "proposed"))
                  )
                  "description",
                  jstr (
                      "The skeleton-op apply contract over the witness surface: accept with the "
                      + "result's canonical bytes and hash, or reject with the class, per validator "
                      + "clause per op, plus Batch atomicity and the id-collision shapes."
                  ) ]
        )

        line "  ]"
        line "}"
        sb.ToString()

    // -----------------------------------------------------------------------
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let vectorsPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, vectorsFileName)

    let manifestPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, manifestFileName)

    /// Write both artefacts with LF endings, whatever the host platform — the corpus is
    /// byte-compared by several hosts on three operating systems.
    let write (corpusDir: string) : unit =
        Directory.CreateDirectory(familyDir corpusDir) |> ignore
        File.WriteAllText(vectorsPath corpusDir, renderVectors ())
        File.WriteAllText(manifestPath corpusDir, renderManifest ())

    // -----------------------------------------------------------------------
    //  reading a committed file back
    // -----------------------------------------------------------------------

    /// One parsed vector, reduced to what a check reads. The `(tree, op)` pair is handed back
    /// DECODED, so a consumer runs it rather than re-parsing it.
    type ParsedVector =
        { Id: string
          Case: string
          Tree: RNode
          Op: SkeletonOp<RNode, string>
          Verdict: string
          ExpectedTree: string option
          ExpectedHash: string option
          Rejection: string option }

    let parseVectors (json: string) : Result<ParsedVector list, string> =
        match Json.parse json with
        | Error m -> Error("the vector file did not parse: " + m)
        | Ok doc ->
            match field "vectors" doc with
            | Some(JArr items) ->
                let parsed =
                    items
                    |> List.map (fun v ->
                        match str "id" v, field "input" v, field "expected" v with
                        | Some id, Some input, Some expected ->
                            match str "tree" input, str "op" input, str "verdict" expected with
                            | Some treeJson, Some opJson, Some verdict ->
                                match decodeTree treeJson, decodeOp opJson with
                                | Ok tree, Ok op ->
                                    Ok
                                        { Id = id
                                          Case = defaultArg (str "case" v) ""
                                          Tree = tree
                                          Op = op
                                          Verdict = verdict
                                          ExpectedTree = str "tree" expected
                                          ExpectedHash = str "hash" expected
                                          Rejection = str "rejection" expected }
                                | Error m, _ -> Error("vector " + id + ": input.tree did not decode (" + m + ")")
                                | _, Error m -> Error("vector " + id + ": input.op did not decode (" + m + ")")
                            | _ -> Error("vector " + id + ": input.tree / input.op / expected.verdict missing")
                        | _ -> Error "a vector is missing id / input / expected")

                match
                    parsed
                    |> List.tryPick (function
                        | Error m -> Some m
                        | Ok _ -> None)
                with
                | Some m -> Error m
                | None ->
                    Ok
                        [ for p in parsed do
                              match p with
                              | Ok v -> yield v
                              | Error _ -> () ]
            | _ -> Error "the vector file carries no `vectors` array"

    /// Run one parsed vector the way a host would — apply, then compare — and report what
    /// disagreed with the recorded answer.
    let checkVector (v: ParsedVector) : string option =
        match Ops.apply nodew idw v.Op v.Tree, v.Verdict with
        | Ok result, "accept" ->
            let bytes = encodeTree result

            match v.ExpectedTree, v.ExpectedHash with
            | Some expected, Some expectedHash ->
                if bytes <> expected then
                    Some(sprintf "%s: the reference produced\n  %s\nbut the vector records\n  %s" v.Id bytes expected)
                elif hashOf bytes <> expectedHash then
                    Some(
                        sprintf
                            "%s: the recorded hash is %s but the recorded bytes digest to %s"
                            v.Id
                            expectedHash
                            (hashOf bytes)
                    )
                else
                    None
            | _ -> Some(sprintf "%s: an `accept` vector carries no expected.tree / expected.hash" v.Id)
        | Ok _, verdict -> Some(sprintf "%s: the reference ACCEPTED the op, but the vector says `%s`" v.Id verdict)
        | Error r, "reject" ->
            let actual = Canon.render (rejectionJ r)

            match v.Rejection with
            | Some expected when expected = actual -> None
            | Some expected ->
                Some(sprintf "%s: the reference refused with\n  %s\nbut the vector records\n  %s" v.Id actual expected)
            | None -> Some(sprintf "%s: a `reject` vector carries no expected.rejection" v.Id)
        | Error r, verdict -> Some(sprintf "%s: the reference REFUSED (%A), but the vector says `%s`" v.Id r verdict)
