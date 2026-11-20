- **A capability invocation keys its arguments by qualified hole name** (Phase 712, `Fuaran.Core.Function`,
  DECISIONS.md D281) — class **breaking**: `removal` on `Fuaran.Core.Function` and a breaking rewrite of its wire
  baseline. An argument binds the hole whose qualified name it carries. The qualified name is the names of the
  slots the hole was composed through, outermost first, then its own name, joined by `.`; it was keyed by the
  hole's address before.
  - **Holes and the admission gate.** `HoleDecl` gains `Slots: string list`, and `SigEntry.Name` is the qualified
    name. `Signature.validate` refuses a name outside the segment grammar (`IllFormedHoleName`; a segment is one
    or more ASCII letters, digits, `_` or `-`) and two holes with one qualified name (`DuplicateHoleName`); both
    are appended to `DeclarationFault`. `EmptySpace` and `NonFiniteBound` carry the name.
  - **The seam and its documents.** Validation, typing, invocation, `invocationKey`, `invocationPreimage`,
    `grant`, both registries and a pipeline's invoke node bind by name, and `Function.toJsonSchema` keys its
    properties and `required` by name. An invocation's argument is `{"name","value"}` and a pipeline argument
    is `{"name","source"}`; every seam refusal carries `name` (`names`) where it carried `addr` (`addrs`).
    `Capability.repeatedAddrs` is `repeatedNames`, `FunctionRegistry.narrow` takes names,
    `FunctionRegistry.addressed` re-keys an argument list to addresses for `Function.apply`, and `ContentPack`'s
    `BoundAddrs` and `UnknownBoundAddr` are `BoundNames` and `UnknownBoundName`. `apply`, `curry`, `compose` and
    `bindHandlers` still bind by address.
  - **The vectors** of `capabilityLaws`, `capabilityDocumentLaws`, `capabilityGateLaws` and `seamRefusals`
    (now 145 vectors) are re-emitted keyed by name, and the corpus pin moves with them.
  - **What breaks, and what a consumer changes:**
    - A full-literal `HoleDecl` fails with FS0764 until it sets `Slots`; a composing domain records slot names in
      its `Bind` and reports them in `Holes`.
    - An exhaustive `match` over `DeclarationFault` needs the two new arms.
    - A caller sends names, and an old `addr`-keyed invocation is refused `MissingField` at `args[i].name`.
    - A host codec reads and writes `name` members.
    - A declaration whose names repeat or break the grammar is refused where it was admitted.
    - A capture keyed before this change is keyed differently after it.
