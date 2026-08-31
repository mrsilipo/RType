# Generated EK9 Reference Rig

This folder marks the runtime diagnostic replacement model.

Runtime/game coordinate convention used by `classicFourWheel` and the in-game procedural rig:

- +Z = vehicle forward / nose
- +X = vehicle right
- +Y = up
- vehicle origin = physics CG reference on the ground-projected vehicle centreline
- front axle centre = Z +0.9956 m
- rear axle centre = Z -1.6244 m
- wheelbase = 2.620 m
- front/rear track = 1.480 m
- front wheel steering yaw is read directly from `VehicleState.FrontLeftSteerAngleDegrees` and `FrontRightSteerAngleDegrees`

The in-game reference model is currently generated procedurally by `Rendering/GeneratedEk9ReferenceModelFactory.cs` so it does not depend on imported FBX pivots or mesh bounds.

Blender source coordinate convention:

- +X = vehicle forward / nose
- +Y = vehicle right
- +Z = up

The Blender script maps runtime/game coordinates to Blender coordinates as:

- Blender X = physics Z
- Blender Y = physics X
- Blender Z = physics Y

Expected Blender wheel centres:

- FL = X +0.9956 m, Y -0.740 m, Z +0.298 m
- FR = X +0.9956 m, Y +0.740 m, Z +0.298 m
- RL = X -1.6244 m, Y -0.740 m, Z +0.298 m
- RR = X -1.6244 m, Y +0.740 m, Z +0.298 m

In Blender, the front wheels steer around local/world +Z. Wheel rolling rotation is around the lateral wheel axis, local +Y/-Y depending on side.

`generate_generated_ek9.py` is a Blender script for creating a matching source `.blend` and FBX export on a machine with Blender installed.
