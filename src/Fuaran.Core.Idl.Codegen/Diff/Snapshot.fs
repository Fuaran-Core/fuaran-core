namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's READER: one `idl.json` revision (and its `support.json`) into a
/// typed `Snapshot`. Phase 310 — a TOLERANT classifier by design: an unrecognised shape degrades
/// to a placeholder rather than refusing, because a revision written before a key existed must
/// still compare. So it reads every member through the typed decode layer and DISCARDS a refusal
/// deliberately, at the three adapters at the head of the module and nowhere else.
module internal DiffSnapshot =

    let private field (name: string) (v: JVal) : JVal option = Decoder.tryMember name v

    let private str (name: string) (v: JVal) : string option =
        Decoder.optField name Decoder.str v |> Result.defaultValue None

    let private arr (name: string) (v: JVal) : JVal list =
        Decoder.fieldOr name [] Decoder.items v |> Result.defaultValue []

    /// The type object with its `hostSurface` key removed — the wire-observable
    /// part. Recursive, because a `list<fn>` hides one a level down.
    let rec private stripHostSurface (v: JVal) : JVal =
        match v with
        | JObj fs ->
            fs
            |> List.filter (fun (k, _) -> k <> "hostSurface")
            |> List.map (fun (k, fv) -> k, stripHostSurface fv)
            |> JObj
        | JArr xs -> JArr(xs |> List.map stripHostSurface)
        | scalar -> scalar

    /// Only the `hostSurface` blocks, keyed by the path they sit at — so a
    /// change to a nested closure's F# signature is still attributable.
    let rec private hostSurfaceOnly (path: string) (v: JVal) : (string * JVal) list =
        match v with
        | JObj fs ->
            [ for (k, fv) in fs do
                  if k = "hostSurface" then
                      yield path, fv
                  else
                      yield! hostSurfaceOnly (path + "/" + k) fv ]
        | JArr xs ->
            xs
            |> List.mapi (fun i x -> hostSurfaceOnly (sprintf "%s/%d" path i) x)
            |> List.concat
        | _ -> []

    /// A readable one-line rendering of a field type. Display only — every
    /// equality decision runs on the canonical strings, so an unrecognised
    /// shape here degrades to raw JSON rather than to a wrong verdict.
    let rec private typeLabel (v: JVal) : string =
        let tag = str "$type" v |> Option.defaultValue "?"

        let named () = str "name" v |> Option.defaultValue "?"

        match tag with
        | "str"
        | "int"
        | "bool"
        | "float"
        | "node"
        | "kind"
        | "op"
        | "json"
        | "closure"
        | "opaque" -> tag
        | "enum" -> "enum " + named ()
        | "record" -> "record " + named ()
        | "var" -> "'" + named ()
        | "union" ->
            match arr "args" v with
            | [] -> "union " + named ()
            | args -> sprintf "union %s<%s>" (named ()) (args |> List.map typeLabel |> String.concat ", ")
        | "list" ->
            match field "of" v with
            | Some inner -> sprintf "list<%s>" (typeLabel inner)
            | None -> "list<?>"
        | "map" ->
            match field "values" v with
            | Some inner -> sprintf "map<string, %s>" (typeLabel inner)
            | None -> "map<string, ?>"
        | "fn" ->
            match field "hostSurface" v |> Option.bind (str "fsharp") with
            | Some sg -> sprintf "fn(%s)" sg
            | None -> "fn"
        | "hosted" ->
            match field "hostSurface" v |> Option.bind (str "fsharp") with
            | Some h -> sprintf "hosted(%s)" h
            | None -> "hosted"
        | other -> other

    /// The member-ordered, number-normalised form of a JSON value: its canonical text, read back
    /// once at snapshot time, so two values are structurally equal exactly when their canonical
    /// texts are. No rule re-parses anything after this (Phase 524).
    let private canonical (v: JVal) : JVal =
        match Json.parse (Canonical.write Rendering.CrossHost v) with
        | Ok c -> c
        | Error _ -> v

    /// The optionality object as a typed presence — an `omitDefault`'s value included, an
    /// unrecognised or absent `$type` kept by name (`?` when absent).
    let private presence (v: JVal) : PresenceSnap =
        match str "$type" v with
        | Some "required" -> PresenceSnap.Required
        | Some "optional" -> PresenceSnap.Optional
        | Some "hostOnly" -> PresenceSnap.HostOnly
        | Some "omitDefault" -> PresenceSnap.OmitDefault(field "default" v |> Option.map canonical)
        | Some t -> PresenceSnap.Unrecognised t
        | None -> PresenceSnap.Unrecognised "?"

    /// The annotation set as a canonical string — `""` when the key is absent,
    /// which is both "declares none" and "this revision predates the key". The two
    /// are deliberately indistinguishable: an artifact written before Phase 113
    /// describes a vocabulary that annotated nothing, so reading them alike is
    /// correct rather than merely convenient.
    let private readAnnotations (v: JVal) : string =
        match field "annotations" v with
        | Some a -> Canonical.write Rendering.CrossHost a
        | None -> ""

    let private readField (v: JVal) : FieldSnap option =
        match str "name" v, field "type" v, field "optionality" v with
        | Some name, Some ty, Some opt ->
            Some
                { Name = name
                  Label = typeLabel ty
                  Wire =
                    { Tag = str "$type" ty |> Option.defaultValue "?"
                      Shape = canonical (stripHostSurface ty) }
                  Host = { Blocks = hostSurfaceOnly "" ty |> List.map (fun (p, h) -> p, canonical h) }
                  Presence = presence opt
                  Annotations = readAnnotations v
                  Aliases =
                    arr "aliases" v
                    |> List.choose (function
                        | JStr s -> Some s
                        | _ -> None)
                  PresenceCondition =
                    match field "presence" v with
                    | Some c -> Canonical.write Rendering.CrossHost c
                    | None -> "" }
        | _ -> None

    let private readFields (owner: JVal) : FieldSnap list =
        arr "fields" owner |> List.choose readField

    let private byName (key: JVal -> string option) (project: JVal -> 'a) (xs: JVal list) : Map<string, 'a> =
        xs
        |> List.choose (fun x -> key x |> Option.map (fun k -> k, project x))
        |> Map.ofList

    /// The support document's declared entries as one flat map of canonical texts — the
    /// shape the diff walk pairs by key.
    let private supportEntries (doc: SupportDocument) : Map<string, string> =
        let sup = doc.Support

        let projection (p: KindProjection) =
            String.concat
                "\n--\n"
                ([ p.SpecDecl
                   p.Encoder
                   p.Decoder
                   (match p.Mk with
                    | Some mk -> mk
                    | None -> "") ]
                 // Phase 403 — appended only when declared, so a projection declaring neither
                 // compares exactly as it did before they existed.
                 @ (match p.MapMsg with
                    | Some map -> [ "mapMsg:\n" + map ]
                    | None -> [])
                 @ (match p.RecordFields with
                    | Some fs ->
                        [ "recordFields:\n"
                          + Canonical.write Rendering.CrossHost (CodegenLookup.fieldsJson fs) ]
                    | None -> []))

        [ for KeyValue(path, lines) in sup.Docs -> "doc:" + path, String.concat "\n" lines
          match sup.TypeSplice with
          | Some t -> yield "splice:type", t
          | None -> ()
          match sup.EncodeSplice with
          | Some t -> yield "splice:encode", t
          | None -> ()
          match sup.DecodeSplice with
          | Some t -> yield "splice:decode", t
          | None -> ()
          match sup.AccessorSplice with
          | Some t -> yield "splice:accessor", t
          | None -> ()
          for KeyValue(case, expr) in sup.CaseRefines -> "refine:" + case, expr
          for KeyValue(kind, proj) in sup.KindProjections -> "projection:" + kind, projection proj
          match doc.HostPrelude with
          | Some pre -> yield "prelude", pre.Module + "/" + pre.Path
          | None -> () ]
        |> Map.ofList

    /// Read one `idl.json` revision. Tolerant of keys the revision predates
    /// (`ops`, `hostCases`, `transparentCase` are all emitted conditionally) —
    /// their absence is read as empty, which is what the emitter means by it.
    ///
    /// No support document is read, and a malformed member is skipped, so the only refusal is
    /// a root that is not a JSON object.
    let private ofArtifact (artifact: JVal) : Result<Snapshot, string> =
        match artifact with
        | JObj _ ->
            let version =
                match field "version" artifact with
                | Some(JInt i) -> i
                | _ -> 0

            let kinds = arr "kinds" artifact
            let ops = arr "ops" artifact

            Ok
                { Version = version
                  Kinds = kinds |> byName (str "tag") readFields
                  KindCategory =
                    kinds
                    |> byName (str "tag") (fun k -> str "category" k |> Option.defaultValue "")
                  KindAnnotations = kinds |> byName (str "tag") readAnnotations
                  Ops = ops |> byName (str "tag") readFields
                  OpAnnotations = ops |> byName (str "tag") readAnnotations
                  Unions =
                    arr "unions" artifact
                    |> byName (str "name") (fun u ->
                        let cases = arr "cases" u

                        { Params =
                            match field "params" u with
                            | Some(JArr ps) ->
                                ps
                                |> List.choose (function
                                    | JStr s -> Some s
                                    | _ -> None)
                            | _ -> []
                          Cases =
                            cases
                            |> byName (str "tag") (fun c ->
                                { Fields = readFields c
                                  Annotations = readAnnotations c
                                  Hosted =
                                    field "hosted" c
                                    |> Option.map (Canonical.write Rendering.CrossHost)
                                    |> Option.defaultValue "" })
                          CaseOrder = cases |> List.choose (str "tag")
                          TransparentCase = str "transparentCase" u
                          TagRule = str "tagRule" u |> Option.defaultValue "caseName" })
                  Enums =
                    arr "enums" artifact
                    |> byName (str "name") (fun e ->
                        let strings key =
                            arr key e
                            |> List.choose (function
                                | JStr s -> Some s
                                | _ -> None)

                        { WireCases = strings "cases"
                          HostCases = strings "hostCases"
                          CaseAnnotations =
                            match field "caseAnnotations" e with
                            | Some(JObj entries) ->
                                entries
                                |> List.map (fun (wire, block) -> wire, Canonical.write Rendering.CrossHost block)
                                |> Map.ofList
                            | _ -> Map.empty
                          CaseAliases =
                            match field "aliases" e with
                            | Some(JObj entries) ->
                                entries
                                |> List.map (fun (wire, xs) ->
                                    wire,
                                    (match xs with
                                     | JArr items ->
                                         items
                                         |> List.choose (function
                                             | JStr s -> Some s
                                             | _ -> None)
                                     | _ -> []))
                                |> Map.ofList
                            | _ -> Map.empty })
                  Records = arr "records" artifact |> byName (str "name") readFields
                  TypeLayouts =
                    (arr "records" artifact
                     |> List.choose (fun r ->
                         match str "name" r, field "inline" r with
                         | Some n, Some(JBool true) -> Some(("record", n), "inline")
                         | _ -> None))
                    @ (arr "unions" artifact
                       |> List.choose (fun u ->
                           match str "name" u, field "byPresence" u with
                           | Some n, Some rules -> Some(("union", n), Canonical.write Rendering.CrossHost rules)
                           | _ -> None))
                    |> Map.ofList
                  TypeAnnotations =
                    [ "union", "unions"; "record", "records"; "enum", "enums" ]
                    |> List.collect (fun (family, key) ->
                        arr key artifact
                        |> byName (str "name") readAnnotations
                        |> Map.toList
                        |> List.map (fun (name, ann) -> (family, name), ann))
                    |> Map.ofList
                  Defaults =
                    arr "defaults" artifact
                    |> List.choose (fun d ->
                        match str "kind" d, str "field" d, field "value" d with
                        | Some k, Some f, Some v -> Some((k, f), Canonical.write Rendering.CrossHost v)
                        | _ -> None)
                    |> Map.ofList
                  NodeFields = arr "nodeFields" artifact |> List.choose readField
                  Support = Map.empty
                  Wire =
                    match field "wire" artifact with
                    | Some w ->
                        (str "discriminator" w |> Option.defaultValue "$type")
                        + "/"
                        + (str "nodeEnvelope" w |> Option.defaultValue "nestedKind")
                        + "/"
                        + (str "keyOrder" w |> Option.defaultValue "sorted")
                    | None -> "$type/nestedKind/sorted"
                  Harden =
                    // Phase 180 — an absent block reads as the UNDECLARED policy, and it
                    // does so by walking the SAME members over an empty object rather
                    // than by a second answer beside them. This used to be a match whose
                    // `None` arm carried a literal second copy of the engine's old
                    // hard-coded default; it moved with `Artifact.readHarden`, because a
                    // classifier that disagrees with the reader about what an artifact
                    // MEANS is worse than either answer alone, and it is written this way
                    // so the two cannot drift apart again.
                    let h = field "harden" artifact |> Option.defaultValue (JObj [])

                    [ "gatedKind"
                      "placeholderKind"
                      "placeholderField"
                      "textLiteralCase"
                      "textLiteralField"
                      "valueLiteralCase"
                      "valueLiteralField" ]
                    |> List.map (fun k -> str k h |> Option.defaultValue "")
                    |> String.concat "/"
                    |> fun tokens ->
                        tokens
                        + "/"
                        + (arr "transparentUnions" h
                           |> List.map (fun e ->
                               (str "union" e |> Option.defaultValue "")
                               + "."
                               + (str "case" e |> Option.defaultValue ""))
                           |> String.concat ",") }
        | _ -> Error "idl.json: expected a JSON object at the root"

    /// Phase 293 — a snapshot with the support document beside the artifact joined in;
    /// `None` reads as a vocabulary with no declared support.
    let read (artifact: JVal) (support: JVal option) : Result<Snapshot, string> =
        match support with
        | None -> ofArtifact artifact
        | Some sv ->
            SupportArtifact.ofJson sv
            |> Result.mapError (fun e -> "support: " + e)
            |> Result.bind (fun doc ->
                ofArtifact artifact
                |> Result.map (fun snap ->
                    { snap with
                        Support = supportEntries doc }))

    /// Parse + read in one step — the artifact's text and, when given, the support document's.
    /// `Error` names `support:` when the support text is the one that failed.
    let parse (text: string) (supportText: string option) : Result<Snapshot, string> =
        match supportText with
        | None -> Json.parse text |> Result.bind ofArtifact
        | Some st ->
            Json.parse st
            |> Result.mapError (fun e -> "support: " + e)
            |> Result.bind (fun sv -> Json.parse text |> Result.bind (fun av -> read av (Some sv)))
