- **`Dag.heads` and the drain allocate no whole-DAG lists** (Phase 614, `Fuaran.Core.OpStream.Dag`) — class
  **internal**: no `api/` row, wire byte, order or refusal moves. `Dag.heads` gathers parents into a `HashSet` and
  reads the node map in its own ordinal key order, with no lists, `Set` or sort; the Kahn drain keeps in-degrees and
  children in `Dictionary`s sized to the node count and keeps the ordered `Set` frontier that defines the order.
  Over a 9,866-node store `Dag.heads` allocates 0.20 MB where it allocated 8.33 MB, `Dag.totalOrderBy` 4.2 MB where
  it allocated 33.8 MB, and `Dag.tryReplayTo` 18.8 MB where it allocated 48.4 MB, with gen2 per call unchanged.
  Held against the previous walks, kept verbatim, over hand-built DAGs with cycles.
  ([results](../../benchmarks/results/2026-11-19-ultra9-386h-phase-614.md))
