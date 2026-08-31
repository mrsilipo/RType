# Fence_Chainlink_A Validation

- Donor roster ID: `AP-0257`
- Source object: `WALL_FENCES_FENCE_MELBOURNE_0`
- Source component: `3168`
- Exported GLB: `C:\Monogames\GranTurismo\Assets\Tracks\AlbertParkStreetCircuit\Extracted\Fences\Fence_Chainlink_A.glb`
- Review blend: `C:\Monogames\GranTurismo\Assets\Tracks\AlbertParkStreetCircuit\Review\Fence_Chainlink_A_review.blend`
- Validation status: `PASS`

## Extraction

- Extracted one representative chainlink panel face only.
- Excluded neighbouring panels and unrelated track-wide fence fragments.
- Excluded exact overlapping/copanar duplicate panel copy; material is double-sided instead.
- No post/support geometry was included because the selected representative panel component contains no integrated posts.

## Cleanup

- Merge tolerance metres: `1e-05`
- Vertices before / after: `4 / 4`
- Triangles before / after: `2 / 2`
- Connected components before / after: `1 / 1`
- Exact duplicate vertices after cleanup: `0`
- Duplicate faces after cleanup: `0`
- Zero-area faces after cleanup: `0`
- Degenerate edges after cleanup: `0`
- Loose vertices after cleanup: `0`

## Re-imported Export

- Dimensions metres: `(3.794846, 0.151337, 2.729093)`
- Connected-component count: `1`
- Location: `(0.0, 0.0, 0.0)`
- Rotation degrees: `(0.0, 0.0, 0.0)`
- Scale: `(1.0, 1.0, 1.0)`
- BBox min metres: `(-1.897423, -0.075668, 0.0)`
- BBox center metres: `(0.0, 0.0, 1.364546)`
- Material slots: `1`
- Materials: `['Fence_Chainlink_DarkGrey_Alpha']`

## Material

- Replaced source `FENCE_MELBOURNE` dependency with dedicated packed alpha texture material.
- Material: `Fence_Chainlink_DarkGrey_Alpha`, non-metallic, roughness 0.82, double-sided, transparent diamond-link pattern.

## Notes

- +Z is up, X is panel length, Y is panel depth/thickness.
- Asset is centered on X/Y and grounded at Z=0.
- Panel ends are the original component bounds and are suitable for repetition along X.

## Failures

- None
