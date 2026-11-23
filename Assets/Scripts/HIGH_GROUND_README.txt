HIGH GROUND / 3 LOD MODES

HyperFast and MaxFast: disabled (no queue rebuild, no Burst scheduling, no visible chunks).
Active: Fine / Fast / SuperFast with existing Burst jobs.
High Ground logic adapted from Terrain_HighGround_Updated(1).zip.

Height = player.position.y - seaLevelY
Height <= 40 m: radius = loadRadius (default 350 m).
40-1000 m: linear interpolation to 1000 m.
Height >= 1000 m: radius = 1000 m.
Only SuperFast receives the expanded radius.
Fast/Fine and colliders remain at their original distances.
SuperFast outside radius + highGroundUnloadMargin is unloaded gradually.

Inspector: High Ground Streaming - SuperFast Only.
Note: 1 km radius is not an instantaneous guarantee; generation respects Jobs budgets.
Unity runtime and compile have NOT been tested in this environment.
