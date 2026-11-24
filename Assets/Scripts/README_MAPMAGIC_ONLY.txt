UNLIMITED — MAPMAGIC-ONLY SURFACE / DENSITY FOUNDATION

SOURCE: Unlimited_MapMagic_Height_Source_Fix.zip

IMPORTANT: This is a REPLACEMENT of the Assets/Scripts folder, not a patch.
1. Back up / commit your Unity project.
2. Remove the old Assets/Scripts folder (or at minimum delete old TerrainGenerator*.cs, MapMagicHeightProvider.cs, and Terrain/ folder with their .meta files).
3. Copy Scripts/ from this ZIP into Assets/Scripts.
4. Remove Missing Script components from scene objects that formerly held TerrainGenerator or MapMagicHeightProvider.
5. Keep MapMagic generators and their Unity Terrain rendering enabled. MapMagic package, graph, scene and terrain data are NOT part of this ZIP.
6. Floating islands are retained and have independent Burst jobs and spatial LOD.
7. Density/DensityLodCore.cs supplies 3D LOD coordinate helpers (4/16/64 m, 16 cells/axis) and an unscheduled Burst-ready density sampling job.

REMOVED: custom ground heightfield generator, associated jobs, caches, queues, visibility and test toggles.
NOT YET IMPLEMENTED: density streaming, digging, surface extraction, collider/LOD seams, Unity Terrain holes.
NOT TESTED IN UNITY: verify compilation and scene references.
