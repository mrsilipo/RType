# High Speed Ring Authored Manifest

- Export date: `2026-09-07T10:10:18.894879+10:00`
- Authoring blend: `C:\\Monogames\\GranTurismo\\Assets\\Tracks\\HighSpeedRing\\Editable\\HighSpeedRing_Authoring.blend`
- Runtime GLB: `C:\\Monogames\\GranTurismo\\Assets\\Tracks\\HighSpeedRing\\Runtime\\HighSpeedRing_Authored.glb`
- GLB valid: `True`
- GLB size bytes: `631872`
- Validation passed: `True`

## Authored Scene

- Object count: `28`
- Visible object count: `26`
- Hidden objects omitted: `2`
- Mesh object count: `25`
- Renderable object count: `26`
- Exported object count: `25`
- Mesh datablock count: `19`
- Vertex count: `18732`
- Triangle count: `18700`
- Material count: `13`
- Texture count: `7`
- Bounds min: `349.931, -827.374, -7.578`
- Bounds max: `1479.888, -161.412, 20.931`
- Authored IDs added this export: `12`

## Textures
- `Textures/Barrier_BlueWhite.png`
- `Textures/GrandStand_Crowd.png`
- `Textures/GrandStand_Empty.png`
- `Textures/Grass_A.png`
- `Textures/Road_Asphalt.png`
- `Textures/StartFinish_Line.png`
- `Textures/White_Solid.png`

## Collections
- Collection
- Furniture
- HighSpeedRing
- PolePositions
- Terrain

## Validation Notes
- Missing textures: `0`
- Invalid transforms: `0`
- Duplicate authored IDs: `0`
- Unsupported/non-core material nodes observed: `0`
- Modifier objects baked: `8`
- Curve/font/surface objects converted: `1`
- Manual curve arrays baked: `0`
- Replaced generated visual objects skipped: `0`
- Hidden Blender objects omitted: `2`
- Base mesh fallbacks: `0`
- Empty/unexportable geometry omitted: `1`
- Warning: Modifier baked: Barrier_BlueWhite
- Warning: Modifier baked: Barrier_Fences_01
- Warning: Modifier baked: Barrier_Fences_02
- Warning: Modifier baked: GrandStand_Crowd
- Warning: Modifier baked: StartFinish_TrackChecker
- Warning: Modifier baked: Terrain_Inner_Driveable
- Warning: Modifier baked: Terrain_Outer_Driveable
- Warning: Modifier baked: Track_Asphalt_Driveable
- Warning: Converted to mesh: Track_Curve
- Warning: Hidden object omitted: Grass_Field_0001
- Warning: Hidden object omitted: Wall_Block
- Warning: Omitted empty geometry: Track_Curve

## Workflow

- Authored Blender scene is the visual source of truth.
- Procedural bootstrap object parity is not required.
- Deleted, moved, joined, split, replaced and newly authored objects are valid.
- Only objects visible in the saved Blender view layer are exported; hidden objects remain in the authoring blend but are omitted from the runtime GLB.
- GLB is exported from a temporary evaluated mesh scene with Blender glTF exporter extras enabled.
- Modifiers and curve/font/surface objects are baked only into the runtime GLB; the authoring blend remains editable.
- If a mesh object's evaluated modifier stack produces no faces, the exporter falls back to the base mesh and reports it.
- Distance-based curve array modifiers that evaluate empty in headless Blender are manually baked along their curve path.
- When Track_Asphalt* exists, the previous generated Road object is treated as replaced and is not exported to the runtime GLB.
