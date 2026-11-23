TerrainGenerator update: staged LOD streaming

Replace the existing files with all five C# files in this folder. Keep them
in the same Unity folder/assembly. Remove the old duplicate versions first.

Files:
- TerrainGenerator.cs
- TerrainGenerator.Streaming.cs
- TerrainGenerator.Generation.cs
- TerrainGenerator.Diagnostics.cs (unchanged)
- TerrainLodVisibilityController.cs

Changes in this update:
- Fast jobs are prioritized by complete 64x64 m Super parent groups, nearest first.
- Fine jobs are queued only after the corresponding Fast group has replaced its
  Super parent, preserving a visible Super -> Fast -> Fine progression.
- Fine groups are prioritized by their 16x16 m Fast parent, nearest first.
- Fast and Fine pending jobs have separate limits (defaults 20 and 12).
- Completed Fast and Fine jobs share the build budget fairly, so one type cannot
  permanently starve the other.

Inspector defaults added in TerrainGenerator:
- Max Pending Fast Jobs: 20
- Max Pending Fine Jobs: 12

This update does not change terrain geometry algorithms, chunk dimensions,
Burst job implementations, or collider generation. Unity compilation/runtime
verification has not been performed in this environment.
