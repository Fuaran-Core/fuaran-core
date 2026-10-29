module Fuaran.Core.Tests.IdentityRuleTests

// Phase 578 — the identity rule (`docs/identity-rule.md`) certified at the reference. The committed
// `conformance/identity/` table is held to its render by the exporter table; what these cases ask is
// whether what it records is still TRUE of the shipped code: each line re-minted through
// `ContentAddress.ofValue`, each relation the rule names, and the rule's tie to the vocabulary's own
// canonical bytes. Each comparison has a go-red beside it.

open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Core.Idl

type private Line =
    { Name: string
      Profile: EncodingProfile
      Input: string
      Canonical: string
      Address: string }

let private parseLine (text: string) : Line =
    match text.Split '\t' with
    | [| name; profile; input; canonical; address |] ->
        match EncodingProfile.tryParse profile with
        | Some p ->
            { Name = name
              Profile = p
              Input = input
              Canonical = canonical
              Address = address }
        | None -> failtestf "line '%s' names no profile ('%s')" name profile
    | fields -> failtestf "a line carries %d fields, not 5: %s" fields.Length text

/// The committed table's lines.
let private committed () : Line list =
    let text = File.ReadAllText(Snapshots.repoFile "conformance/identity/identity.json")

    match Json.parse text with
    | Ok(JObj members) ->
        match members |> List.tryFind (fun (k, _) -> k = "lines") with
        | Some(_, JArr items) ->
            items
            |> List.map (function
                | JStr s -> parseLine s
                | other -> failtestf "a line is not a string: %A" other)
        | _ -> failtest "the table carries no `lines`"
    | _ -> failtest "the table is not a JSON object"

/// The rule applied through the SHIPPED constructor: read through the vocabulary, the canonical form,
/// `ContentAddress.ofValue` under the line's profile.
let private mint (profile: EncodingProfile) (input: string) : Result<string * string, string> =
    IdentityIdl.read input
    |> Result.bind IdentityIdl.canonicalValue
    |> Result.bind (fun canonical ->
        ContentAddress.ofValue profile canonical
        |> Result.map (fun d -> Canonical.write (Rendering.ofProfile profile) canonical, Digest.print d))

/// Every disagreement between a line and the rule re-applied to its input.
let private disagreements (lines: Line list) : string list =
    [ for l in lines do
          match mint l.Profile l.Input with
          | Error e -> yield sprintf "%s: the rule refused its input (%s)" l.Name e
          | Ok(canonical, address) ->
              if canonical <> l.Canonical then
                  yield sprintf "%s: canonical text differs" l.Name

              if address <> l.Address then
                  yield sprintf "%s: address differs" l.Name ]

let private addressOf (lines: Line list) (name: string) =
    match lines |> List.tryFind (fun l -> l.Name = name) with
    | Some l -> l.Address
    | None -> failtestf "no vector named '%s'" name

let private declared: IdentityIdl.Declared =
    { Method = "sha256:" + Hash.sha256Hex "ordinary least squares, revision 3"
      Sources = [ "ledger/2026-q3#a1"; "survey/2026-09#b2" ]
      Value = "12.34"
      Unit = None }

[<Tests>]
let identityTests =
    testList
        "Identity rule: a declared record's identity is its content address under a named profile (Phase 578)"
        [ testCase "the reference vocabulary declares cleanly and is a fixture, not a Core type"
          <| fun _ ->
              Expect.isEmpty (Declare.errors IdentityIdl.idl) "the vocabulary has no declaration fault"

              let coreTypes =
                  [ for a in System.AppDomain.CurrentDomain.GetAssemblies() do
                        if
                            a.GetName().Name.StartsWith "Fuaran.Core"
                            && not (a.GetName().Name.Contains "Tests")
                        then
                            for t in a.GetExportedTypes() do
                                if t.Name = "Fact" then
                                    yield t.FullName ]

              Expect.isEmpty coreTypes "no shipped Core assembly declares a Fact type"

              Expect.equal
                  (typeof<IdentityIdl.Declared>.Assembly.GetName().Name)
                  "Fuaran.Core.Tests.Fixtures"
                  "the reference vocabulary lives in the fixtures library"

          testCase "every committed vector re-mints through ContentAddress.ofValue, byte for byte"
          <| fun _ ->
              let lines = committed ()

              Expect.equal
                  (List.length lines)
                  (List.length CertificationVectors.identityVectors)
                  "every vector is committed"

              Expect.isEmpty (disagreements lines) "each line is the rule applied to its input"

          testCase "go-red: a line whose address or canonical text moved is named"
          <| fun _ ->
              let lines = committed ()
              let first = List.head lines

              let flipped (s: string) =
                  s.Substring(0, s.Length - 1) + (if s.EndsWith "0" then "1" else "0")

              let wrongAddress =
                  { first with
                      Address = flipped first.Address }

              let wrongText =
                  { first with
                      Canonical = first.Canonical.Replace("12.34", "12.340") }

              Expect.equal (disagreements [ wrongAddress ]) [ first.Name + ": address differs" ] "a moved address"
              Expect.equal (disagreements [ wrongText ]) [ first.Name + ": canonical text differs" ] "a moved text"

          testCase "the pairs the rule names: one address where it says one, two where it says two"
          <| fun _ ->
              let lines = committed ()

              for a, b, same in CertificationVectors.identityRelations do
                  let ea, eb = addressOf lines a, addressOf lines b

                  if same then
                      Expect.equal ea eb (sprintf "'%s' and '%s' are one identity" a b)
                  else
                      Expect.notEqual ea eb (sprintf "'%s' and '%s' are two identities" a b)

          testCase "under v2 the address is the digest of the vocabulary's own canonical bytes"
          <| fun _ ->
              for v in
                  CertificationVectors.identityVectors
                  |> List.filter (fun v -> v.Profile = EncodingProfile.V2) do
                  let text =
                      match IdentityIdl.read v.Input |> Result.bind (Encode.encodeOp IdentityIdl.idl) with
                      | Ok t -> t
                      | Error e -> failtestf "%s: %s" v.Name e

                  let viaText =
                      ContentAddress.ofCanonicalText EncodingProfile.V2 text
                      |> Result.map Digest.print

                  Expect.equal (viaText |> Result.map Some) (mint v.Profile v.Input |> Result.map (snd >> Some)) v.Name

          testCase "a record built in memory mints the address of the same record received as text"
          <| fun _ ->
              let viaMemory =
                  IdentityIdl.authored declared
                  |> Result.bind IdentityIdl.canonicalValue
                  |> Result.bind (ContentAddress.ofValue EncodingProfile.V2)
                  |> Result.map Digest.print

              Expect.equal viaMemory (Ok(addressOf (committed ()) "declared order")) "one record, one address"

              let atDefault =
                  IdentityIdl.authored
                      { declared with
                          Unit = Some IdentityIdl.DefaultUnit }
                  |> Result.bind IdentityIdl.canonicalValue
                  |> Result.bind (ContentAddress.ofValue EncodingProfile.V2)
                  |> Result.map Digest.print

              Expect.equal atDefault viaMemory "the default is not materialised"

          testCase "a value that is not its own canonical decimal text is refused, not minted"
          <| fun _ ->
              Expect.isError (IdentityIdl.authored { declared with Value = "12.340" }) "a trailing zero"
              Expect.isError (IdentityIdl.authored { declared with Value = "twelve" }) "not a number"

              Expect.equal
                  (DecimalText.tryCanonical "12.340")
                  (Some "12.34")
                  "the vocabulary's one text per number is DecimalText's"

          testCase "the reader's refusals stand: a member the vocabulary does not declare mints nothing"
          <| fun _ ->
              let input = (List.head CertificationVectors.identityVectors).Input
              Expect.isError (mint EncodingProfile.V2 (input.Replace("\"value\"", "\"amount\""))) "a renamed member" ]
