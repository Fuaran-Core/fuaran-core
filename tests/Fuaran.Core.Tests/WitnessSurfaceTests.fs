module Fuaran.Core.Tests.WitnessSurfaceTests

// The mechanical backstop for the 1.0 witness-record field freeze (STABILITY.md
// "Witness-record field freeze"). The four witness records are plain records, so a
// field addition is a silent compile-break for every adopter's construction site — and
// exactly the kind of change a well-meaning refactor makes without noticing the blast
// radius. These tests reflect each frozen record's field SET and pin it: adding,
// removing, or renaming a field fails here first, forcing the change through the freeze
// policy (compose a new witness that embeds the frozen one; do not grow it).

open Expecto
open FSharp.Reflection
open Fuaran.Core

let private fieldNames (t: System.Type) : string list =
    FSharpType.GetRecordFields t
    |> Array.map (fun p -> p.Name)
    |> Array.toList
    |> List.sort

[<Tests>]
let tests =
    testList
        "witness surface freeze (1.0 contract)"
        [ testCase "IdWitness fields are frozen"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<IdWitness<obj>>)
                  [ "Equals"; "OfString"; "ToString" ]
                  "IdWitness field set is frozen — see STABILITY.md 'Witness-record field freeze'"

          testCase "NodeWitness fields are frozen"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<NodeWitness<obj, obj>>)
                  [ "Children"; "Id"; "KindTag"; "ReplaceChildren" ]
                  "NodeWitness field set is frozen — compose a new witness embedding it, never add a field"

          testCase "StreamWitness fields are frozen"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<StreamWitness<obj, obj, obj>>)
                  [ "Apply"; "Decode"; "Encode" ]
                  "StreamWitness field set is frozen — metadata rides in the opaque 'Op (F8), never a new field"

          testCase "ArtifactWitness fields are frozen (and embed — not grow — the base witnesses)"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<ArtifactWitness<obj, obj>>)
                  [ "Bind"; "Effect"; "Holes"; "IdW"; "Tree" ]
                  "ArtifactWitness is the composition precedent: it embeds NodeWitness (Tree) + IdWitness (IdW)"

          testCase "AiSurfaceWitness fields are frozen"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<AiSurfaceWitness<obj, obj, obj>>)
                  [ "Apply"; "Decide"; "Explain"; "KindOfOp"; "OpKinds"; "Patterns"; "ReadTools" ]
                  "AiSurfaceWitness field set is frozen — see STABILITY.md 'Witness-record field freeze'"

          testCase "ProjectionWitness fields are frozen (and embeds — not grows — the base witnesses)"
          <| fun _ ->
              Expect.equal
                  (fieldNames typeof<ProjectionWitness<obj, obj, obj>>)
                  [ "Encode"; "IdW"; "ParseBack"; "Snippet"; "Tree" ]
                  "ProjectionWitness embeds NodeWitness (Tree) + IdWitness (IdW); never grow it" ]
