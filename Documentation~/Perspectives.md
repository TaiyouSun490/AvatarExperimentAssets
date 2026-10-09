# First- and third-person perspectives

`AvatarExperiments.Perspectives` separates a calibrated body/input frame from the
rendering observer. It depends on Unity and uGUI, not on Movement SDK, FinalIK,
SteamVR or a particular full-body tracker. The containing package retains its
existing Unity 6000.5 / OpenXR / URP requirements; this does not certify Quest
compatibility of every other module in the package.

## What is included

- Five configurable spatial profiles and six separate, participant-confirmed protocols.
- Fade-out, offset application while black, fade-in; tracked head pose remains untouched.
- Trial lock, explicit tracking status, interruption, resume/restart and recalibration gates.
- Provider-neutral local/world pose conversion and JSON events for a host recorder.
- Editor setup and hardware-free smoke tests. No participant experiment, EEG recorder,
  tracking driver, avatar model or SDK-specific retargeter is bundled.

## Coordinate contract

```text
BodyStage A                        // fixed floor, position and calibrated forward
  physical tracking / IK targets  // A * tracked local pose
  avatar (or independent root driven in A)
Observer A * O                     // sibling of BodyStage, unit world scale
  rendering-only rig
    tracked head H                // XR runtime writes H
```

The rendered head is `A * O * H`. The avatar remains `A * J`, where J is the
tracked/retargeted body pose. X is right, Y up, Z calibrated forward. O is a
translation, never a head-yaw-dependent orbit or a LookAt camera. No component in
this module changes avatar bones, body scale or the HMD's local pose.

`PerspectiveController` requires independent branches, unit world scales and a
yaw-only stage. Scene re-centering is an explicit pause/calibration operation.
It must not silently change the stage during a task. Perform recentering behind
the host's pause/calibration screen, update both tracking and body mapping, call
`Recalibrate`, reconfirm tracking, then wait for the participant to resume.

## Setup

1. Start with a working avatar and XR camera in the host project. Confirm which
   component owns HMD pose updates and which coordinate frame supplies body poses.
2. Open `Tools > Avatar Experiments > Perspectives > Create observer setup`.
   Assign the calibrated body/input stage, the **rendering-only** branch, tracked
   camera and `Runtime/Perspectives/Presets/perspective-catalog.json`.
   The operation reparents that branch under a new observer using Unity Undo.
   Do not select a branch containing body targets or a solved avatar.
3. For this package's `AvatarExperimentSetup`, assign the generated controller to
   its `Perspective` field. The setup then gives XrSession a separate camera origin,
   places UI rays in that observer frame, and shows the head in third person.
   The original `TrackingOrigin` still drives body targets and physical calibration.
   Leaving `Perspective` and `ObserverOrigin` empty preserves existing behavior.
4. Initialize through `PerspectiveActions.Initialize`. An SDK adapter must report
   fresh head **and required body** tracking through `SetTrackingValid(bool)`.
   Recovered tracking does not resume automatically. Participant confirmation calls
   `PerspectiveActions.Resume`, then `StartProtocol` and `Continue` as indicated.
5. Bind participant-visible controls to `PerspectiveActions` methods and display
   `onStatus`, protocol `Instruction`, dwell and `CanContinue`. These are plain
   UnityEvents and public APIs; no vendor button is hardcoded. The Inspector buttons
   are developer controls, not a replacement for the participant's pause/continue UI.
6. Subscribe both controller and protocol `onEvent` streams to the host recorder.
   Persist the catalog snapshot, its hash, selected protocol and actual individual
   calibration, plus host/session/trial IDs. This library does not persist data itself.
7. The task may begin only after `protocol.Complete`. Call `TryBeginTrial` and only
   present a stimulus on success. Keep the lock through choice **and confidence**;
   call `EndTrial` afterward. On `trial_invalidation_required`, invalidate the task's
   active trial, hide stimuli and stop its audio through the host state machine.

The fade is a camera-space black Canvas. Verify both eyes, near plane, camera
stacking and any compositor overlays in the actual XR pipeline. An external
passthrough/compositor layer can bypass ordinary scene UI and must be handled by
the host. Set up the fade before use; do not substitute an empty CanvasGroup.

## Movement SDK and FBT integration

| Host | Body/input mapping | Rendering and validity |
| --- | --- | --- |
| Meta Movement SDK | Preserve SDK source tracking-local poses; retarget them in BodyStage. If a provider exposes world poses after the observer transform, use `TrackingSpacePose.MapWorld(sourcePose, sourceFrame, bodyStage)` at the adapter boundary. | Put the rendering rig in the observer branch, keep HMD pose ownership with the Meta rig. Report actual head/body validity; no `XrSession` on the same camera. |
| Existing OpenXR + optional FinalIK setup | `AvatarTrackingInput` continues to transform device-local head/hand poses through `AvatarRig.TrackingOrigin`. | Assign the optional Perspective field. `XrSession` alone writes HMD local pose; a second Tracked Pose Driver must not also write it. |
| External full-body tracking | Convert measured joints/solver targets from the provider's documented frame into BodyStage, using `FromLocal` or `MapWorld`. Retarget before displaying. | The observer uses the HMD tracking frame calibrated to the same A. Aggregate required tracker validity/age and call `SetTrackingValid`. Do not substitute a frozen skeleton for a tracked frame. |

`MapWorld` is a rigid coordinate conversion, not skeleton retargeting or automatic
calibration. Tracker offsets, joint conventions, latency, estimated-vs-measured
joints and execution order remain adapter responsibilities. Rebase a completed
pose once; do not offset both solver targets and the solved skeleton.

During third-person use, perform physical joint measurement/body resizing after
returning to first person. The setup's resize guard rejects changes while trials
or transitions are active and while an offset is applied. Custom hosts must provide
the equivalent guard. Pause/menu policy and shortcut conflicts belong to the host.

## Research-derived profiles, not an optimum claim

| Profile | Observer offset (m) | Interpretation |
| --- | --- | --- |
| first_person | (0, 0, 0) | Baseline |
| rear_120 | (0, 0, −1.2) | Distance used by Galvan Debarba et al. (2017); not an optimized metacognitive dose |
| partial_overlap | (individual, 0, 0) | Requires `SetCalibratedOverlap(signedMetres)` while paused; 0.25 m is only a study mean/reference |
| lateral_080 | (+0.8, 0, 0) | Separation comparison; verify actual non-overlap for the user's body/posture |
| rear_elevated | (0, +0.3, −1.2) | Exploratory engineering value, no claimed optimal height |

`fixed_first`, `fixed_rear`, `fixed_partial`, `fixed_lateral`, and `fixed_elevated`
start in first person for 20 seconds, then hold the selected view for 60 seconds.
`guided_alternation` uses first/rear/first/rear, each for 20 seconds. All durations
exclude transitions, pauses and user confirmation. Every step requires confirmation.
These are tunable examples; original studies did not prescribe these dwell times.
Different transition counts and first-person exposure remain possible confounds.

The catalog is an explicit TextAsset reference. To customize it, duplicate the
asset into the consumer project's Assets, edit offsets/dwell/protocol steps and
assign that copy **before initialization**. Freeze it for a session. Partial-overlap
calibration is cleared on recentering. Mirrored lateral offsets are supported;
preserve the sign in recorded metadata.

References:

- [Galvan Debarba et al. 2017](https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0190109): rear 1.2 m, 1PP/3PP/alternation, multisensory congruence. Congruent conditions did not show a significant ownership difference; this is not proof of equivalence. The paper used a 200 ms translation with blur; this module uses fades as a distinct engineering choice.
- [Maselli & Slater 2014](https://www.frontiersin.org/journals/human-neuroscience/articles/10.3389/fnhum.2014.00693/full): total/partial/no overlap, individually set lateral shift (mean 25 cm), 80 cm separation, synchronous leg stimulation. Geometry alone does not reproduce that protocol.
- [Wolf et al. 2022](https://www.frontiersin.org/journals/virtual-reality/articles/10.3389/frvir.2022.1031093/full): distances 1/2.5/4 m refer to a virtual mirror, not observer-to-avatar separation.

None of these establishes the best third-person geometry for metacognitive improvement.
Task-specific reflection prompts and audio conditions belong in the consuming experiment.

## Validation

Run `Tools > Avatar Experiments > Perspectives > Run smoke tests (no hardware)`.
Batch entry point: `AvatarExperiments.Perspectives.Editor.PerspectiveSmokeTests.RunBatch`.
The test creates temporary objects and does not access participant settings.

The isolated perspective assemblies were compiled and tested using Unity
6000.0.74f1 / uGUI 2.0.0. The containing package's 6000.5 dependencies were not
downgraded. See [PerspectiveValidation.md](PerspectiveValidation.md) for the exact
run and scope. Complete package integration, headset rendering, body ownership,
Movement SDK/FBT adapters and Android performance require their own runtime checks.
