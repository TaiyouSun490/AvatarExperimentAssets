using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AvatarExperiments
{
    public enum SpectatorLayout { ThirdPerson, SideBySide, PictureInPicture }

    /// <summary>
    /// URP desktop views rendered after the XR camera (and its mirror blit).
    /// Both owned cameras explicitly opt out of XR. No tracking camera settings,
    /// XR mirror preferences, input bindings or time scale are changed.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class DesktopSpectator : MonoBehaviour
    {
        public Camera ParticipantCamera;
        [Tooltip("Renderers used to frame the whole demonstration, including both avatars.")]
        public Renderer[] Subjects = Array.Empty<Renderer>();
        public Transform DetailTarget;
        public SpectatorLayout Layout = SpectatorLayout.ThirdPerson;
        public bool DetailView;
        public bool ShowControls = true;
        public bool EnableOperatorInput = true;
        [Tooltip("Independent spectator visibility. Exclude world-space menus here if desired.")]
        public LayerMask SpectatorMask = ~0;
        [Range(20, 90)] public float SpectatorFieldOfView = 48;
        [Range(40, 110)] public float ParticipantFieldOfView = 75;
        [Range(.2f, .5f)] public float InsetSize = .34f;
        [Min(.15f)] public float DetailDistance = .65f;
        public float OutputDepth = 100;
        [Min(0)] public float FollowSharpness = 8;
        public FirstPersonHeadVisibility HeadVisibility;
        /// <summary>Optional live contact point in world space; null falls back to DetailTarget.</summary>
        public Func<Vector3?> DetailPointProvider;
        public Camera ThirdPersonCamera => third;
        public Camera ParticipantPreviewCamera => participant;

        private Camera third, participant;
        private FirstPersonHeadVisibility registeredVisibility;
        private Vector3 focus, pan;
        private float yaw = 135, pitch = 12, zoom = 1;
        private bool framed;
        private SpectatorLayout lastLayout;
        private bool lastDetail;
        private GUIStyle label;
        private readonly HashSet<KeyCode> heldShortcuts = new();
        private static DesktopSpectator desktopOwner;

        public void Configure(Camera source, Renderer[] subjects, FirstPersonHeadVisibility visibility = null)
        {
            ParticipantCamera = source;
            Subjects = subjects ?? Array.Empty<Renderer>();
            HeadVisibility = visibility;
            ResetView();
        }

        private void OnEnable()
        {
            Application.onBeforeRender += SyncParticipantPose;
        }
        private void OnDisable()
        {
            Application.onBeforeRender -= SyncParticipantPose;
            if (registeredVisibility != null) registeredVisibility.RemoveFirstPersonCamera(participant);
            registeredVisibility = null;
            ReleaseCamera(ref third);
            ReleaseCamera(ref participant);
            if (desktopOwner == this) desktopOwner = null;
            framed = false;
            heldShortcuts.Clear();
        }
        private void OnApplicationFocus(bool focused)
        { if (!focused) heldShortcuts.Clear(); }

        public void SetLayout(SpectatorLayout value)
        {
            Layout = value;
            zoom = 1; pan = Vector3.zero;
        }
        public void ResetView()
        {
            yaw = ParticipantCamera != null ? ParticipantCamera.transform.eulerAngles.y + 135 : 135;
            pitch = DetailView ? 20 : 12;
            zoom = 1; pan = Vector3.zero; framed = false;
        }

        private void LateUpdate()
        {
            RefreshViews(Time.unscaledDeltaTime, Screen.width, Screen.height);
        }

        /// <summary>Also usable by editor validation without running an XR session.</summary>
        public void RefreshViews(float deltaTime, int width, int height)
        {
            if (!isActiveAndEnabled || ParticipantCamera == null)
            {
                if (third != null) third.enabled = false;
                if (participant != null) participant.enabled = false;
                return;
            }
            // A desktop backbuffer has one owner, including during additive scene loads.
            if (desktopOwner != null && desktopOwner != this) return;
            desktopOwner = this;
            if (third == null) third = CreateCamera("Desktop spectator - third person");
            if (participant == null) participant = CreateCamera("Desktop spectator - participant");
            if (registeredVisibility != HeadVisibility)
            {
                if (registeredVisibility != null) registeredVisibility.RemoveFirstPersonCamera(participant);
                registeredVisibility = HeadVisibility;
                if (registeredVisibility != null) registeredVisibility.AddFirstPersonCamera(participant);
            }
            if (lastLayout != Layout || lastDetail != DetailView)
            {
                zoom = 1; pan = Vector3.zero;
                if (lastDetail != DetailView) pitch = DetailView ? 20 : 12;
                lastLayout = Layout; lastDetail = DetailView;
            }
            Viewports(Layout, width, height, InsetSize, out var thirdRect, out var participantRect);
            third.rect = thirdRect; participant.rect = participantRect;
            third.aspect = Mathf.Max(1, width) * thirdRect.width / (Mathf.Max(1, height) * thirdRect.height);
            participant.aspect = Mathf.Max(1, width) * participantRect.width / (Mathf.Max(1, height) * participantRect.height);
            third.depth = Mathf.Max(OutputDepth, ParticipantCamera.depth + 1);
            participant.depth = third.depth + 1;
            third.targetDisplay = participant.targetDisplay = ParticipantCamera.targetDisplay;
            third.cullingMask = SpectatorMask;
            participant.cullingMask = ParticipantCamera.cullingMask;
            third.fieldOfView = SpectatorFieldOfView;
            participant.fieldOfView = ParticipantFieldOfView;
            third.farClipPlane = participant.farClipPlane = Mathf.Max(10, ParticipantCamera.farClipPlane);
            participant.nearClipPlane = Mathf.Max(.01f, ParticipantCamera.nearClipPlane);
            third.backgroundColor = participant.backgroundColor = ParticipantCamera.backgroundColor;
            third.enabled = ParticipantCamera.isActiveAndEnabled;
            participant.enabled = third.enabled && Layout != SpectatorLayout.ThirdPerson;

            Bounds bounds = SubjectBounds();
            Vector3 wanted = DetailView
                ? DetailPointProvider?.Invoke() ?? (DetailTarget != null ? DetailTarget.position : bounds.center)
                : bounds.center;
            float blend = !framed || FollowSharpness <= 0 ? 1 : 1 - Mathf.Exp(-FollowSharpness * Mathf.Max(0, deltaTime));
            focus = Vector3.Lerp(focus, wanted, blend); framed = true;
            // A sphere around the bounds fits all corners at any camera heading,
            // including narrow split-screen windows and raised arms.
            float distance = DetailView ? DetailDistance : FramingDistance(bounds, SpectatorFieldOfView, third.aspect);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            third.transform.SetPositionAndRotation(focus + pan - rotation * Vector3.forward * Mathf.Max(.12f, distance * zoom), rotation);
            SyncParticipantPose();
        }

        [BeforeRenderOrder(10000)]
        private void SyncParticipantPose()
        {
            if (participant != null && ParticipantCamera != null)
                participant.transform.SetPositionAndRotation(ParticipantCamera.transform.position, ParticipantCamera.transform.rotation);
        }
        private Bounds SubjectBounds()
        {
            var result = new Bounds();
            bool any = false;
            foreach (var renderer in Subjects)
            {
                if (renderer == null || renderer.bounds.size.sqrMagnitude < .000001f) continue;
                if (!any) { result = renderer.bounds; any = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (!any)
                result = new Bounds(ParticipantCamera.transform.position + Vector3.down * .6f, new Vector3(1.2f, 1.8f, 1.2f));
            return result;
        }
        public static float FramingDistance(Bounds bounds, float verticalFov, float aspect)
        {
            float halfVertical = Mathf.Clamp(verticalFov, 10, 120) * Mathf.Deg2Rad * .5f;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * Mathf.Max(.05f, aspect));
            return Mathf.Max(.3f, bounds.extents.magnitude * 1.12f / Mathf.Sin(Mathf.Min(halfVertical, halfHorizontal)));
        }
        public static void Viewports(SpectatorLayout layout, int width, int height, float inset,
            out Rect thirdRect, out Rect participantRect)
        {
            thirdRect = new Rect(0, 0, 1, 1);
            participantRect = new Rect(0, 0, 1, 1);
            if (layout == SpectatorLayout.SideBySide)
            {
                thirdRect.width = .5f;
                participantRect = new Rect(.5f, 0, .5f, 1);
            }
            else if (layout == SpectatorLayout.PictureInPicture)
            {
                float size = Mathf.Clamp(inset, .2f, .5f);
                float xMargin = Mathf.Min(.02f, 16f / Mathf.Max(1, width));
                float yMargin = Mathf.Min(.02f, 16f / Mathf.Max(1, height));
                // Lower right leaves the operator's top bar and scene faces clear.
                participantRect = new Rect(1 - size - xMargin, yMargin, size, size);
            }
        }
        private static Camera CreateCamera(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.nearClipPlane = .03f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false; camera.allowMSAA = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.renderShadows = true;
            return camera;
        }
        private static void ReleaseCamera(ref Camera camera)
        {
            if (camera == null) return;
            camera.enabled = false;
            CoreUtils.Destroy(camera.gameObject); camera = null;
        }

        private void ReadInput(Event input)
        {
            if (!EnableOperatorInput || !Application.isFocused) return;
            // Desktop window events work with either Unity input backend and
            // with presentation/remoting tools that send ordinary window keys.
            // Use a single shortcut path so one press cannot toggle twice.
            bool shortcut = input.keyCode == KeyCode.F2 || input.keyCode == KeyCode.F3 ||
                input.keyCode == KeyCode.F4 || input.keyCode == KeyCode.Home;
            if (shortcut && input.type == EventType.KeyUp) heldShortcuts.Remove(input.keyCode);
            if (shortcut && input.type == EventType.KeyDown)
            {
                if (heldShortcuts.Add(input.keyCode))
                {
                    if (input.keyCode == KeyCode.F2) SetLayout((SpectatorLayout)(((int)Layout + 1) % 3));
                    if (input.keyCode == KeyCode.F3) DetailView = !DetailView;
                    if (input.keyCode == KeyCode.F4) ShowControls = !ShowControls;
                    if (input.keyCode == KeyCode.Home) ResetView();
                }
                input.Use();
                return;
            }
            if (input.type != EventType.MouseDrag && input.type != EventType.ScrollWheel) return;
            int width = Screen.width, height = Screen.height;
            Viewports(Layout, width, height, InsetSize, out var viewport, out var inset);
            var mouse = input.mousePosition;
            var normalized = new Vector2(mouse.x / Mathf.Max(1, width), 1 - mouse.y / Mathf.Max(1, height));
            float uiScale = Mathf.Clamp(width / 1000f, .65f, 1.5f);
            if (!viewport.Contains(normalized) ||
                (Layout == SpectatorLayout.PictureInPicture && inset.Contains(normalized)) ||
                (ShowControls && (mouse.y < 80 * uiScale || mouse.y > height - 40 * uiScale))) return;
            if (input.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(input.delta.y * .06f), .15f, 5);
                input.Use();
            }
            else if (input.button == 1)
            {
                yaw += input.delta.x * .2f;
                pitch = Mathf.Clamp(pitch + input.delta.y * .2f, -80, 80);
                input.Use();
            }
            else if (input.button == 2 && third != null)
            {
                float metresPerPixel = 2 * Vector3.Distance(third.transform.position, focus + pan) *
                    Mathf.Tan(SpectatorFieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, height);
                pan -= (third.transform.right * input.delta.x - third.transform.up * input.delta.y) * metresPerPixel;
                input.Use();
            }
        }

        // IMGUI is the desktop operator UI; it is never used for VR interaction.
        private void OnGUI()
        {
            if (desktopOwner != this || third == null || !third.enabled) return;
            ReadInput(Event.current); // F4 must also work while all controls are hidden.
            if (!ShowControls) return;
            var oldMatrix = GUI.matrix;
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && EnableOperatorInput;
            float scale = Mathf.Clamp(Screen.width / 1000f, .65f, 1.5f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            GUI.Box(new Rect(10, 10, 600, 38), GUIContent.none);
            if (GUI.Button(new Rect(18, 17, 200, 24), "F2  " + LayoutName(Layout)))
                SetLayout((SpectatorLayout)(((int)Layout + 1) % 3));
            if (GUI.Button(new Rect(224, 17, 160, 24), DetailView ? "F3  Contact close-up" : "F3  Whole scene"))
                DetailView = !DetailView;
            if (GUI.Button(new Rect(390, 17, 100, 24), "Home  Reset")) ResetView();
            if (GUI.Button(new Rect(496, 17, 104, 24), "F4  Hide UI")) ShowControls = false;
            GUI.Label(new Rect(18, 53, 240, 24), "SPECTATOR", label);
            if (Layout == SpectatorLayout.SideBySide)
                GUI.Label(new Rect(width * .5f + 18, 53, 240, 24), "PARTICIPANT", label);
            else if (Layout == SpectatorLayout.PictureInPicture)
            {
                Viewports(Layout, Screen.width, Screen.height, InsetSize, out _, out var rect);
                GUI.Label(new Rect(rect.x * width + 8, (1 - rect.yMax) * height + 8, 240, 24), "PARTICIPANT", label);
            }
            GUI.Box(new Rect(10, height - 36, Mathf.Min(width - 20, 780), 26),
                "Right drag: orbit   |   Middle drag: pan   |   Wheel: zoom   |   F4: show / hide controls");
            GUI.matrix = oldMatrix;
            GUI.enabled = oldEnabled;
        }
        private static string LayoutName(SpectatorLayout layout) => layout switch
        {
            SpectatorLayout.SideBySide => "Side by side",
            SpectatorLayout.PictureInPicture => "Picture in picture",
            _ => "Third person"
        };
    }
}
