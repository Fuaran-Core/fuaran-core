# Contributing to Fuaran.Core

Thanks for your interest. Fuaran.Core is the cross-domain substrate for the Fuaran family — a
library of **generic functions over domain-witness records**, FSharp.Core-only and Fable-clean on
both the encode and decode paths. It has no base node type and no domain evaluator; that constraint
is the whole design, not an accident.

## Building and testing

Requirements: the .NET SDK pinned in [`global.json`](global.json).

```powershell
./run.ps1        # restore tools, format, build, test
./verify.ps1     # format-check + build + Fable-compile gate + test + sample (the green gate)
```

A change is ready to propose when `./verify.ps1` is green.

## Coding standards

- **F# formatting is Fantomas.** Run `./run.ps1` (or `dotnet fantomas src tests`) before every
  commit; `./verify.ps1` fails on unformatted code.
- **Totality — no exceptions in the public surface.** Failures are typed values (`Result`, a
  `Rejection`, or a named `*Error` envelope), never a thrown exception. A recoverable envelope must
  *name the failure and enumerate the valid alternatives*.
- **FSharp.Core only, Fable-clean.** No `System.Text.Json`, no host or native dependency. Every
  public surface must compile under both .NET and Fable — the `tests/fable-smoke` gate enforces it.

## The design invariants — please read before a non-trivial change

[`STABILITY.md`](STABILITY.md) is the contract. In particular:

- **No base node type.** Core never sees a concrete `NodeKind`; it operates through witness records.
  Introducing a concrete node/kind type into a Core package is a breaking architectural regression,
  not a feature.
- **No new witness field.** The public witness records are frozen in shape. A generic function that
  needs a capability the witnesses don't expose takes it as a **per-call function parameter**, never
  a new witness field.
- **No domain evaluator in Core.** Render / recompute / regenerate / reflow stay on the domain side.

A change that adds a public surface should add or extend a `Conformance` law that certifies it, and
update `STABILITY.md` when it touches a stability-critical surface.

## Developer Certificate of Origin (DCO)

Contributions are accepted under the [Developer Certificate of Origin](https://developercertificate.org/).
Sign off every commit with `git commit -s`, certifying you wrote the change (or otherwise have the
right to submit it) under the project's Apache-2.0 license:

```
Signed-off-by: Your Name <you@example.com>
```

## Pull requests

Keep PRs focused and describe what changed and why. Make sure `./verify.ps1` passes. By contributing
you agree that your contribution is licensed under the Apache License, Version 2.0.
