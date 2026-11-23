Floating islands - Burst stage 1 (2026-11-23)

Source: confirmed working Unlimited_Island_Memory_Optimization_v1.zip.

Changes:
- FloatingIslandSpatialLod.cs: Burst IJobParallelFor classifies fully interior cells
  of each Fine/Fast/SuperFast tile using a reusable persistent NativeArray.
- The classification result is consumed by the existing mesh builder.
- NativeArray is disposed in OnDestroy.
- Unity.Mathematics, Unity.Collections, Unity.Jobs, Unity.Burst packages required
  (already used by TerrainGenerator in this project).

Preserved: PerlinNoise and precision fix, seed, all vertex heights, clipping,
triangles, wireframe colors, collider logic, LOD distances and sizes.

IMPORTANT: This is an incremental Burst stage, NOT a full Burst mesh builder.
The existing managed Mesh construction, clipping, PerlinNoise and Unity
GameObject creation remain on the main thread. A job scheduled and completed
per tile has overhead; FPS improvements are NOT guaranteed and may be negative.
The purpose is a conservative integration test without changing island shapes.
No Unity Editor compilation/runtime validation performed here.

Install: back up Assets/Scripts; replace it with the Scripts folder in ZIP.
