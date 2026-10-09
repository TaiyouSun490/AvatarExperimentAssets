# Perspective validation — 2026-10-09

Implemented: spatial catalog, fixed/alternating procedures, fade controller,
participant UI entry points, rigid tracking-space conversions, Editor setup and
optional origin/head-visibility/UI-ray integration with AvatarExperimentSetup.

The isolated runtime and Editor assemblies compile in Unity 6000.0.74f1 with
uGUI 2.0.0. `PerspectiveSmokeTests.RunBatch` reports **55 passing checks**.
Checks exercise real Unity Transforms and include:

- Zero, rear, rotated-stage and signed individual lateral offsets.
- Unchanged avatar and tracked-head local pose; head yaw cannot orbit the view.
- Trial locking, invalidation on tracking loss, no automatic resume, cancellation
  and restarting an interrupted destination step.
- Mandatory individual overlap calibration, clearing it after recentering,
  malformed profile/pose/time rejection and independent unscaled branches.
- Protocol dwell/confirmation/completion, world/local pose rebasing.
- Editor-created hierarchy and references, preserved camera pose, black fade image
  and its explicit target camera.

Reproduce in a **new** temporary directory with `Tools~/TestPerspectives.ps1`:

```powershell
./Tools~/TestPerspectives.ps1 -UnityEditor '<Unity Editor executable>' -ProjectPath '<new temporary project>'
```

This copies only the two perspective module directories into a clean project.
It does not require the source experiment or its avatars and does not overwrite an
existing validation project. It is an isolated module check, not a test of the
entire package at a lower Unity version.

The existing package minimum (Unity 6000.5, URP 17.5, uGUI 2.5 and declared XR
dependencies) is unchanged. Modifications to its optional XrSession/setup,
head-hiding and calibration-menu integration were inspected but the full package
was not compiled or exercised with that exact environment in this run.

Still required before an experiment: host participant UI reachability and input
conflicts; both-eye fade with the selected compositor; Quest 3/3S frame timing;
Movement source-pose space and execution order; FBT measured-joint validity;
head/hand/body correspondence through view changes; recentering and tracking-loss
recovery in the headset; body ownership and metacognitive outcomes. No hardware,
Android build, participant data or physiological synchronization was used here.
