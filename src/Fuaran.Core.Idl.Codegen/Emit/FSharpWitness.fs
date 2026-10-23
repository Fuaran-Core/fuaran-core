namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core

/// The F# structural layer's WITNESS leg (moved out of [[FSharpCodec]] by Phase 474 to keep that file
/// under the 2,000-line rule, unchanged): the `NodeWitness` over the generated `Node` and the validator
/// scaffold wired to it.
module internal FSharpWitness =

    // -----------------------------------------------------------------------
    // Phase 317 increment 5 — the Core witness-record leg. Emit a
    // `NodeWitness<Node, string>` for the generated `Node`, so the generated
    // structural layer plugs straight into `Fuaran.Core.Tree` / `.Validator` /
    // `.Observer` — the "serves every domain via the Core witness, not just UI"
    // promise. `Children` / `ReplaceChildren` are derived from the IDL: a field
    // is a child position iff `Derive.structural` says so — a node or
    // node list the kind always carries (Phase 545: an optional node is a keyed
    // position, on both the private and the public emission).
    // -----------------------------------------------------------------------

    /// Emit the `NodeWitness<Node, string>` + its three helper projections. Top-level
    /// `match` functions (not record-literal lambdas) to dodge offside pitfalls in
    /// generated code. `Error` on a kind mixing a `Node list` field with other node-bearing
    /// fields (`ReplaceChildren` not generable — GP4/GP5) rather than emitting a runtime
    /// throwing guard; kinds whose node-bearing fields are all single `Node` are generated
    /// with positional re-assignment.
    ///
    /// Phase 374 — `publicAccess` is the derived structural-access emission: the same arms as
    /// PUBLIC functions (`wireTag`, `allWireTags`, `children`, `withChildren`, kids first so
    /// `withChildren (children n) n = n` reads as written) with `nodeWitness` built on them, and
    /// an OPTIONAL node field is a keyed position rather than a child (it is not an ordered list
    /// a structural edit may rebuild). `false` is the emission every module had before, byte for
    /// byte.
    ///
    /// Phase 545 — both emissions read child positions through the one classifier,
    /// `FSharpDerive.structuralField`. The private witness used to read them by type alone, so an
    /// optional node field became `[ s.Hint ]` — a `Node option` where a `Node` is expected — and
    /// a module requesting no StructuralAccess failed to compile (FS0001).
    ///
    /// Phase 403 — a projected kind's children are read from its declared record fields
    /// (`Projection.RecordFields`); one that declares none and whose wire holds a node directly is
    /// refused by name, because nothing says which member of the host record holds it.
    let witnessDecl
        (publicAccess: bool)
        (projections: Map<string, KindProjection>)
        (msg: Set<string>)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []

        let construct, request =
            if publicAccess then
                "the structural children", "do not request StructuralAccess"
            else
                "the node witness's children", "do not select the kind"

        let kindFields =
            kinds
            |> List.map (fun k ->
                FSharpDerive.nodeFieldsOf
                    projections
                    (fun f ->
                        match f.Type with
                        | TNode
                        | TList TNode -> true
                        | _ -> false)
                    construct
                    request
                    k
                |> Result.map (fun fs -> k.Tag, fs))
            |> sequenceR
            |> Result.map Map.ofList

        let fieldsOf (k: IdlKind) =
            kindFields |> Result.map (fun m -> m[k.Tag]) |> Result.defaultValue []

        let childBearing =
            kinds
            |> List.filter (fun k -> not (List.isEmpty (Derive.structuralFields (fieldsOf k))))

        let allBearing = List.length childBearing = List.length kinds

        let kindTagArms =
            kinds
            |> List.map (fun k -> sprintf "    | NodeKind.%s _ -> %s" k.Tag (SourceLit.fsString k.Tag))
            |> String.concat "\n"

        let childArm (k: IdlKind) =
            let exprs =
                Derive.structuralFields (fieldsOf k)
                |> List.map (fun (f, isList) ->
                    if isList then
                        "s." + pascal f.Name
                    else
                        "[ s." + pascal f.Name + " ]")
                |> String.concat " @ "

            sprintf "    | NodeKind.%s s -> %s" k.Tag exprs

        let replaceArm (k: IdlKind) : Result<string, CodegenError> =
            Derive.childLayout k.Tag (Derive.structuralFields (fieldsOf k))
            |> Result.map (fun layout ->
                match layout with
                | ChildLayout.Whole f ->
                    sprintf
                        "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = kids } }"
                        k.Tag
                        k.Tag
                        (pascal f.Name)
                | ChildLayout.Positional [ f ] ->
                    sprintf
                        "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = List.head kids } }"
                        k.Tag
                        k.Tag
                        (pascal f.Name)
                | ChildLayout.Positional fs ->
                    // Several single-`Node` fields (real tier: `ErrorBoundary` has `child` +
                    // `fallback`). `witnessChildren` returns them in field order, so re-assign `kids`
                    // positionally.
                    let assigns =
                        fs
                        |> List.mapi (fun i f -> sprintf "%s = List.item %d kids" (pascal f.Name) i)
                        |> String.concat "; "

                    sprintf "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s } }" k.Tag k.Tag assigns)

        let childArms =
            (childBearing |> List.map childArm)
            @ (if allBearing then [] else [ "    | _ -> []" ])
            |> String.concat "\n"

        let replaceArms =
            kindFields
            |> Result.bind (fun _ -> childBearing |> List.map replaceArm |> sequenceR)
            |> Result.map (fun arms -> (arms @ (if allBearing then [] else [ "    | _ -> n" ])) |> String.concat "\n")

        let publicDecl (replaceArmsStr: string) =
            String.concat
                "\n"
                [ "/// The kind's wire tag — the discriminator it is encoded under."
                  sprintf "let wireTag (n: Node%s) : string =" nodeArgs
                  "    match n.Kind with"
                  kindTagArms
                  ""
                  "/// Every wire tag this module's kinds are encoded under, in declaration order."
                  sprintf
                      "let allWireTags: string list = [ %s ]"
                      (kinds |> List.map (fun k -> SourceLit.fsString k.Tag) |> String.concat "; ")
                  ""
                  "/// The node's ordered structural children, in field order."
                  sprintf "let children (n: Node%s) : Node%s list =" nodeArgs nodeArgs
                  "    match n.Kind with"
                  childArms
                  ""
                  "/// The node with exactly this structural child list, its id and kind kept."
                  sprintf "let withChildren (kids: Node%s list) (n: Node%s) : Node%s =" nodeArgs nodeArgs nodeArgs
                  "    match n.Kind with"
                  replaceArmsStr
                  ""
                  sprintf "let nodeWitness: NodeWitness<Node%s, string> =" nodeArgs
                  "    { Id = fun n -> n.Id"
                  "      KindTag = wireTag"
                  "      Children = children"
                  "      ReplaceChildren = fun n kids -> withChildren kids n }" ]

        replaceArms
        |> Result.map (fun replaceArmsStr ->
            if publicAccess then
                publicDecl replaceArmsStr
            else
                String.concat
                    "\n"
                    [ sprintf "let private witnessKindTag (n: Node%s) : string =" nodeArgs
                      "    match n.Kind with"
                      kindTagArms
                      ""
                      sprintf "let private witnessChildren (n: Node%s) : Node%s list =" nodeArgs nodeArgs
                      "    match n.Kind with"
                      childArms
                      ""
                      sprintf
                          "let private witnessReplaceChildren (n: Node%s) (kids: Node%s list) : Node%s ="
                          nodeArgs
                          nodeArgs
                          nodeArgs
                      "    match n.Kind with"
                      replaceArmsStr
                      ""
                      sprintf "let nodeWitness: NodeWitness<Node%s, string> =" nodeArgs
                      "    { Id = fun n -> n.Id"
                      "      KindTag = witnessKindTag"
                      "      Children = witnessChildren"
                      "      ReplaceChildren = witnessReplaceChildren }" ])

    // -----------------------------------------------------------------------
    // Phase 317 increment 6 — the validator-rule-scaffold leg. Emit a
    // `Fuaran.Core.Validator`-ready entry point wired through the generated
    // `nodeWitness`. Rule *content* stays domain-side (that is the whole point
    // of `Core.Validator` — `RuleFamily` packs are domain-supplied); what the
    // generator owns is the scaffold: a `runValidator` that runs any registry
    // over the generated `Node` via the witness. A domain registers its own
    // families and gets build-time verification over generated nodes for free.
    // -----------------------------------------------------------------------

    /// Emit the validator scaffold — independent of the kind set (it wires the
    /// generic `Validator.runAll` to the generated `Node` + `nodeWitness`).
    let validatorDecl (msg: Set<string>) : string =
        let nodeArgs = declParams msg "Node" []

        String.concat
            "\n"
            [ "// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side."
              sprintf
                  "let runValidator (reg: Validator.RuleRegistry<Node%s, string>) (root: Node%s) : Defect<string> list ="
                  nodeArgs
                  nodeArgs
              "    Validator.runAll nodeWitness reg root" ]
