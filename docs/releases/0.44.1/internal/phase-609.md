- **A digest copies its input once** (Phase 609, `Fuaran.Core.Text`) — class **internal**: no `api/` or `api/wire/`
  row, no verdict and no vector moves. `Hash.utf8Bytes` sizes one exact array with `utf8ByteCount` and encodes into
  it; the SHA-256 compression reads every whole block straight from its input and pads only the tail in a 64- or
  128-byte scratch; the hex form writes one `char[64]`. `sha256Hex` over a million mixed units allocates 1.36 MB
  where it allocated 9.63 MB, and `sha256HexOfBytes` a constant 728 bytes at every size where it allocated three
  times its input; measured in `benchmarks/results/2026-11-19-ultra9-386h-phase-609.md`.
