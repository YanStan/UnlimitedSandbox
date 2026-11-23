Island Burst Stage 3: asynchronous scheduling
- A single tile's mask and height-composition jobs are scheduled without immediate Complete().
- Update polls IsCompleted on subsequent frames; only then uploads mesh on main thread.
- Each in-flight tile owns its NativeArrays until completion, including on destruction.
- Noise sampling, topology, clipping, grid, sizes, and LOD rules are unchanged.
- Conservative one-in-flight-job approach prioritizes correctness over throughput.
- IMPORTANT: this version has not been compiled in Unity. Keep a backup of Stage 2.
