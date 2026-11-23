Floating islands - Burst stage 2 (2026-11-23)

Base: Unlimited_Island_Burst_Stage1.zip (user confirmed working).

Changes:
- New IslandGridHeightJob computes regular grid top and bottom heights in parallel using Unity Burst.
- Existing Mathf.PerlinNoise samples remain on the main thread with the same precision-safe seed offset.
- Persistent NativeArray<float> and NativeArray<float2> scratch buffers reused between tiles, disposed in OnDestroy.
- The existing interior classification job, boundary clipping, LOD, colliders, triangle topology and wireframe remain unchanged.

Note: math.smoothstep polynomial matches the smoothstep formula, but floating-point rounding may differ very slightly from Mathf.SmoothStep. The perimeter boundary continues using original Shape().
The job is completed synchronously before mesh construction; performance gain is not guaranteed. The major remaining cost (Perlin sampling, clipping, Mesh construction) is still on the main thread.

No Unity Editor compilation or runtime benchmark was available. Back up the working Scripts folder before installing.
