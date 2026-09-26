using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarExperiments
{
    /// <summary>
    /// Hides only the local avatar's head during its first-person camera render.
    /// Keeps the source mesh/bones intact for IK and GPU skinning, and restores
    /// normal materials for nested mirror cameras and after rendering.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonHeadVisibility : MonoBehaviour
    {
        public bool HideHead = true;
        [Tooltip("Normally only XR hides the head; desktop avatar inspection stays unchanged.")]
        public bool HideOutsideVR;
        public Camera FirstPersonCamera { get; private set; }
        public int HeadRendererCount => parts.Count;
        public int HiddenRenderCount { get; private set; }
        public bool IsHeadHidden => hidden;

        private sealed class Part
        {
            public Renderer Renderer;
            public bool EntireHead;
            public bool[] HeadSlots;
            public readonly List<Material> Original = new();
            public readonly List<Material> Hidden = new();
            public ShadowCastingMode Shadows;
            public bool ForceOff;
        }
        private struct Frame { public Camera Camera; public bool WasHidden; }
        private readonly List<Part> parts = new();
        private readonly List<Frame> frames = new(4);
        private readonly Dictionary<Material, Material> shadowMaterials = new();
        private XrSession session;
        private bool hidden;

        // LightMode tags, not shader pass names. Keep the original ShadowCaster,
        // including alpha clipping / full-body dissolve and property blocks.
        private static readonly string[] ViewPasses =
        {
            "UniversalForward", "UniversalForwardOnly", "UniversalGBuffer",
            "SRPDefaultUnlit", "DepthOnly", "DepthNormals", "DepthNormalsOnly",
            "MotionVectors", "XRMotionVectors", "Universal2D", "Meta"
        };

        public void Configure(Animator avatar, Camera camera, XrSession xrSession)
        {
            Restore();
            parts.Clear();
            FirstPersonCamera = camera;
            session = xrSession;
            if (avatar == null || !avatar.isHuman) return;
            var head = avatar.GetBoneTransform(HumanBodyBones.Head);
            var neck = avatar.GetBoneTransform(HumanBodyBones.Neck);
            if (head == null) return;
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                bool entireHead = renderer.transform.IsChildOf(head);
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var bones = skin.bones;
                    bool any = false, all = true;
                    foreach (var bone in bones)
                    {
                        if (bone == null) continue;
                        any = true;
                        // Hair can have small neck weights. A shirt containing a
                        // head bone is NOT a head renderer: all bones must match.
                        all &= bone == head || bone.IsChildOf(head) ||
                            (neck != null && (bone == neck || bone.IsChildOf(neck)));
                    }
                    entireHead |= any && all;
                }
                var materials = renderer.sharedMaterials;
                var slots = new bool[materials.Length];
                bool hasHeadSlot = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    string label = materials[i] != null ? materials[i].name.ToLowerInvariant() : "";
                    // CC avatars combine body and face in one renderer; never
                    // remove the body/arm mesh or change its vertex indices.
                    slots[i] = label.Contains("skin_head") || label.Contains("skin_face") || label.Contains("eyelash");
                    hasHeadSlot |= slots[i];
                }
                if (entireHead || hasHeadSlot)
                    parts.Add(new Part { Renderer = renderer, EntireHead = entireHead, HeadSlots = slots });
            }
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            RenderPipelineManager.endContextRendering += EndContext;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RenderPipelineManager.endContextRendering -= EndContext;
            Restore();
        }
        private void OnDestroy()
        {
            Restore();
            foreach (var material in shadowMaterials.Values) CoreUtils.Destroy(material);
            shadowMaterials.Clear();
        }

        private bool ShouldHide(Camera camera) => HideHead && camera == FirstPersonCamera &&
            (HideOutsideVR || camera.stereoEnabled || (session != null && session.Running));

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool hide = ShouldHide(camera);
            if (frames.Count == 0)
            {
                if (!hide) return;
                Capture();
            }
            frames.Add(new Frame { Camera = camera, WasHidden = hidden });
            Apply(hide);
            if (hide) HiddenRenderCount++;
        }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            int index = frames.Count - 1;
            if (index < 0 || frames[index].Camera != camera) return;
            bool restoreHidden = frames[index].WasHidden;
            frames.RemoveAt(index);
            Apply(restoreHidden);
        }
        private void EndContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Nested SubmitRenderRequest contexts must not clear the outer
            // camera's stack. EndCamera does the matching restoration instead.
            if (frames.Count == 0) Apply(false);
        }

        private void Capture()
        {
            foreach (var part in parts)
            {
                var renderer = part.Renderer;
                if (renderer == null) continue;
                part.Shadows = renderer.shadowCastingMode;
                part.ForceOff = renderer.forceRenderingOff;
                if (part.EntireHead) continue;
                renderer.GetSharedMaterials(part.Original);
                part.Hidden.Clear();
                for (int i = 0; i < part.Original.Count; i++)
                {
                    var material = part.Original[i];
                    part.Hidden.Add(i < part.HeadSlots.Length && part.HeadSlots[i] && material != null
                        ? ShadowOnly(material) : material);
                }
            }
        }
        private Material ShadowOnly(Material source)
        {
            if (!shadowMaterials.TryGetValue(source, out var copy) || copy == null)
            {
                copy = new Material(source) { name = source.name + " (First Person Shadow Only)", hideFlags = HideFlags.HideAndDontSave };
                shadowMaterials[source] = copy;
            }
            // Full-body mode swaps materials; properties can also change while
            // playing. Copies must not freeze the avatar's current appearance.
            copy.shader = source.shader;
            copy.CopyPropertiesFromMaterial(source);
            foreach (string pass in ViewPasses) copy.SetShaderPassEnabled(pass, false);
            copy.SetShaderPassEnabled("ShadowCaster", source.GetShaderPassEnabled("ShadowCaster"));
            return copy;
        }
        private void Apply(bool value)
        {
            if (value == hidden) return;
            foreach (var part in parts)
            {
                var renderer = part.Renderer;
                if (renderer == null) continue;
                if (part.EntireHead)
                {
                    renderer.shadowCastingMode = value && part.Shadows != ShadowCastingMode.Off
                        ? ShadowCastingMode.ShadowsOnly : part.Shadows;
                    renderer.forceRenderingOff = part.ForceOff || (value && part.Shadows == ShadowCastingMode.Off);
                }
                else renderer.SetSharedMaterials(value ? part.Hidden : part.Original);
            }
            hidden = value;
        }
        private void Restore() { Apply(false); frames.Clear(); }
    }
}
