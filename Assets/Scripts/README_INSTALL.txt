UNLIMITED — COMPLETE SCRIPTS FOLDER (MAPMAGIC SURFACE ONLY)

This package replaces the old Assets/Scripts directory.

1. Back up/commit your Unity project.
2. Delete Assets/Scripts (only if you have no custom scripts added since the supplied archive).
3. Extract this ZIP into Assets/ so that Assets/Scripts/*.cs exists.
4. Remove the old World GameObject from your scene if it was only used for TerrainGenerator.
   If it holds other components, remove only the missing TerrainGenerator script.
5. Remove any other missing-script components in your scene.
6. Leave MapMagic's generator, graph, and Unity Terrain enabled.
7. Open Unity Console and check for compiler errors.

This ZIP does not contain MapMagic package or your Unity scene. Deleting scripts
will not delete old World objects or components from a scene automatically.

Contents: PlayerController, MobileTouchControls, BurstTest, floating island
system (with its independent Burst/LOD), and DensityLodCore (future scaffold).
Old TerrainGenerator and MapMagicHeightProvider have been removed.
The density scaffold does not yet implement 3D streaming or digging.

Compilation in Unity has not been tested.
