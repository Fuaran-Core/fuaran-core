- **A parse holds its nesting cap on any thread stack** (Phase 663, `Json.fs`, DECISIONS.md D272) — class
  **internal**: no `api/` row, wire byte, refusal kind, sentence, position or path moves. The `JVal` parse keeps its
  open containers in a list indexed by nesting level, in the heap, where it recursed once per container, so the cap
  is reached and refused by name whatever the stack. The recursion spent 576 to 1,280 bytes of stack a level, so on
  a 256 KB thread a document at the default cap of 512 ended the process in Release and Debug builds alike, where
  the cap promises `MaxDepthExceeded`; it now decodes there, the one past it is refused, and a lifted cap decodes
  100,000 levels. The container rules are steps the reader's loops and the parse both run, so there is still one
  copy of each. A parse of 2,000 five-member objects allocates 1.44 MB where it allocated 3.03 MB, and every other
  case about 210 bytes more; time reads within the noise of the machine. Measured in
  `benchmarks/results/2026-11-19-ultra9-386h-phase-663.md`.
