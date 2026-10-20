using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AvatarExperiments.VirtualHand.Editor
{
    static class VirtualHandSpectatorVerification
    {
        public static void Run(TabletopExperiment e, Action<bool, string> check)
        {
            var adapter = TabletopSpectatorSetup.Configure(e);
            var spectator = adapter.spectator;
            var camera = e.study.presenter.participantCamera;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            var projection = camera.projectionMatrix; var rect = camera.rect; var target = camera.targetTexture;
            var eye = camera.stereoTargetEye; var xr = cameraData.allowXRRendering;
            var space = e.trackedBrush.trackingSpace.localToWorldMatrix;
            var hand = e.hand.transform.localToWorldMatrix;
            var mapped = e.trackedBrush.MapToVisual(e.trackedBrush.RawTip);
            try
            {
                foreach (SpectatorLayout layout in Enum.GetValues(typeof(SpectatorLayout)))
                {
                    spectator.SetLayout(layout); spectator.RefreshViews(1, 1600, 900);
                    check(spectator.ThirdPersonCamera && spectator.ThirdPersonCamera.enabled, layout + ": third-person PC camera active");
                    check(spectator.ParticipantPreviewCamera.enabled == (layout != SpectatorLayout.ThirdPerson), layout + ": participant inset lifecycle");
                    check(!spectator.ThirdPersonCamera.GetUniversalAdditionalCameraData().allowXRRendering &&
                        !spectator.ParticipantPreviewCamera.GetUniversalAdditionalCameraData().allowXRRendering &&
                        spectator.ThirdPersonCamera.stereoTargetEye == StereoTargetEyeMask.None &&
                        spectator.ParticipantPreviewCamera.stereoTargetEye == StereoTargetEyeMask.None, layout + ": PC cameras excluded from XR");
                }
                check(camera.transform.position == position && camera.transform.rotation == rotation && camera.rect == rect &&
                    camera.projectionMatrix == projection && camera.targetTexture == target &&
                    camera.stereoTargetEye == eye && cameraData.allowXRRendering == xr, "HMD camera pose, projection, viewport and XR settings preserved");
                check(e.trackedBrush.trackingSpace.localToWorldMatrix == space && e.hand.transform.localToWorldMatrix == hand &&
                    e.trackedBrush.MapToVisual(e.trackedBrush.RawTip) == mapped, "Spectator leaves physical brush and hand coordinates unchanged");
                e.ResetTrial();
                e.study.protocol = new StudyProtocol { restSeconds = 0, drift = new DriftSettings { repetitions = 1 } };
                e.study.protocol.conditions = new List<StudyCondition> {
                    new StudyCondition { code = "spectator_verification", stimulus = new TrialSettings {
                        preparation = 0, induction = .2f, melting = .1f, observation = 0, enableMelting = false } }
                };
                e.trackedBrush.Sample(e.trackedBrush.SampleTime + .01, new Pose(e.trackedBrush.trackingSpace.InverseTransformPoint(
                    e.trackedBrush.MapToReal(e.study.virtualTarget.position)), Quaternion.identity));
                e.study.CapturePhysicalTarget(); e.study.setupChecked = true;
                e.study.BeginStudy();
                check(e.study.Stage == StudyStage.Baseline, "Spectator scene enters calibrated baseline");
                adapter.RefreshVisibility();
                check(!spectator.enabled && !spectator.ThirdPersonCamera && !spectator.ParticipantPreviewCamera,
                    "Baseline hides all spectator cameras and controls");
                e.study.SubmitMarker(); e.study.StartStimuli();
                check(e.study.Stage == StudyStage.Stimulation, "Spectator study starts stimulus sequence");
                adapter.RefreshVisibility(); spectator.RefreshViews(1, 1600, 900);
                check(spectator.enabled && spectator.ThirdPersonCamera.enabled, "Spectator returns for visible stimulation");
                e.Advance(1); e.study.SendMessage("Update"); adapter.RefreshVisibility();
                check(e.study.Stage == StudyStage.PostMeasurement && !spectator.enabled, "Post-measurement stays occluded on the PC as well as HMD");
                e.study.Abort("Synthetic spectator verification complete"); adapter.RefreshVisibility(); spectator.RefreshViews(1, 1600, 900);
                check(spectator.enabled && spectator.ThirdPersonCamera.enabled && !e.vibration.Active, "Abort restores spectator with outputs stopped");
                adapter.showDesktopView = false; adapter.RefreshVisibility();
                check(!spectator.enabled, "Operator can return PC output to the original participant view");
            }
            finally
            {
                if (e.study.Active) e.study.Abort("Spectator verification cleanup");
                UnityEngine.Object.DestroyImmediate(adapter.gameObject);
                e.ResetTrial();
            }
        }
    }
}
