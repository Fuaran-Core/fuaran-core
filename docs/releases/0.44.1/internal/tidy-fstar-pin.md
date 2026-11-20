- **The F\* pin is moved deliberately, and a stale one is visible** (`proofs/README.md`, "Bumping the pin";
  `proofs/check.ps1`) — class **internal**: the proof leg's caller and its documentation only; no `api/` or
  `api/wire/` row, no package, wire or conformance byte moves, and no kit file. The README states the bump for both
  pins as they are shaped today (per-OS entries, the mirror, the custard leg's own pin): mirror the assets, edit the
  pin, clear the installed prover, `check.ps1 -Runs 3`, re-extract on oracle drift, rebuild the kit asset's sha, and
  commit the pin with its extraction diff in one commit naming both releases. `check.ps1` prints the newest upstream
  release beside the pinned ones, from one request, never changing the verdict and silent when it cannot answer.
