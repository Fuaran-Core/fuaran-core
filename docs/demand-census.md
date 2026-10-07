# What the declarative surfaces express — the demand census

Core has three declarative surfaces a consumer states intent through: **`Query`** (a typed
data-acquisition declaration, `Fuaran.Core.Query`), the **AI surface's pattern bank**
(`Fuaran.Core.AiSurface`) and the **column layer** (`Fuaran.Core.Column`). A declarative algebra
that only parses what it has seen is the fuaran-core#90 class: an intent nobody listed looks, when
the next consumer reaches for it, like everybody's error but the language's. This census lists the
intents consumers reach for and gives each one, on each surface, a **verdict** — so a consumer reads
a decision rather than discovering a gap.

It was first run by Phase 398 (DECISIONS.md D126), against the `0.36.0` draft. The verdicts that
reach consumers are restated in [`ADOPTION.md`](ADOPTION.md#what-the-declarative-surfaces-express).

## The verdicts

| verdict | meaning |
|---|---|
| **expressible** | a declaration states it, the codec carries it, and the file:line cited is where |
| **host-side by design** | Core deliberately does not state it; the record cited says why and where it lives instead |
| **gap** | a consumer needs it, Core does not state it, and nothing has decided that it should not |

A gap is not a failure of the census; a gap with no verdict is. After Phase 398 no intent below is a
gap: every one is either expressible or host-side by a recorded decision.

### `Query` (`src/Fuaran.Core.Query/Query.fs`)

| intent | verdict | decided at |
|---|---|---|
| substring / contains | **expressible** — `ColumnPredicate.Contains`, ordinal and case-sensitive, on a `string` column | `Query.fs:95` |
| membership (a value in a set) | **host-side by design** — `Where` is a conjunction; a set is a disjunction of equalities. A host takes the set as a parameter and its resolver reads it | D126 |
| string building | **host-side by design** — a query declares what to fetch, not what to compute; scalar functions are the compute repository's | D126, D66 |
| null tests | **expressible** — `ColumnPredicate.IsNull` / `IsNotNull` | `Query.fs:97` |
| date deltas | **host-side by design** as arithmetic (D66); a date RANGE against a literal is expressible — `AtLeast` / `LessThan` on a `date` column | `Query.fs:82` |
| pagination | **expressible** — `Query.PageSize`, the page token on input (`Query.invokePage`), `QueryResult.NextPageToken` | `Query.fs:145`, `:1006`, `:169` |
| sorting | **expressible** — `Query.OrderBy`, a column list with a direction each | `Query.fs:152` |
| filtering (equality, range) | **expressible** — `Query.Where`, a closed conjunction: `EqualTo`, `GreaterThan`, `AtLeast`, `LessThan`, `AtMost` | `Query.fs:149`, `:82` |
| aggregation over the result | **host-side by design** on `Query` (a query returns rows); expressible over a `Table` in the column layer, below | D126 |
| alternation, typed slots | not a `Query` intent (they are the pattern bank's) | — |

A resolver that cannot honour a declared predicate or order refuses it by name
(`ResolveFault.PredicateUnsupported` / `OrderUnsupported`, `Query.fs:263`), surfacing as
`QueryError.PredicateNotHonoured` / `OrderNotHonoured`; it never returns rows the filter was not
applied to. A declaration whose filter or order its result schema does not admit is refused at
registration and by the declaration reader with one error (`QueryRegistry.admissionFaultAt`,
`Query.fs:1214`).

### The pattern bank (`src/Fuaran.Core.AiSurface/AiSurface.fs`)

| intent | verdict | decided at |
|---|---|---|
| substring / contains | **expressible** — an anchor's literal segments match the intent text in order, ordinally and case-insensitively | `AiSurface.fs:231`, `:277` |
| membership | **host-side by design** — inside the domain's `Emit` | `AiSurface.fs:103` |
| string building | **host-side by design** — `Emit` builds the ops | `AiSurface.fs:103` |
| null tests | **host-side by design** — the intent's `Args` are strings the host parsed; `Emit` reads them | `AiSurface.fs:84` |
| date deltas | **host-side by design** — `Emit` | `AiSurface.fs:103` |
| pagination, sorting, filtering, aggregation | not pattern-bank intents (a match emits ops, not rows) | — |
| alternation | **expressible** between whole anchors — a pattern carries several, and any ONE matching selects it; **host-side by design** within one anchor, and the bank resolves first match in bank order | `AiSurface.fs:100`, `:295`, D126 |
| typed slots / captures | **host-side by design** — a `{…}` wildcard never captures; a value reaches `Emit` only through `Args`, which the host fills | `AiSurface.fs:84`, D126 |

### The column layer (`src/Fuaran.Core.Column/Column.fs`)

| intent | verdict | decided at |
|---|---|---|
| null tests | **expressible** — `Cell.isNull` | `Column.fs:451` |
| aggregation over a `Table` | **expressible** — `Column.aggregate` over the closed `AggFn` set | `Column.fs:1053`, `:561` |
| sorting | **host-side by design** as an operation (the compute repository's `Transform`); Core owns THE order a host sorts by, `Cell.compare` | `Column.fs:517`, D66 |
| substring / contains, string building, date deltas, membership, filtering | **host-side by design** — scalar functions and transforms are the compute repository's | D66, D71 |
| pagination, alternation, typed slots | not column-layer intents | — |

## How this census is run

It is the cross-domain demand loop's template, instantiated for a surface rather than a model
corpus. Its five stages:

1. **Census.** The grid above: every intent against every surface. There is no emission corpus in
   this repository to cluster, so the census is the grid itself, and it is complete only when every
   cell has a verdict.
2. **Intake before classification.** The intents come from three records, cited rather than
   re-surveyed: the 2026-09-30 survey of every Core consumer in the workspace (five consumer surveys
   and one algebra-closure analysis), which filed the input-side paging Phase 316 shipped; the
   fuaran-core#90 class itself (`contains`, demanded by an evaluation task and three model emissions
   before anything named it); and the 2026-10-07 gaps review, whose seventh finding asked for this
   census.
3. **Classify by falsifier.** A directed surface check, which the template admits where an intent is
   settled structurally: an intent is **expressible** exactly when a declaration can state it and
   the codec carries it, and the cited file:line is the evidence. No model run was spent, and none
   is needed to settle a structural question.
4. **Dispatch by layer.** An intent that should be Core surface becomes surface (Phase 398 shipped
   `Where` and `OrderBy`); one that should not is a recorded decision (D126) and is listed here as
   host-side by design.
5. **Verify the compounding.** The verdicts that ship are pinned by tests that go red if the surface
   moves back: the `Query Where / OrderBy (Phase 398)` list in `QueryTests.fs` and the
   `queryLaws` arm in `QuerySeamLaws.fs`. The next consumer's need should arrive in canonical form,
   riding `Where` and `OrderBy`; if it arrives as an opaque parameter instead, the census is re-run.

### The row shape

One row per (surface, intent): the **intent**, the **verdict**, **decided at** (a file:line for an
expressible intent, a DECISIONS record for a host-side one), and the **stamp** — the Core version
the verdict was read against, which is this document's: the `0.36.0` draft.

### The two provenance hazards

- **Stale verdicts.** A verdict is a claim about one version of a surface. A later phase that moves a
  surface makes every verdict about it a claim about a surface that no longer exists, silently. The
  stamp above says which version was read; a phase that moves `Query`, the pattern bank or the column
  layer re-reads its rows, and the tests named in stage 5 refuse the silent case for the verdicts
  that ship.
- **First-gap undercount.** A census that stops at the first missing intent of a surface counts one
  gap per surface, not all of them. Every cell of the grid is evaluated, and a surface with several
  missing intents shows several rows.

### The seams a domain supplies

A domain running this census over its own declarative surfaces supplies what Core supplied here:

| seam | Core's value |
|---|---|
| the surfaces under census | `Query`, the pattern bank, the column layer |
| the intent source (intake) | the three records in stage 2 |
| the verdict oracle | the declaration types and their codecs, read through the committed `api/` and `api/wire/` baselines — the machine-readable vocabulary, never a hand-listed set |
| the by-design exclusions | the DECISIONS records cited in the tables |

The template also names a probe-corpus marker; a surface census has no diagnostic corpus, so Core
supplies none.
