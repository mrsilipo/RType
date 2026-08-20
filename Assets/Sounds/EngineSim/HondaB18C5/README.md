# Honda B18C5 Engine Sim Profile

This folder contains the local runtime subset of the Engine Simulator Honda VTEC profile used by the EK9 audio and power model.

Runtime files:

- `assets/engines/honda_b18c5_vtec.mr`
- `es/objects/objects.mr`
- `es/sound-library/impulse_responses.mr`
- `es/sound-library/new/mild_exhaust.wav`

These files are intentionally copied into the GranTurismo asset tree so the game does not depend on the original `engine-sim-v0.1.14a` package layout at runtime. The source material is from the MIT-licensed Engine Simulator project; see `LICENSE.engine-sim.txt` and `THIRD_PARTY_NOTICES.md`.
