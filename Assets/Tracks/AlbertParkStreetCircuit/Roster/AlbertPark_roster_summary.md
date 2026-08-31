# Albert Park Source GLB Roster Summary

Source: `C:\Monogames\GranTurismo\Assets\Tracks\AlbertParkStreetCircuit\Original GLB\Albert Park Street Circuit.glb`

Mesh objects inventoried: 267

## Category Counts

- barrier: 59
- building: 43
- collider: 13
- light: 11
- pit: 26
- road_surface: 6
- sign: 41
- terrain: 14
- trackside_prop: 18
- tyre_wall: 3
- vegetation: 31
- verge_runoff: 2

## Recommended Action Counts

- keep: 75
- review: 160
- skip: 32

## Duplicate Status Counts

- probable-duplicate: 47
- unique: 220

## Notes

- Vegetation is recorded but marked skip for production extraction.
- Road, curb, verge, runoff, terrain, and collision entries are marked for review because they need a separate spline/profile workflow.
- Proposed names are first-pass audit names and should be adjusted during visual review before extraction approval.
- AP-0132 `Barrier_TyreStack_A` is the canonical approved tyre-stack asset. AP-0133 was rejected as a functionally indistinguishable duplicate.
- Default facing-prop orientation convention approved from AP-0039: `+Z = up`, `-Y = forward/readable facing direction`, `X = width`. Use where an asset has an obvious facing direction; do not force it onto directionless assets.
- AP-0039 `Sign_Braking_100_A` is approved. Geometry/origin/orientation and transform convention are approved. Current generated 100 texture is acceptable for now, but should be treated as temporary/polish-later.
- AP-0097 `Cone_Traffic_A` is approved as the canonical traffic cone. AP-0098 through AP-0101 are rejected as scaled/placed duplicates of the same normalized cone topology.

