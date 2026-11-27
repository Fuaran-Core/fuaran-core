module Fuaran.Core.Tests.IdlCanonicalFormEmissionTests

// ============================================================================
//  Phase 723 (DECISIONS.md D190, D288) — the TypeScript codec applies a member's
//  aliases, an enum case's aliases and every presence condition. A vocabulary
//  declaring none of them must see nothing: each fixture vocabulary of the
//  suite's library that declares none emits, from the TypeScript module entry,
//  its derived entry with the collecting decoders, and with the vocabulary
//  constants, the bytes it emitted before the phase (`dc5aa1b0`, measured on a
//  build of that commit), held here as SHA-256.
// ============================================================================

open Expecto
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

let private sha256 (s: string) : string =
    System.Text.Encoding.UTF8.GetBytes s
    |> System.Security.Cryptography.SHA256.HashData
    |> System.Convert.ToHexString
    |> fun h -> h.ToLowerInvariant()

/// Each fixture vocabulary's three emissions before Phase 723: the module, the module with the
/// collecting decoders, and the module with the vocabulary constants.
let private before: (string * (string * string * string)) list =
    [ "mini",
      ("0b472f745c340ceb7e256fa9d124ecb48bdf259ec5be8c5f6a3bb8bd5dc8dde3",
       "1a57055b7c01bec2b14bd175290ab71f86093e219cd2dca76cbf16953718efa7",
       "b62beecd2c0a158fa3a10cb4931b03f393ec3d51315bf851ba3ad7a29f3d99da")
      "document",
      ("236a673348048c700c444a998582d01f6c1362ee8c3946b3445558f279df5fe2",
       "1c5f6764e346dee36ad1b9499055d1e3da38de3ef2f0e388cd454a4656328c29",
       "b4746942ddaf386d4218fe816564f0f486b678a25bce603c585564a6fd6668da")
      "score",
      ("ea0a60ee6a8c7f4a8a485573be71d981fe1500ad627bc91afdc750c2901c46d2",
       "eb40507897578a77688849bdc3727d6f2ed03119063d604c8b059c0bc0b81563",
       "14a6f29ad549814e931f013db3f7dc4eb751f248e9dfd010e55e8b8f7e2f9092")
      "reference",
      ("2e3f1315141b8e551f6d25a68587269f71a4ba7bbb47deb920f46c096d03d770",
       "1e4c5649814ee5d0ed25068980aa34915f7ab54c36f74439223f81bd4729004c",
       "ead8863e59fe7ce205881bbd9d6097d106f85b7b14c6cfd97103073473b9f190")
      "reference, value coverage",
      ("323ac416b9f7d2f7787badf87c042454f07f6dd2c061912ce406d98c4afcb512",
       "9a32a65f62ae67e54970c7efdf6335c1030b7f1ed2a3c930edf8a6ba6b912420",
       "501848ae5cc2f5e21507cedc81b8a9ef89afe0d45ef18ad1b2e86b8814c05c75")
      "reference, unsorted coverage",
      ("323ac416b9f7d2f7787badf87c042454f07f6dd2c061912ce406d98c4afcb512",
       "9a32a65f62ae67e54970c7efdf6335c1030b7f1ed2a3c930edf8a6ba6b912420",
       "501848ae5cc2f5e21507cedc81b8a9ef89afe0d45ef18ad1b2e86b8814c05c75")
      "contracts",
      ("a33d7e0a07de165d69663da0cf24f955ea14910615ac81dfe3e208c6347933f6",
       "2158a50f4c8896c404f3f711a811b2a47d26e5473e5da5389b81886747f3800e",
       "dbf11542d504f08a792cd056dfb0db02757f9ee703b5af7e6a30557d661b05d9")
      "derive",
      ("647523a32c35f07d3ac87fe5a3899964ae05290e9f924c2bc8e90aa62d859bbd",
       "ab8e71e26f2b4577205d0f907890dc7ef1c3fe82c2a21fc97750ad779149c1c1",
       "083b86d524cab25677b772b6c8d52902968e49dd184f3466bc18076e77059fc2")
      "identity",
      ("51bf6ccc6ace36f522fe1e6969298e167011b93f1c5e83e71f6134c311fa070f",
       "d3f5914afcec927b8164d4745569e38d524998b790400204b2c1174396eb0ee5",
       "38d57ce981388623423fc98a0996fa5189bb2a90da301b49338b7c5b1cbcfd35")
      "layout",
      ("0679cec1e540f012a5d177af88b94c219483817a292c8148c13339745e64fe32",
       "8abf8363416a0f42f491fbecfc490b637a7aaad935414ea8600501633fb138c1",
       "d32138ce29bf5b9c9776ed5536ebac2f4bc62f071d3ef55414f9344a4e75474b")
      "layout, flat twin",
      ("5feb26f1b16736e13ed66a7c6d2cde60214c3598d10c0a6a32061a44020eb821",
       "ee444ad114a080b6fa4e67850478be42d2647324103453eb3682e4fd21127fe6",
       "a463e46645b02eb52fbde36d85c1297085786cdf3091b8f3d2e0a69ecba8380d")
      "layout, declared order",
      ("e6c07ffa6a9943f30a5b48eb219e58d06402969cc029dc153bf08b2463841a07",
       "aefd303cccc40c34fd6b60bc02e995996563b158e41c7d4bcc7ed2884377dfa4",
       "246559da104d3c334b7c90beb8a8cd01831774aa61e3ef057c5b7daeb668e5aa")
      "layout, flat twin in declared order",
      ("b54e0a7163ce2b77fedaf4923ab0ca12b5e7cad899d22ed5143845a7cba0db08",
       "70dad910fdba5bed9429fcd42664d9394a6c2b57ed9278d0bf091eb7fb640a89",
       "4433437dab499ee20ded09aa586152dcce4a10ea10db4338a6540eaf9c6a512b")
      "layout holders",
      ("667dab5de81dfaebfb9d23df6a6335a8cd06e62cdeb2d44b65f3bd7bf923afde",
       "a66d47175e76b550360e31e2ad1a04e98d122b69f8ceb7f5d191ad1370f4d873",
       "8172b716655f6334b9d54a4918e9606819a01ba5748edf30f319af7b7d5722a6")
      "layout derived",
      ("7353c51094dc00820bf7c916c5bf0e3196c5c63cd1b90cef8f53ec774c8ef646",
       "2540854a1fd71e7589d49c7e62c28533083ee165f126e760013bb31397145899",
       "bfcdca9f2dffccd9562e915ee58f1b4ba275b1e606cd0a2589ef568c6af4364b")
      "omit when empty, plain",
      ("aac71ec44d6d24e35df113027f64774b0d9f81759611598e53d7f28f7ce0ed74",
       "b6f58a81c3d0928669a3330749ff62e54ca946e7b3cce2fbe2fe1ebf72f24d5d",
       "cf17b26d8f654890eeecf1220a5c2a2c58b44df55ae4db29467ea4910c26dcaa")
      "refusal corners",
      ("938af31e05aafd338a322da4795b6190028bb3c6ad0943e4eff36dabc672eedb",
       "bb9c9e7be68651ca894b6787b461425840ebd6150d7a05b5a57f3437fcc5b99e",
       "f4e8934a4bc58e53d75de0ec35422ec03679f7a58b4c62c8bda9d4a6540d0e17")
      "spec decoders",
      ("e1613bb6607316cc0472f37bfcffac611455122994d91eff7271b12127ce4647",
       "d4e4c8e4a0cac95ccfb2191bd602682cd51dd7c47a58a41e4b4b87c368443bcb",
       "c8ba39a7ccf20a3e42e3ea697a035fd31bd5312ed65bed4039b777a2b8c51607")
      "wider defaults",
      ("a42f1ca26aa08dd6ddd192da5cfd18c4f40bc231349ebf6a74dfdc69ab16c34b",
       "7fd900e0b5c1c23805561ef592ecac07bacd9e8c348d5cff7e6c2fbeb2eb68d6",
       "26b024050796e1333a08688c26aaa619eb72e1e3a6113d3d173767affdfe6c1b")
      "classify base",
      ("48d6128ea230394985bb9d63fa72d20cee246cb2a984bac3a01de122b8da838b",
       "289c159d0b62c629b1962065a4e33b20283f183983362be744b10a74043930dd",
       "a1f1641023000cc1ed773cf2c847bc1b18f9903361400f532b2f8482261a010b")
      "parameterised records",
      ("33698f013b35b52af142d0be6569f68b2a6a03c82a7655e1c1cf9f5f177d2c3f",
       "962a666d3979ba8c9859ab149313a9feaf7f994829847f222cb710ff8fa84589",
       "ef9cca7bb07ae7c2269283aa39686cbce59c29018cac75150214c518bf023e4d") ]

/// Whether a vocabulary declares an alias, an enum case's alias or a presence condition.
let private declares (idl: Idl) : bool =
    let fields =
        (idl.Kinds |> List.collect _.Fields)
        @ (idl.Ops |> List.collect _.Fields)
        @ (idl.Records |> List.collect _.Fields)
        @ (idl.Unions |> List.collect (fun u -> u.Cases |> List.collect _.Fields))
        @ idl.NodeFields

    (fields
     |> List.exists (fun f -> not (List.isEmpty f.Aliases) || f.Presence.IsSome))
    || (idl.Enums
        |> List.exists (fun e -> e.Aliases |> List.exists (fun (_, a) -> not (List.isEmpty a))))

let private emitted (r: Result<string, CodegenError>) : string =
    match r with
    | Ok s -> sha256 s
    | Error e -> failtestf "refused: %s" (CodegenError.describe e)

[<Tests>]
let tests =
    testList
        "Phase 723 — a vocabulary declaring no canonical-form declaration emits the TypeScript it emitted"
        [ testCase "every fixture vocabulary declaring none is pinned, and every pinned one declares none"
          <| fun _ ->
              let library =
                  ("parameterised records", ParameterisedRecordsIdl.idl)
                  :: IdlRecordParamsTests.fixtures

              let bare = library |> List.filter (snd >> declares >> not) |> List.map fst

              Expect.equal (List.sort bare) (before |> List.map fst |> List.sort) "one pin per bare fixture"

              Expect.isTrue
                  (library |> List.exists (snd >> declares))
                  "the library holds a declaring fixture, which is not pinned"

          testCase "each emits the bytes it emitted before the phase"
          <| fun _ ->
              let library =
                  ("parameterised records", ParameterisedRecordsIdl.idl)
                  :: IdlRecordParamsTests.fixtures
                  |> Map.ofList

              for name, (moduleSha, specSha, constantsSha) in before do
                  let idl = library[name]
                  let tags = idl.Kinds |> List.map _.Tag

                  Expect.equal (emitted (Gen.typescriptModule idl tags)) moduleSha (name + ": the module")

                  Expect.equal
                      (emitted (Gen.typescriptModuleDerived [ Derivation.SpecDecoders ] idl tags))
                      specSha
                      (name + ": the module with the collecting decoders")

                  Expect.equal
                      (emitted (Gen.typescriptModuleDerived [ Derivation.VocabularyConstants ] idl tags))
                      constantsSha
                      (name + ": the module with the vocabulary constants") ]
