# High Speed Ring Scenery Reference Plan

## Visual Target

Use the reference-photo direction as the High Speed Ring baseline, now framed as a creative Thruxton/Salisbury Plain-inspired "High Speed Circuit" theme rather than a generic mountain venue. This is not a literal map recreation. The target is a fast English circuit edge: open chalk downland, broad sky, dry grass, hedgerows, scrub clumps, and soft distant ridges.

- Deep blue upper sky.
- Warm off-white/yellow haze near the horizon.
- Distant land band at the horizon with muted blue-green, chalk beige, olive and dark hedgerow shapes.
- Low-poly far downland/ridge silhouettes rather than alpine mountains, empty flat sky, or stretched billboard strips.
- Pixel-art road and grass retained; avoid over-smoothed filtering that fights the retro look.
- Grass should read as clumped dry-green chalk grassland with yellow straw variation, not a flat saturated green plane.
- Trackside furniture should mix UK airfield-circuit flavour with Japanese racing-game readability: start-line seating, concrete blocks, chevron boards, service roads, and restrained scenic landmarks outside the racing surface.

## Research Basis

- Sean O'Neil/GPU Gems atmospheric scattering: Rayleigh scattering drives the blue sky; Mie/aerosol scattering creates the pale/yellow horizon haze and sun glow.
- Low-poly horizon scenery: far chalk downs, distant earth and hedgerow masses should be simple uneven geometry bands so the horizon reads as landscape, not a horizontally repeated billboard texture.
- Billboard/impostor scenery remains useful for nearer field trees where texture detail is visible and scale is easy to judge.
- Procedural vegetation: repeated clumped silhouettes with deterministic variation are enough for distant tree massing before we invest in individual tree geometry.

## Current Implementation

- `ProceduralSkyRenderer` renders the sky first with the compiled `ProceduralAtmosphere.fx` shader.
- The procedural sky source is `Assets/Shaders/ProceduralAtmosphere.fx`; `Content.mgcb` compiles it to `Assets/Shaders/ProceduralAtmosphere.mgfxo.xnb`, which MonoGame loads by the asset name `ProceduralAtmosphere.mgfxo`.
- The current atmosphere tuning favours deeper zenith blue, a warmer off-white/yellow horizon band, and a softer controlled haze transition so the sky supports the Salisbury Plain reference without washing out the road.
- The shader and CPU fallback now include subtle procedural cirrus/high-air variation, low horizon-air bands, a restrained sun-side warm lobe, and a reduced screen-space haze wash. These are deliberately restrained so the sky gains depth without becoming cloudy or reintroducing the previous white wash over the scene.
- Fog is disabled on the track/car path to prevent the road fading to artificial blue.
- `ProceduralBackdropRenderer` draws camera-relative concentric scenery:
  - the layer radii, sun-side tint direction and palette are now held in a `BackdropSceneryProfile.HighSpeedCircuit` profile, so this track's sky/horizon/vegetation palette is explicit instead of scattered as anonymous constants
  - textured low-poly far Salisbury chalk downland mesh
  - broken blue-grey distant ridge-cap mesh
  - textured low-poly soft Wiltshire ridge mesh
  - broken distant valley and field-seam mesh
  - far chalk scarp highlights and blue-green copse breaks
  - warm and blue-grey transparent horizon veil rings that soften the far terrain into the sky
  - subtle sun-facing warm and opposite-side cool horizon veil arcs, so the distant earth bands have a directional afternoon light bias instead of one flat tint all the way around
  - textured low-poly open chalk grass foothill mesh
  - broken pale chalk field terraces
  - thin chalk lane cuts, muted pasture patchwork and small river-glint bands
  - low-poly pale chalk field band
  - broken distant hedgerow islands
  - low-poly dark hedgerow/scrub mass
  - subtle low-poly chalk track/river-scar band
  - near uneven scrub bank fragments
  - near juniper/scrub tree clumps
- `GeneratedTextures` creates all scenery textures procedurally:
  - `Grass`
  - `Road`
  - `DistantEarth`
  - `Mountain`
  - `TreeClump`
  - `ChevronSign`
  - `StartBoard`
  - `BrakeMarkerSign`
  - vehicle/scenery support masks such as shadow and light-lens textures
- If present, transparent reference tree PNGs from `Assets/Tracks/Textures/Reference Material Trees` are loaded for stronger billboard silhouettes:
  - `000-bigtree2.png`
  - `011-pines.png`
  - `012-ShrubBranch.png`
- Field trees are intentionally separate from the horizon line: they are larger clustered groves around the safe outer field/perimeter, while the far distance is handled by low-poly mountain/valley/river meshes.
- Reference tree PNGs are downsampled and contrast-boosted at runtime so they read as bold pixel-art foliage instead of delicate photo cutouts.
- Each supplied reference tree source now also generates a deterministic alternate stylized variant, using mirrored sampling and slight warm/cool/darker contrast shifts. Field groves choose from six local cutout variants instead of three, preserving the approved tree texture style while reducing repeated silhouettes.
- Broad perimeter fields use coarse rolling terrain patches. Tree bases sample the same background height function so groves can sit on gentle topography instead of every tree being stamped onto one flat plane.
- Off-track field surfaces now include broad, slightly rotated muted pasture, dry chalk crop, harvest-strip and chalk-cut parcels layered over the main grass so large open areas read as countryside patchwork instead of one repeated texture or editor-aligned slabs.
- Larger forest clusters now receive dark olive grass-textured understory patches so the tree groups feel rooted into the field surface instead of floating over uniform grass.
- The backdrop includes textured and broken horizon belts for ridge caps, valley seams, chalk terraces, hedgerow islands, and scrub-bank fragments so the far scene reads as layered countryside rather than a single repeated horizontal wall.
- Horizon terrain-belt UVs wrap continuously around the ring, instead of restarting on every angular segment. This prevents generated mountain/earth textures from reading as vertical repeated slices.
- Horizon terrain-belt points now include subtle deterministic radius variation, so low-poly downland and earth bands do not sit on mathematically perfect concentric circles.
- Field trees render as alpha-tested cutouts with depth writes, not transparent blended cards. This is intended to reduce pop/fade caused by transparent sorting and crossed billboard planes.
- The foliage alpha-test threshold is intentionally low so distant, minified tree pixels do not blink out as whole cards cross subpixel coverage.
- Foliage is linear-clamped during the alpha-test pass, while track road/grass remain point-sampled. This keeps the approved pixel-art ground sharp without making distant tree silhouettes blink from single-pixel alpha dropout.
- The main chase/fixed camera far clip now covers the full High Speed Ring scenery footprint so perimeter forests do not hard-clip at the old short render distance.
- The generated grass texture is now 64x64 with denser blades, straw flecks, chalk flecks and clump shadows; it should be treated as the current benchmark for tree/ground pixel density.
- The grass and backdrop palettes are gently biased toward dry chalk downland and hedgerow olive tones while preserving the approved tree/horizon composition.
- The generated asphalt texture is 512x512 with coarse, medium, and fine aggregate variation, restrained rubbering, repair patches and sparse cracks so seams repeat less obviously while keeping point-sampled pixel character.
- Broken mid-field hedgerows and low flint-wall hints now divide the open grass into more believable Wiltshire field boundaries without replacing the approved tree clusters or blocking the circuit read.
- Low timber post-and-rail farm fences with gate breaks now sit across selected outer fields, following the rolling background terrain height so they read as field boundaries rather than flat decals.
- Thruxton/Salisbury Plain flavour has been added as trackside/world geometry:
  - larger low-poly grandstand seating near the start line, with extra rowed spectator terraces offset along the start straight
  - start/finish gantry with signal lamps and procedural circuit board
  - checkered start/finish stripe and staggered grid-box road markings
  - small race-control hut beside the start area
  - low pit-wall/service blocks, timing screens, paddock tents and box-truck silhouettes tucked behind the start area
  - old airfield apron, simple hangars, control hut, windsock and parked light-aircraft silhouette as a Thruxton-inspired background cue
  - Japanese-style chevron boards and angular concrete corner blocks
  - low-poly tyre-stack safety bundles and red/white barrier blocks around selected corner exits
  - trackside braking marker boards on corner approaches
  - small marshal shelters with flag/figure silhouettes at selected corners
  - distant standing-stone landmark
  - flint/chalk farm wall divisions
  - distant chalk/brick farmstead clusters with simple barns, sheds, silos and small sheep blocks
  - off-track railway and brick underpass
  - pale chalk stream/field scar
- Additional supplied mountain/earth/grass references under `Assets/Tracks/Textures` are used as art direction for the generated palette and silhouette treatment. Full photo textures are not applied directly to the race scene because they fight the intended pixel-art track style.
- The track remains point-sampled so the road/grass keep the pixel-art character.

## Validation Checklist

Road test the runtime view against the reference photo:

- Horizon should be pale off-white/yellow, not saturated blue.
- Zenith should be visibly deeper blue than the horizon.
- Distant land should sit at the horizon as muted blue-green/dark-grey bands.
- Mountains and foothills should dominate the far horizon as mesh silhouettes; trees should stay smaller and nearer so they do not read as giant horizon-scale objects.
- The far horizon should avoid straight billboard-wall reads. It should layer as pale horizon haze, blue mountains, green foothills, dark valley mass, and occasional river/earth strips.
- Race-circuit furniture should never occupy or visually read as part of the active driving surface.
- Start-line seating should read as simple elevated rows placed in a few banks around the start area, not oversized buildings.
- Railway, walls, Stonehenge-style landmark and chalk stream should stay scenic and off-track.
- Road should no longer fade into blue fog.
- Grass should feel textured and clumped while remaining pixel-art.
- Sky should feel less flat than the previous solid gradient.

For deterministic capture without HUD/menu overlays:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_ring_scenery_current.png
```

Recent start-line seating smoke test:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_circuit_startline_seating_v2.png --scenery-screenshot-view elevated --scenery-screenshot-delay-ms 800
```

Recent directional horizon tint smoke test:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_circuit_horizon_sun_tint_v1.png --scenery-screenshot-view wide --scenery-screenshot-delay-ms 800
```

Recent tree-variant smoke test:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_circuit_tree_variants_v1.png --scenery-screenshot-view wide --scenery-screenshot-delay-ms 800
```

Recent irregular horizon-belt smoke test:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_circuit_irregular_horizon_belts_v1.png --scenery-screenshot-view wide --scenery-screenshot-delay-ms 800
```

This starts directly on High Speed Ring, renders the scene-only frame, saves the PNG, then exits.

Optional delayed capture:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_ring_scenery_mid.png --scenery-screenshot-delay-ms 1400
```

Optional viewpoint override:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot Telemetry\Scenery\high_speed_ring_scenery_elevated.png --scenery-screenshot-view elevated --scenery-screenshot-delay-ms 800
```

Supported views: `chase`, `rear`, `front`, `left`, `right`, `elevated`, `wide`.

Batch validation capture:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-screenshot-set Telemetry\Scenery\high_speed_ring_reference_set --scenery-screenshot-delay-ms 800
```

This writes `high_speed_ring_reference_set_chase.png`, `wide`, `elevated`, `front`, `left`, and `right` from one launch, making it easier to compare sky, horizon, trees, start-line furniture, and road/grass treatment together.

Optional foliage visibility recorder for tree blink/pop diagnosis:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-visibility-log Telemetry\Scenery\foliage_visibility.csv --auto-exit-ms 8000
```

This starts High Speed Ring directly and records transparent foliage mesh bounds against the active camera frustum every few frames.

Runtime scenery inventory:

```powershell
dotnet .\bin\Debug\net8.0-windows\RType.dll --scenery-inventory
```

This starts High Speed Ring directly, builds the real runtime mesh list, prints counts for sky/backdrop-adjacent scenery families, trees, field parcels, race furniture, airfield flavour, Salisbury/Wiltshire landmarks, and exits.

Rebuild the compiled procedural sky after editing the shader:

```powershell
dotnet tool run mgcb Content.mgcb
```

## Next Improvements

The major requested scenery systems are now in place. Further work should be based on fresh runtime art-direction feedback from driving views rather than continuing to add scenery systems speculatively.
