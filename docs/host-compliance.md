# Host compliance

A host that implements Core in another runtime cannot inherit the proofs in [`proofs/`](../proofs/): a
theorem is about the F\* model and this repository's code, not about a port. What a port CAN show is
evidence, row by row of the claims ladder ([`proofs.json`](../proofs.json)). This page specifies how a
host declares that evidence, how Core validates the declaration, and the two labels Core computes from
it ([DECISIONS.md](../DECISIONS.md) D228).

**Declare local.** A host commits `host-compliance.json` at its own repository root. Its shape is
[`host-compliance.schema.json`](host-compliance.schema.json).

**Validate here.** Whether the declaration is TRUE is Core's question, answered from three things this
repository already derives and a host cannot edit:

- the evidence map ([host-evidence-map.md](host-evidence-map.md)): for each ladder row, the vector
  families whose cases the row's model answered, the oracle functions that ask the row's model, and a
  `host-premise` row's probe;
- every self-enumerated family manifest under [`conformance/`](../conformance/), and each host's
  adoption RECORD in it ([conformance-corpus.md](conformance-corpus.md#adoption-record));
- the `host-premise` rows and their probes ([host-premises.md](host-premises.md)).

A host runs the validator from the Core checkout its declaration names:

```
dotnet run --project tools/Fuaran.Core.Tools -- compliance <path to host-compliance.json>
```

It exits `0` and prints the verdict on stdout when the declaration validates, `1` and every finding on
stderr when it does not, and `2` when it cannot run (no file, no argument, or a checkout whose own
evidence does not read).

## The declaration

```json
{
  "kind": "hostCompliance",
  "host": "fuaran-core-rs",
  "coreVersion": "0.41.0",
  "rows": [
    { "row": "tree-differential", "evidence": [
        { "class": "vector-certified", "family": "skeletonApply", "cases": 25 },
        { "class": "oracle-differential", "model": "TreeOps", "function": "tree.apply", "inputs": 100000, "seed": "0x5eed" } ] },
    { "row": "host-int53", "evidence": [ { "class": "host-premise", "probe": "premiseInt53", "cases": 29 } ] },
    { "row": "host-recursion-depth", "evidence": [ { "class": "not-covered", "reason": "no stack probe on the embedded target yet" } ] }
  ]
}
```

- `host` is the key the family manifests' adoption records use for the host.
- `coreVersion` is the Core the declaration is judged by. The validator refuses a declaration naming any
  other version than the checkout running it, so a declaration is always judged by the evidence it was
  written against.
- `rows` names each row once, with at least one piece of evidence. A row the evidence map offers
  anything for and the declaration leaves out reads as undeclared. A row the map offers nothing for is
  Core's gap, already named on the map's worklist, and may be left out.

### The four classes

The set is closed. Each piece of evidence is one of:

| Class | Members | It says | Validated against |
|---|---|---|---|
| `vector-certified` | `family`, `cases` | the host's leg ran a vector family and asserted `cases` of its vectors | the family exists, the map offers it for the row, and the family's adoption record for the host reads `adopted` with exactly `cases` |
| `oracle-differential` | `model`, `function`, `inputs`, `seed` | the host's own fuzzer drove the oracle process ([oracle-protocol.md](oracle-protocol.md)) on `inputs` inputs from `seed`, asking `function` of `model`, and found no disagreement | the function is in the oracle's table, the map offers it for the row, `model` is the row's model, at least one input, and a seed |
| `host-premise` | `probe`, `cases` | the host discharged a `host-premise` row on its own runtime | the row is a `host-premise` row, `probe` is its probe, and the probe's adoption record for the host reads `adopted` at every vector |
| `not-covered` | `reason` | the host offers nothing for the row | a reason that is not blank, and nothing else on the row |

A row may carry several pieces of evidence, which is how it reaches parity: a certification and a
differential, or several offered families. It may not carry one claim twice. The input count and seed
of a differential are the host's own record; Core cannot re-run a host's fuzzer, and checks only that
the claim names an offered function of the row's model and is not vacuous. A vector certification and a
discharge, by contrast, are checked against the count the adoption record carries, which the host
recorded in Core in the change that landed its leg.

A further class (for example, evidence a port's own verifier produces) joins only by a recorded
decision that amends the schema's enumeration, the validator and this page together. Until then the
validator refuses it as `class-unknown`, as it refuses any other name.

## Refusals

Every refusal is a finding with a code, the place in the declaration (`$`, `rows[<row>]`, or
`rows[<row>].evidence[<n>]`) and a sentence naming the remedy. The suite holds a go-red for each code.

| Code | Refused |
|---|---|
| `shape` | the file is not a declaration of this schema: not JSON, or a member missing, mistyped, repeated or unknown |
| `kind` | the root `kind` is not `hostCompliance` |
| `host-name` | the host key is not lower-case letters and digits in `-`-separated words |
| `core-version` | the declaration names a Core version other than the checkout judging it |
| `row-unknown` | a row the claims ladder does not carry |
| `row-repeated` | one row declared twice |
| `evidence-empty` | a row with no evidence |
| `class-unknown` | an evidence class outside the closed set |
| `evidence-repeated` | one claim made twice on one row (same class, same family, function or probe) |
| `not-covered-alone` | `not-covered` beside other evidence on the same row |
| `reason-empty` | `not-covered` with a blank reason |
| `family-unknown` | a family no vector file under `conformance/` is |
| `family-not-offered` | a family the evidence map does not offer for the row |
| `premise-row` | `vector-certified` on a `host-premise` row, whose evidence is the discharge |
| `adoption-missing` | no adoption record carries the family for this host: no manifest indexes the family, or its record has no entry for the host |
| `adoption-unmeasured` | the host's adoption record reads `proposed`, `declined` or `vacuous` |
| `cases-mismatch` | a case count the host's adoption record does not carry |
| `function-unknown` | an oracle function the oracle's table does not hold |
| `function-not-offered` | an oracle function the evidence map does not offer for the row |
| `model-mismatch` | an oracle function claimed against a model other than the row's |
| `inputs-vacuous` | an oracle differential over no input |
| `seed-empty` | an oracle differential with a blank seed |
| `not-a-premise` | `host-premise` on a row that is not a `host-premise` row |
| `probe-mismatch` | `host-premise` naming a probe other than the row's |
| `premise-partial` | a premise discharged on fewer than every vector of its probe |

## The verdict

A declaration that validates yields one canonical JSON object (keys in ordinal order), which is what a
projection reads:

| Member | Meaning |
|---|---|
| `kind` | `hostComplianceVerdict` |
| `host`, `coreVersion` | the declaration's |
| `offered` | the rows the evidence map offers anything for |
| `declared` | the rows the declaration names |
| `byClass` | the pieces of evidence declared, by class |
| `undeclared` | the offered rows the declaration leaves out, in ladder order |
| `compliant` | `undeclared` is empty: every row Core offers evidence for is declared, so every gap is named |
| `gaps` | for each offered row short of the evidence offered for it, the row and what is `missing` |
| `parity` | `compliant`, and `gaps` is empty |

**`compliant` is the honest account; `parity` is the label.** A host is compliant when it has declared
every row Core offers evidence for, whether with evidence or with a named gap. It is at parity when,
for every such row, it carries ALL of the evidence offered: every offered family certified at every one
of its vectors, every offered oracle function differentially tested, and every host premise discharged.
Neither label is written by a host; both are computed, and a projection takes them from the verdict
rather than computing them again against a different map. Parity is a label a projection can award, not
a gate a port waits for.

**Where the cross-host matrix renders.** Not here. This repository cannot read another repository's
declaration, and its gate never crosses a repository boundary. The matrix of host by row is a projection
over every host's declaration, made outside this repository by running this verb from each declaration's
own Core checkout and reading the verdict (D228).

## What the labels say today

No host carries a declaration yet. The Rust Core spine, `fuaran-core-rs`, is the first instance: its
adoption records already carry its measured legs (`SpineAdoption`, D225), so a declaration of its
certified rows validates against this tree, and its premises read `proposed` until it runs the probes.

No host can be at parity today. The law families under `conformance/laws/` (among them `decimal` and
`propagationLaws`, which the evidence map offers for the `DecimalText` and `Propagation` rows) carry no
per-host adoption record in this repository, so a `vector-certified` claim on them is refused as
`adoption-missing`, and the verdict names each such family as a gap no host can close. Recording the law
families' adoption per host is the change that lifts it.
