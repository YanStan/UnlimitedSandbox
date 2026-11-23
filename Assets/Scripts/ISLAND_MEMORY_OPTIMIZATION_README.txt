Floating island spatial LOD: allocation/CPU cleanup (2026-11-23)

Changed: FloatingIslandSpatialLod.cs only.
- Reuse polygon clipping scratch Lists instead of allocating two Lists per boundary triangle.
- Remove temporary Vector2[3] allocation per clipped triangle.
- Consume sorted build queue with an index instead of List.RemoveAt(0) (avoids repeated shifting).
- Cache wireframe child GameObject rather than GetChild(0) on every Update.
- Avoid creating a temporary copy of tile keys in OnDestroy.

Not changed: noise seed precision fix, island shape, vertex heights, triangulation,
LOD sizes, colors, collider rules, distance rules, mesh output, or inspector fields.

This is a targeted first-stage optimization, NOT Burst/Jobs conversion.
Not runtime-tested in Unity; keep a backup of the working scripts.
