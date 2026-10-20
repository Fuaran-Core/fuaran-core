namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's WALK: every difference between two snapshots, as `Change`s in
/// rule order.
module internal DiffChanges =

    /// The addition and removal of a field are a `FieldOptionalityChanged` seen
    /// from too far away only when the name matches; everything else pairs by
    /// name too, so field diffing is one shared walk.
    let private diffFields (owner: Owner) (before: FieldSnap list) (after: FieldSnap list) : Change list =
        let b = before |> List.map (fun f -> f.Name, f) |> Map.ofList
        let a = after |> List.map (fun f -> f.Name, f) |> Map.ofList

        [ for f in after do
              match Map.tryFind f.Name b with
              | None -> yield FieldAdded(owner, f)
              | Some old ->
                  if old.Wire <> f.Wire then
                      yield FieldTypeChanged(owner, f.Name, old, f)
                  elif old.Host <> f.Host then
                      yield FieldHostSurfaceChanged(owner, f.Name, old.Host, f.Host)

                  if old.Presence <> f.Presence then
                      yield FieldOptionalityChanged(owner, f.Name, old, f)

                  if old.Annotations <> f.Annotations then
                      yield FieldAnnotationsChanged(owner, f.Name, old.Annotations, f.Annotations)

          for f in before do
              if not (Map.containsKey f.Name a) then
                  yield FieldRemoved(owner, f.Name, f) ]

    let private diffNamed
        (added: string -> Change)
        (removed: string -> Change)
        (inner: string -> 'a -> 'a -> Change list)
        (before: Map<string, 'a>)
        (after: Map<string, 'a>)
        : Change list =
        [ for KeyValue(name, av) in after do
              match Map.tryFind name before with
              | None -> yield added name
              | Some bv -> yield! inner name bv av

          for KeyValue(name, _) in before do
              if not (Map.containsKey name after) then
                  yield removed name ]

    /// Rename inference — a removed name and an added name whose wire-observable
    /// field signature is IDENTICAL, and which pair uniquely on both sides.
    ///
    /// Deliberately conservative and deliberately additional. There is nothing in
    /// the artifact that records a rename (the wire has no identity beyond the
    /// `$type` string), so any detection is a guess about intent; a wrong guess
    /// that SUPPRESSED the add + remove would hide a breaking change behind a
    /// friendlier-sounding one. So a rename is reported beside them, and the
    /// classification of the pair is unaffected by whether the guess landed.
    let private renamePairs
        (make: string * string -> Change)
        (before: Map<string, FieldSnap list>)
        (after: Map<string, FieldSnap list>)
        : Change list =
        let sign (fs: FieldSnap list) =
            fs
            |> List.map (fun f -> f.Name + ":" + f.Wire.Text + ":" + f.Presence.Label)
            |> String.concat "|"

        let gone =
            [ for KeyValue(n, fs) in before do
                  if not (Map.containsKey n after) then
                      yield n, sign fs ]

        let fresh =
            [ for KeyValue(n, fs) in after do
                  if not (Map.containsKey n before) then
                      yield n, sign fs ]

        [ for (oldName, s) in gone do
              // Unique on both sides, and no empty-signature pairing: a
              // field-less kind matches every other field-less kind, which is a
              // coincidence, not a rename.
              if s <> "" then
                  match fresh |> List.filter (fun (_, fs) -> fs = s) with
                  | [ (newName, _) ] when (gone |> List.filter (fun (_, bs) -> bs = s) |> List.length) = 1 ->
                      yield make (oldName, newName)
                  | _ -> () ]

    /// Every difference between two snapshots, sorted by rule rank then by the change's
    /// own key, so identical inputs give an identical list. A kind rename is INFERRED and
    /// reported beside its add and remove, never instead of them; annotation and category
    /// moves are reported only for members both revisions carry.
    let changes (before: Snapshot) (after: Snapshot) : Change list =
        let unordered =
            [ if before.Version <> after.Version then
                  ArtifactVersionChanged(before.Version, after.Version)

              if before.Wire <> after.Wire then
                  WireShapeChanged(before.Wire, after.Wire)

              if before.Harden <> after.Harden then
                  HardenPolicyChanged(before.Harden, after.Harden)

              yield!
                  diffNamed KindAdded KindRemoved (fun tag b a -> diffFields (OKind tag) b a) before.Kinds after.Kinds

              yield! renamePairs KindRenamed before.Kinds after.Kinds

              for KeyValue(tag, cat) in after.KindCategory do
                  match Map.tryFind tag before.KindCategory with
                  | Some old when old <> cat -> KindCategoryChanged(tag, old, cat)
                  | _ -> ()

              // Phase 119 — a kind's own annotations, reported only for a tag both
              // revisions carry: a kind that arrived or left is already `KindAdded` /
              // `KindRemoved`, and saying it also gained annotations adds nothing.
              for KeyValue(tag, ann) in after.KindAnnotations do
                  match Map.tryFind tag before.KindAnnotations with
                  | Some old when old <> ann -> KindAnnotationsChanged(OKind tag, old, ann)
                  | _ -> ()

              yield! diffNamed OpAdded OpRemoved (fun tag b a -> diffFields (OOp tag) b a) before.Ops after.Ops

              for KeyValue(tag, ann) in after.OpAnnotations do
                  match Map.tryFind tag before.OpAnnotations with
                  | Some old when old <> ann -> KindAnnotationsChanged(OOp tag, old, ann)
                  | _ -> ()

              yield!
                  diffNamed
                      UnionAdded
                      UnionRemoved
                      (fun name b a ->
                          [ if b.Params <> a.Params then
                                UnionParamsChanged(name, b.Params, a.Params)

                            if b.TransparentCase <> a.TransparentCase then
                                UnionTransparencyChanged(name, b.TransparentCase, a.TransparentCase)

                            yield!
                                diffNamed
                                    (fun c -> UnionCaseAdded(name, c))
                                    (fun c -> UnionCaseRemoved(name, c))
                                    (fun c bf af ->
                                        [ if bf.Annotations <> af.Annotations then
                                              UnionCaseAnnotationsChanged(name, c, bf.Annotations, af.Annotations)

                                          yield! diffFields (OUnionCase(name, c)) bf.Fields af.Fields ])
                                    b.Cases
                                    a.Cases ])
                      before.Unions
                      after.Unions

              yield!
                  diffNamed
                      EnumAdded
                      EnumRemoved
                      (fun name b a ->
                          [ for w in a.WireCases do
                                if not (List.contains w b.WireCases) then
                                    EnumCaseAdded(name, w)

                            for w in b.WireCases do
                                if not (List.contains w a.WireCases) then
                                    EnumCaseRemoved(name, w)

                            if b.HostCases <> a.HostCases then
                                EnumHostMappingChanged(name, b.HostCases, a.HostCases)

                            // Phase 119 — per-case annotations, over the cases both
                            // revisions carry. A case that arrived or left is already
                            // `EnumCaseAdded` / `EnumCaseRemoved`; an absent entry on
                            // either side reads as `""`, so a first marking and a full
                            // withdrawal both surface here, which is what the classifier
                            // grades `Additive` and `HostSurfaceOnly` respectively.
                            for w in a.WireCases do
                                if List.contains w b.WireCases then
                                    let bw = b.CaseAnnotations |> Map.tryFind w |> Option.defaultValue ""
                                    let aw = a.CaseAnnotations |> Map.tryFind w |> Option.defaultValue ""

                                    if bw <> aw then
                                        EnumCaseAnnotationsChanged(name, w, bw, aw) ])
                      before.Enums
                      after.Enums

              yield!
                  diffNamed
                      RecordAdded
                      RecordRemoved
                      (fun name b a -> diffFields (ORecord name) b a)
                      before.Records
                      after.Records

              // Phase 548 — a type's own annotations, over the types both revisions carry in
              // the same family: one that arrived or left is already its `…Added` / `…Removed`.
              for KeyValue((family, name), ann) in after.TypeAnnotations do
                  match Map.tryFind (family, name) before.TypeAnnotations with
                  | Some old when old <> ann -> TypeAnnotationsChanged(family, name, old, ann)
                  | _ -> ()

              yield! diffFields ONodeEnvelope before.NodeFields after.NodeFields

              for KeyValue((kd, f), v) in after.Defaults do
                  match Map.tryFind (kd, f) before.Defaults with
                  | None -> DefaultAdded(kd, f, v)
                  | Some old when old <> v -> DefaultChanged(kd, f, old, v)
                  | Some _ -> ()

              for KeyValue((kd, f), v) in before.Defaults do
                  if not (Map.containsKey (kd, f) after.Defaults) then
                      DefaultRemoved(kd, f, v)

              // Phase 293 — the declared support, paired by key.
              for KeyValue(key, v) in after.Support do
                  match Map.tryFind key before.Support with
                  | None -> SupportChanged(key, None, Some v)
                  | Some old when old <> v -> SupportChanged(key, Some old, Some v)
                  | Some _ -> ()

              for KeyValue(key, v) in before.Support do
                  if not (Map.containsKey key after.Support) then
                      SupportChanged(key, Some v, None) ]

        unordered |> List.sortBy DiffRules.sortKey
