namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit.Core
open Fuaran.Core.Idl.Emit.FStarModel
open Fuaran.Core.Idl.Emit.FStarVocabulary

/// The interpreter's vectors as NORMALISER facts over a generated model (Phase 303; split out of
/// `FStar.fs` by Phase 524).
module internal FStarVectors =

    // -----------------------------------------------------------------------
    // Phase 303 — the interpreter's vectors as NORMALISER facts over a generated model.
    // -----------------------------------------------------------------------

    /// A float as the model's `flt` carrier at the facts' instantiation: its canonical layout,
    /// as an F* string literal. The model has no render step and an opaque `flt`, so what a fact
    /// can check about a float is that it sits in the right member of the right object.
    let private floatCarrier (where: string) (f: float) : Result<string, CodegenError> =
        if System.Double.IsFinite f then
            Ok(lit (Canonical.write Rendering.CrossHost (JFloat f)))
        else
            unmodellable
                "a non-finite float — the wire spells it as a quoted string (WIRE_FORMAT §7), and the model's float slot carries a number"
                where

    let private strLit (where: string) (s: string) : Result<string, CodegenError> =
        if SourceLit.isWellFormed s then
            Ok(lit s)
        else
            unmodellable "a string holding an unpaired surrogate, which F* cannot spell" where

    let private intLit (i: int) : string =
        if i < 0 then "(" + string i + ")" else string i

    let private pairTerm (kl: Result<string, CodegenError>) (vt: Result<string, CodegenError>) =
        match kl, vt with
        | Ok k, Ok v -> Ok("(" + k + ", " + v + ")")
        | Error e, _
        | _, Error e -> Error e

    /// A `jval int string` term for a JSON value carried VERBATIM (a `json` or hosted slot).
    let rec private jsonTerm (where: string) (j: JVal) : Result<string, CodegenError> =
        match j with
        | JStr s -> strLit where s |> Result.map (fun l -> "(JStr " + l + ")")
        | JInt i -> Ok("(JInt " + intLit i + ")")
        | JBool b -> Ok(if b then "(JBool true)" else "(JBool false)")
        | JFloat f -> floatCarrier where f |> Result.map (fun l -> "(JFloat " + l + ")")
        | JArr xs ->
            xs
            |> List.map (jsonTerm where)
            |> sequenceR
            |> Result.map (fun ts -> "(JArr [" + String.concat "; " ts + "])")
        | JObj fs ->
            fs
            |> List.map (fun (k, v) -> pairTerm (strLit where k) (jsonTerm where v))
            |> sequenceR
            |> Result.map (fun ts -> "(JObj [" + String.concat "; " ts + "])")

    /// The MODEL value of an interpreter-decoded value, as an F* term qualified by `q` (the
    /// model module's abbreviation and a dot) at `num = int`, `flt = string`.
    /// Phase 722 — the case an authored value holds at a presence-keyed field: what tells the
    /// constructors of one holder apart ([[ctorFor]]).
    let private authoredPick (fs: (string * IdlValue) list) (p: Pick) : string option option =
        Some(authoredCase p.Field fs)

    let rec private valueTerm
        (idl: Idl)
        (m: Model)
        (q: string)
        (where: string)
        (s: Slot)
        (v: IdlValue)
        : Result<string, CodegenError> =
        match s, v with
        | SStr, VStr t -> strLit where t
        | SInt, VInt i -> Ok(intLit i)
        | SBool, VBool b -> Ok(if b then "true" else "false")
        | SFloat, VFloat f -> floatCarrier where f
        | SFloat, VInt i -> floatCarrier where (float i)
        | SJson, VJson j -> jsonTerm where j
        | SSentinel _, _
        | SConst _, _ -> Ok "()"
        | SEnum n, VEnum spelling ->
            // Phase 525 — the WIRE spelling resolves through `CaseOf`, the one rule every backend
            // reads; a host case name the wire does not use is not this enum's value.
            match CodegenLookup.tryEnum idl n with
            | Some e ->
                match e.CaseOf spelling with
                | Some case -> Ok(q + ctorName (slotName (SEnum n)) case)
                | None -> unmodellable (sprintf "the enum spelling %A" spelling) where
            | None -> unmodellable ("the undeclared enum " + n) where
        | SList inner, VList xs ->
            xs
            |> List.map (valueTerm idl m q where inner)
            |> sequenceR
            |> Result.map (fun ts -> "[" + String.concat "; " ts + "]")
        | SMap inner, VMap entries ->
            entries
            |> List.map (fun (k, ev) -> pairTerm (strLit where k) (valueTerm idl m q where inner ev))
            |> sequenceR
            |> Result.map (fun ts -> "[" + String.concat "; " ts + "]")
        | SRecord(n, _), VRecord fs ->
            match
                m.Records.TryFind(slotName s)
                |> Option.bind (fun ctors -> ctorFor m (slotName s) ctors "Mk" (authoredPick fs))
            with
            | None -> unmodellable ("the undeclared record " + n) where
            | Some(label, ms) -> ctorTerm idl m q where (q + ctorName (slotName s) label) [] ms fs
        | SUnion(n, args), VUnion(tag, fs) ->
            match CodegenLookup.tryUnion idl n with
            | Some u when List.length u.Params = List.length args ->
                if (TransparentUnion.tag idl.Harden u).IsSome then
                    unmodellable ("a value of the transparent union " + n) where
                else
                    match
                        m.Cases.TryFind(slotName s)
                        |> Option.bind (fun ctors -> ctorFor m (slotName s) ctors tag (authoredPick fs))
                    with
                    | None -> unmodellable (sprintf "the case %s.%s" n tag) where
                    | Some(label, ms) -> ctorTerm idl m q where (q + ctorName (slotName s) label) [] ms fs
            | _ -> unmodellable ("the union " + n) where
        | SNode, VNode(id, tag, fs) -> nodeTerm idl m q where id [] tag fs
        | SNode, VNodeEnv(id, env, tag, fs) -> nodeTerm idl m q where id env tag fs
        // Phase 639 — a string-borne exact value is refused: its model type is a string refined by its
        // spelling, which a normaliser fact over a literal does not discharge. A tuple of other leaves is
        // the F* tuple of their terms.
        | SText n, _ -> unmodellable (sprintf "a '%s' value, whose refinement the facts do not discharge" n) where
        | STuple cs, VList xs when List.length cs = List.length xs ->
            List.map2 (valueTerm idl m q where) cs xs
            |> sequenceR
            |> Result.map (fun ts -> "(" + String.concat ", " ts + ")")
        | _ -> unmodellable (sprintf "a value that does not fit its slot: %A" v) where

    /// The constructor applied to `lead` and then each member in declared KEY ORDER, from the
    /// decoded field list: an absent optional is `None`, an absent omit-default its default.
    and private ctorTerm
        (idl: Idl)
        (m: Model)
        (q: string)
        (where: string)
        (ctor: string)
        (lead: string list)
        (ms: Member list)
        (fs: (string * IdlValue) list)
        : Result<string, CodegenError> =
        argTerms idl m q where ms fs
        |> Result.map (fun parts ->
            match lead @ parts with
            | [] -> ctor
            | all -> "(" + ctor + " " + String.concat " " all + ")")

    and private argTerms
        (idl: Idl)
        (m: Model)
        (q: string)
        (where: string)
        (ms: Member list)
        (fs: (string * IdlValue) list)
        : Result<string list, CodegenError> =
        orderMembers idl.Wire.KeyOrder ms
        |> List.map (fun mb ->
            let given =
                authoredFor mb fs
                |> List.tryPick (fun (n, fv) ->
                    match fv with
                    | VAbsent -> None
                    | _ when n = mb.Name -> Some fv
                    | _ -> None)

            match given, mb.Presence with
            // A synthesised member carries nothing: the wire value is the declaration's.
            | _ when mb.Slot.IsSConst -> Ok "()"
            | None, Some None -> Ok "None"
            | None, Some(Some d) -> Ok("(" + d + ")")
            | None, None -> unmodellable ("a value omitting the required member '" + mb.Name + "'") where
            | Some fv, Some None -> valueTerm idl m q where mb.Slot fv |> Result.map (fun t -> "(Some " + t + ")")
            | Some fv, _ -> valueTerm idl m q where mb.Slot fv |> Result.map (fun t -> "(" + t + ")"))
        |> sequenceR

    and private nodeTerm
        (idl: Idl)
        (m: Model)
        (q: string)
        (where: string)
        (id: string)
        (env: (string * IdlValue) list)
        (tag: string)
        (fs: (string * IdlValue) list)
        : Result<string, CodegenError> =
        match ctorFor m "vkind" m.Kinds tag (authoredPick fs) with
        | None -> unmodellable ("the undeclared kind " + tag) where
        | Some(label, kms) ->
            let ems = m.Envelope

            match strLit where id with
            | Error e -> Error e
            | Ok idLit ->
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind ->
                    ctorTerm idl m q where (q + ctorName "vkind" label) [] kms fs
                    |> Result.bind (fun kindTerm ->
                        ctorTerm idl m q where (q + ctorName "node" "Node") [ idLit; kindTerm ] ems env)
                | NodeEnvelopeShape.FlatKind ->
                    ctorTerm idl m q where (q + ctorName "node" label) [ idLit ] (kms @ ems) (fs @ env)

    /// The EXPECTED term: the interpreter's bytes, parsed, as a `jval int string`, walked by the
    /// slot each member declares — so a number at a float slot is the model's float carrier
    /// whether the canonical layout wrote it as `2` or `2.5`, and every object keeps the key
    /// order the bytes carry.
    let rec private wireTerm (idl: Idl) (m: Model) (where: string) (s: Slot) (j: JVal) : Result<string, CodegenError> =
        let objectOf (lookup: string -> Slot option) (fs: (string * JVal) list) =
            fs
            |> List.map (fun (k, v) ->
                match lookup k with
                | None -> unmodellable ("the undeclared member '" + k + "' in the interpreter's bytes") where
                | Some slot -> pairTerm (strLit where k) (wireTerm idl m where slot v))
            |> sequenceR
            |> Result.map (fun ts -> "(JObj [" + String.concat "; " ts + "])")

        let disc = idl.Wire.Discriminator

        let memberSlot (ms: Member list) (k: string) =
            ms |> List.tryFind (fun m -> m.Name = k) |> Option.map _.Slot

        let tagOf (fs: (string * JVal) list) =
            match fs |> List.tryFind (fun (k, _) -> k = disc) with
            | Some(_, JStr t) -> Ok t
            | _ -> unmodellable "an object with no discriminator in the interpreter's bytes" where

        // Phase 722 — the case a presence-keyed union reads as off an object's keys (D220.3).
        let wirePick (fs: (string * JVal) list) (p: Pick) : string option option =
            m.Keyed
            |> List.tryFind (fun k -> k.Union = p.Union)
            |> Option.bind (fun k ->
                match readPick k (fun key -> fs |> List.exists (fun (n, _) -> n = key)) with
                | Ok case -> Some case
                | Error() -> None)

        let kindMembers (fs: (string * JVal) list) (tag: string) =
            match ctorFor m "vkind" m.Kinds tag (wirePick fs) with
            | None -> unmodellable ("the undeclared kind " + tag) where
            | Some(_, ms) -> Ok ms

        match s, j with
        | SFloat, JInt i -> floatCarrier where (float i) |> Result.map (fun l -> "(JFloat " + l + ")")
        | SFloat, JFloat f -> floatCarrier where f |> Result.map (fun l -> "(JFloat " + l + ")")
        | SJson, _
        | SConst _, _ -> jsonTerm where j
        | (SStr | SSentinel _ | SEnum _ | SText _), JStr t -> strLit where t |> Result.map (fun l -> "(JStr " + l + ")")
        | STuple cs, JArr xs when List.length cs = List.length xs ->
            List.map2 (wireTerm idl m where) cs xs
            |> sequenceR
            |> Result.map (fun ts -> "(JArr [" + String.concat "; " ts + "])")
        | SInt, JInt i -> Ok("(JInt " + intLit i + ")")
        | SBool, JBool b -> Ok(if b then "(JBool true)" else "(JBool false)")
        | SList inner, JArr xs ->
            xs
            |> List.map (wireTerm idl m where inner)
            |> sequenceR
            |> Result.map (fun ts -> "(JArr [" + String.concat "; " ts + "])")
        | SMap inner, JObj fs -> objectOf (fun _ -> Some inner) fs
        | SRecord(n, _), JObj fs ->
            match
                m.Records.TryFind(slotName s)
                |> Option.bind (fun ctors -> ctorFor m (slotName s) ctors "Mk" (wirePick fs))
            with
            | None -> unmodellable ("the undeclared record " + n) where
            | Some(_, ms) -> objectOf (memberSlot ms) fs
        | SUnion(n, args), JObj fs ->
            match CodegenLookup.tryUnion idl n, tagOf fs with
            | _, Error e -> Error e
            | Some u, Ok tag when List.length u.Params = List.length args ->
                match
                    m.Cases.TryFind(slotName s)
                    |> Option.bind (fun ctors -> ctorFor m (slotName s) ctors tag (wirePick fs))
                with
                | None -> unmodellable (sprintf "the case %s.%s" n tag) where
                | Some(_, ms) -> objectOf (fun k -> if k = disc then Some SStr else memberSlot ms k) fs
            | _ -> unmodellable ("the union " + n) where
        | SNode, JObj fs ->
            let ems = m.Envelope

            match idl.Wire.NodeEnvelope with
            | NodeEnvelopeShape.FlatKind ->
                tagOf fs
                |> Result.bind (kindMembers fs)
                |> Result.bind (fun kms ->
                    objectOf
                        (fun k ->
                            if k = disc || k = "id" then
                                Some SStr
                            else
                                memberSlot (kms @ ems) k)
                        fs)
            | NodeEnvelopeShape.NestedKind ->
                let kindObject (kj: JVal) =
                    match kj with
                    | JObj kfs ->
                        tagOf kfs
                        |> Result.bind (kindMembers kfs)
                        |> Result.bind (fun kms ->
                            objectOf (fun k -> if k = disc then Some SStr else memberSlot kms k) kfs)
                    | _ -> unmodellable "a node whose kind is not an object" where

                fs
                |> List.map (fun (k, v) ->
                    match k with
                    | "id" -> pairTerm (Ok(lit "id")) (wireTerm idl m where SStr v)
                    | "kind" -> pairTerm (Ok(lit "kind")) (kindObject v)
                    | _ ->
                        match memberSlot ems k with
                        | None -> unmodellable ("the undeclared envelope member '" + k + "'") where
                        | Some slot -> pairTerm (strLit where k) (wireTerm idl m where slot v))
                |> sequenceR
                |> Result.map (fun ts -> "(JObj [" + String.concat "; " ts + "])")
        | _ -> unmodellable (sprintf "interpreter bytes that do not fit their slot: %A" j) where

    /// Every node kind a value holds, at any depth.
    let rec private nodeTags (v: IdlValue) : Set<string> =
        let ofFields (fs: (string * IdlValue) list) =
            fs |> List.map (snd >> nodeTags) |> Set.unionMany

        match v with
        | VNode(_, tag, fs) -> Set.add tag (ofFields fs)
        | VNodeEnv(_, env, tag, fs) -> Set.add tag (Set.union (ofFields fs) (ofFields env))
        | VList xs -> xs |> List.map nodeTags |> Set.unionMany
        | VMap entries
        | VRecord entries
        | VUnion(_, entries) -> ofFields entries
        | _ -> Set.empty


    /// The implementation of `FStarTarget.vectorsModule` and `vectorsModuleWith` — the published
    /// entries, which carry the contract.
    let vectorsModule
        (moduleName: string)
        (models: VectorModel list)
        (decode: DecodeVectors list)
        : Result<string, CodegenError> =
        // The kinds the committed model declares: every kind the target can express.
        let modelledTags (vm: VectorModel) =
            partition vm.Idl |> List.filter _.Refusal.IsNone |> List.map _.Tag

        // One accepted vector's two definitions, named `<b>_<v><i>` and `<b>_<e><i>`: the model value
        // of what the interpreter decodes the bytes to, and the `jval` the bytes parse to.
        let definitions (vm: VectorModel) (mdl: Model) (vName: string) (eName: string) (i: int) (wire: string) =
            let q = vm.Prefix + "."
            // A top-level F* binding must start lower-case; the abbreviation must not.
            let b = vm.Prefix.ToLowerInvariant()
            let modelled = Set.ofList (modelledTags vm)
            let where = sprintf "%s vector %d" vm.Model i

            match Decode.decode vm.Idl wire, Json.parse wire with
            | Error m, _ -> unmodellable ("a wire the interpreter refuses: " + m) where
            | _, Error m -> unmodellable ("a wire that does not parse: " + m) where
            | Ok v, _ when not (Set.isSubset (nodeTags v) modelled) ->
                unmodellable
                    ("a node of a kind the model does not declare ("
                     + (Set.difference (nodeTags v) modelled |> String.concat ", ")
                     + ")")
                    where
            | Ok v, Ok j ->
                match valueTerm vm.Idl mdl q where SNode v, wireTerm vm.Idl mdl where SNode j with
                | Error e, _
                | _, Error e -> Error e
                | Ok vt, Ok jt ->
                    Ok(
                        sprintf
                            "let %s_%s%d : %snode int string =\n  %s\n\nlet %s_%s%d : jval int string =\n  %s\n"
                            b
                            vName
                            i
                            q
                            vt
                            b
                            eName
                            i
                            jt
                    )

        let block (vm: VectorModel) : Result<string, CodegenError> =
            let q = vm.Prefix + "."
            let b = vm.Prefix.ToLowerInvariant()

            // Phase 525 — the terms read the members of the model itself, not a re-resolution.
            model vm.Idl (modelledTags vm)
            |> Result.bind (fun mdl -> vm.Wires |> List.mapi (definitions vm mdl "v" "e") |> sequenceR)
            |> Result.map (fun defs ->
                let facts =
                    vm.Wires
                    |> List.mapi (fun i _ -> sprintf "  assert_norm (%senc_node %s_v%d == %s_e%d)" q b i b i)
                    |> String.concat ";\n"

                String.concat "\n" defs
                + sprintf "\nlet %s_vectors_agree : unit =\n%s\n" b (if facts = "" then "  ()" else facts))

        // Phase 722 — the DECODE facts: the model's `dec_node` on a document the interpreter reads,
        // and on one it refuses.
        let decodeBlock (dv: DecodeVectors) : Result<string, CodegenError> =
            let vm = dv.Read
            let q = vm.Prefix + "."
            let b = vm.Prefix.ToLowerInvariant()

            // A refused document has no slot walk, so it is carried verbatim; a number that is not an
            // integer would need its slot's float carrier, and is refused by name.
            let rec integral (j: JVal) =
                match j with
                | JFloat _ -> false
                | JArr xs -> xs |> List.forall integral
                | JObj fs -> fs |> List.forall (snd >> integral)
                | _ -> true

            let refused (i: int) (wire: string) =
                let where = sprintf "%s refused vector %d" vm.Model i

                match Decode.decode vm.Idl wire, Json.parse wire with
                | Ok _, _ -> unmodellable "a document the interpreter accepts, named as one it refuses" where
                | _, Error m -> unmodellable ("a wire that does not parse: " + m) where
                | Error _, Ok j when not (integral j) ->
                    unmodellable "a refused document holding a number that is not an integer" where
                | Error _, Ok j ->
                    jsonTerm where j
                    |> Result.map (fun jt -> sprintf "let %s_dr%d : jval int string =\n  %s\n" b i jt)

            model vm.Idl (modelledTags vm)
            |> Result.bind (fun mdl ->
                (vm.Wires |> List.mapi (definitions vm mdl "dv" "de"))
                @ (dv.Refused |> List.mapi refused)
                |> sequenceR)
            |> Result.map (fun defs ->
                let facts =
                    (vm.Wires
                     |> List.mapi (fun i _ -> sprintf "  assert_norm (%sdec_node %s_de%d == Ok %s_dv%d)" q b i b i))
                    @ (dv.Refused
                       |> List.mapi (fun i _ -> sprintf "  assert_norm (Error? (%sdec_node %s_dr%d))" q b i))
                    |> String.concat ";\n"

                String.concat "\n" defs
                + sprintf "\nlet %s_decode_agree : unit =\n%s\n" b (if facts = "" then "  ()" else facts))

        // A model named only by its decode vectors is opened too.
        let opened =
            models @ (decode |> List.map _.Read)
            |> List.distinctBy (fun vm -> vm.Prefix, vm.Model)

        (models |> List.map block) @ (decode |> List.map decodeBlock)
        |> sequenceR
        |> Result.map (fun blocks ->
            let header =
                [ "(*"
                  "   "
                  + moduleName
                  + " — the interpreter's vectors over the generated models (Phase 303)."
                  "   DO NOT EDIT: `FStarTarget.vectorsModule` regenerates it from the three-way"
                  "   differential's vectors, and a generation diff holds it to that."
                  ""
                  "   For each vector, the reference interpreter's canonical bytes are parsed into the model's"
                  "   `jval` (instantiated at `num = int`, `flt = string`, a float carried as its canonical"
                  "   layout), and the value the interpreter DECODES those bytes to is written as the generated"
                  "   model's own value; `assert_norm` has the normaliser evaluate the model's `enc_node` on"
                  "   the value and compare it with the parsed bytes, member for member and key for key. The"
                  "   generated model and the interpreter agree on every vector, or this module does not"
                  "   check. What the facts cannot see, by construction: the float LAYOUT (`flt` is opaque to"
                  "   the model) and the WIRE_FORMAT section-7 sentinels (a non-finite float is spelled as a"
                  "   string the model's float slot does not carry) — the three-way differential covers both." ]
                @ (if List.isEmpty decode then
                       []
                   else
                       [ ""
                         "   DECODE facts (Phase 722), for the models that name them (`<prefix>_decode_agree`): the"
                         "   normaliser evaluates the model's `dec_node` on the `jval` a document's bytes parse to."
                         "   For a document the interpreter READS, the answer is `Ok` of the model value of what the"
                         "   interpreter decodes; for a document the interpreter REFUSES, it is an `Error`. These"
                         "   hold the model's reading of a document to the interpreter's on the documents given:"
                         "   which case of a presence-keyed union the keys present pick, and that two cases left,"
                         "   or a synthesised member absent or at another value, is refused. The generated round"
                         "   trip speaks only of what the encoder writes. A refused document is carried verbatim,"
                         "   and the facts state THAT the model refuses, not the refusal's code or path." ])
                @ [ "*)"; "module " + moduleName; ""; "open WireDecode" ]
                @ (opened |> List.map (fun vm -> "module " + vm.Prefix + " = " + vm.Model))
                |> String.concat "\n"

            header + "\n\n" + String.concat "\n" blocks
            |> fstarStamp "F* vectors" (opened |> List.map _.Idl))
