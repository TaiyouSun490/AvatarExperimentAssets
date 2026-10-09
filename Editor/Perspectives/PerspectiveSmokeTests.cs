using System;
using AvatarExperiments.Perspectives;
using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.Perspectives.Editor
{
    public static class PerspectiveSmokeTests
    {
        private static int checks;

        [MenuItem("Tools/Avatar Experiments/Perspectives/Run smoke tests (no hardware)")]
        public static void Run()
        {
            checks = 0;
            var root = new GameObject("Temporary perspective tests");
            TextAsset catalog = null;
            try
            {
                var assets = AssetDatabase.FindAssets("perspective-catalog t:TextAsset");
                Check(assets.Length == 1, "Exactly one installed catalog");
                catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(assets[0]));
                var data = PerspectiveCatalog.Parse(catalog.text);
                Check(data.profiles.Length == 5 && data.protocols.Length == 6, "Packaged profile/protocol catalog");
                Expect<ArgumentException>(() => PerspectiveCatalog.Parse(catalog.text.Replace("\"first_person\"", "\"missing\"")), "Missing first-person rejected");
                var stage = Child(root.transform, "Stage");
                stage.SetPositionAndRotation(new Vector3(4, 0, 3), Quaternion.Euler(0, 90, 0));
                var avatar = Child(stage, "Avatar"); avatar.localPosition = Vector3.forward;
                var observer = Child(root.transform, "Observer");
                var head = Child(observer, "Tracked head");
                head.localPosition = new Vector3(0.1f, 1.3f, 0.2f);
                var fade = Child(head, "Fade").gameObject.AddComponent<CanvasGroup>();
                var controller = observer.gameObject.AddComponent<PerspectiveController>();
                controller.bodyStage = stage; controller.observerRoot = observer; controller.trackedHead = head;
                controller.fadeSurface = fade; controller.catalogAsset = catalog;
                Check(controller.Initialize(out _), "Initialize valid branches");
                Check(!controller.Initialize(out _), "Repeated initialize cannot reset an active session");
                Check(!controller.Resume(), "Tracking must be confirmed");
                controller.SetTrackingValid(true);
                Check(controller.Resume(), "Explicit resume");
                var avatarPosition = avatar.position;
                var localHead = new Pose(head.localPosition, head.localRotation);
                Check(controller.TryChange("rear_120", out _), "Rear transition starts");
                Check(!controller.TryChange("first_person", out _), "Concurrent transition rejected");
                controller.Tick(10);
                Check(fade.alpha == 1 && controller.IsTransitioning, "Long frame still presents black before fade-in");
                controller.Tick(0.31f);
                Near(observer.position, stage.position + stage.rotation * new Vector3(0, 0, -1.2f), "Stage-local offset under 90-degree yaw");
                Near(avatar.position, avatarPosition, "Avatar remains fixed");
                Near(head.localPosition, localHead.position, "Tracked head position untouched");
                head.localRotation = Quaternion.Euler(0, 80, 0);
                var viewPosition = observer.position; controller.Tick(1);
                Near(observer.position, viewPosition, "Head rotation cannot orbit the observer");
                Check(controller.TryBeginTrial(), "Begin trial at stable viewpoint");
                Check(!controller.TryChange("first_person", out _), "No perspective switch during a trial");
                int invalidations = 0;
                controller.onEvent.AddListener(json => { if (json.Contains("trial_invalidation_required")) invalidations++; });
                controller.SetTrackingValid(false);
                Check(controller.Paused && !controller.TrialActive && invalidations == 1, "Tracking loss invalidates active trial once");
                controller.SetTrackingValid(false); controller.Pause();
                Check(invalidations == 1, "Repeated invalid tracking does not invalidate twice");
                controller.SetTrackingValid(true);
                Check(controller.Paused, "Tracking recovery never auto-resumes");
                Check(!controller.SetCalibratedOverlap(float.NaN), "Nonfinite overlap rejected");
                Check(!controller.SetCalibratedOverlap(0), "Zero overlap shift rejected");
                controller.Resume();
                Check(!controller.TryChange("partial_overlap", out _), "Uncalibrated overlap blocked");
                controller.Pause();
                Check(controller.SetCalibratedOverlap(-0.22f), "Signed participant-specific overlap supported");
                controller.Resume(); controller.TryChange("partial_overlap", out _); Finish(controller);
                Near(controller.CurrentOffset, new Vector3(-0.22f, 0, 0), "Calibration used, not mean 0.25m");
                Check(!controller.TryChange("unknown", out _), "Unknown profile rejected");
                Expect<ArgumentOutOfRangeException>(() => controller.Tick(float.NaN), "Invalid timer rejected");
                controller.TryChange("first_person", out _); Finish(controller);
                Near(observer.position, stage.position, "Zero offset restores first person");
                Check(controller.TryChange("rear_120", out _), "Transition for cancellation");
                controller.Tick(0.1f); controller.Pause();
                Near(observer.position, stage.position, "Cancel before switch preserves previous view");
                Check(fade.alpha == 0 && !controller.IsTransitioning, "Pause clears fade and transition");
                controller.Resume();
                var protocol = observer.gameObject.AddComponent<PerspectiveProtocol>(); protocol.perspective = controller;
                Check(protocol.Begin("guided_alternation", out _), "Start guided protocol");
                Check(!protocol.Continue(out _), "Continue blocked during transition");
                Finish(controller); protocol.Tick(20);
                Check(protocol.Continue(out _) && protocol.StepIndex == 1, "Continue after confirmed dwell");
                controller.Tick(0.1f); controller.Pause(); protocol.Tick(1);
                Check(protocol.DwellSeconds == 0, "Interruption resets dwell");
                controller.Resume(); Check(protocol.RestartStep(out _), "Interrupted destination can restart"); Finish(controller);
                protocol.Tick(20); Check(protocol.Continue(out _), "Advance to first person"); Finish(controller);
                protocol.Tick(20); Check(protocol.Continue(out _), "Advance to rear"); Finish(controller);
                protocol.Tick(20); Check(protocol.Continue(out _) && protocol.Complete && !protocol.Running, "Protocol completes only after confirmation");
                controller.Pause(); stage.position += Vector3.right;
                Check(!controller.Resume(), "Changed stage requires recalibration");
                Check(controller.Recalibrate(out _), "Explicit recalibration"); controller.Resume();
                Check(!controller.TryChange("partial_overlap", out _), "Recalibration clears overlap confirmation");
                var source = Child(root.transform, "Source input frame");
                source.SetPositionAndRotation(new Vector3(-3, 0, 1), Quaternion.Euler(0, -40, 0));
                var pose = new Pose(new Vector3(.3f, 1.2f, .1f), Quaternion.Euler(10, 20, 30));
                var world = TrackingSpacePose.FromLocal(pose, source);
                var mapped = TrackingSpacePose.MapWorld(world, source, stage);
                Near(mapped.position, stage.TransformPoint(pose.position), "FBT/Movement world pose rebased once");
                Check(Quaternion.Angle(mapped.rotation, stage.rotation * pose.rotation) < .01f, "Pose orientation rebased once");
                source.localScale = Vector3.one * 2;
                Expect<ArgumentException>(() => TrackingSpacePose.MapWorld(world, source, stage), "Scaled tracking frame rejected");
                source.localScale = Vector3.one;
                Expect<ArgumentException>(() => TrackingSpacePose.FromLocal(new Pose(Vector3.zero, new Quaternion(0, 0, 0, 0)), stage), "Invalid rotation rejected");
                Expect<ArgumentException>(() => TrackingSpacePose.FromLocal(new Pose(new Vector3(float.NaN, 0, 0), Quaternion.identity), stage), "Nonfinite pose rejected");
                var setupStage = Child(root.transform, "Setup stage");
                setupStage.position = new Vector3(2, 0, 3);
                var branch = Child(setupStage, "Rendering branch");
                var setupCamera = Child(branch, "Camera").gameObject.AddComponent<Camera>();
                setupCamera.transform.localPosition = new Vector3(0, 1.4f, 0);
                var cameraBefore = setupCamera.transform.position;
                var generated = PerspectiveSetupWindow.Create(setupStage, branch, setupCamera, catalog);
                try
                {
                    Near(setupCamera.transform.position, cameraBefore, "Editor setup preserves existing camera pose");
                    Check(!branch.IsChildOf(setupStage), "Editor setup separates rendering from body");
                    Check(generated.ValidateBindings(out _), "Generated bindings are valid");
                    Check(generated.fadeSurface.GetComponentInChildren<UnityEngine.UI.Image>().color == Color.black, "Generated fade has an opaque black image");
                    Check(generated.GetComponent<PerspectiveActions>().protocol.perspective == generated, "Generated UI facade and protocol connected");
                    Check(generated.fadeSurface.GetComponent<Canvas>().worldCamera == setupCamera, "Fade assigned to tracked camera");
                }
                finally { UnityEngine.Object.DestroyImmediate(generated.gameObject); }
                controller.Pause(); observer.SetParent(stage, true);
                Check(!controller.ValidateBindings(out _), "Coupled body/observer hierarchy rejected");
                observer.SetParent(root.transform, true); stage.localScale = Vector3.one * 2;
                Check(!controller.ValidateBindings(out _), "Scaled body stage rejected");
                stage.localScale = Vector3.one; stage.rotation = Quaternion.Euler(10, 0, 0);
                Check(!controller.ValidateBindings(out _), "Tilted stage rejected");
                Debug.Log("PERSPECTIVES_SMOKE_PASS " + checks + " checks; no hardware, rendering or SDK integration verification.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        private static Transform Child(Transform parent, string name)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        private static void Finish(PerspectiveController controller) { controller.Tick(.31f); controller.Tick(.31f); }
        private static void Near(Vector3 actual, Vector3 expected, string label) => Check(Vector3.Distance(actual, expected) < .0001f, label);
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception("Perspective smoke test failed: " + label); checks++; }
        private static void Expect<T>(Action action, string label) where T : Exception
        { try { action(); } catch (T) { checks++; return; } throw new Exception("Expected exception: " + label); }
    }
}
