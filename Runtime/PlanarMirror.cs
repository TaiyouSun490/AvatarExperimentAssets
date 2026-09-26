using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AvatarExperiments
{
    /// <summary>Per-eye planar reflection using the actual URP materials.
    /// Standalone reflection renderer; no third-party meshes or frame assets.</summary>
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    public sealed class PlanarMirror : MonoBehaviour
    {
        [Range(.25f, 1)] public float ResolutionScale = .5f;
        [Range(256, 2048)] public int MaximumResolution = 1024;
        [Min(.001f)] public float ClipOffset = .008f;
        public LayerMask ReflectionMask = ~0;
        public bool RenderShadows = true;
        public bool PreviewInSceneView;
        public const int MirrorLayer = 31;
        public RenderTexture LeftTexture => leftTexture;
        public RenderTexture RightTexture => rightTexture;
        public int RenderCount { get; private set; }
        public bool LastStereo { get; private set; }
        public Matrix4x4 LeftViewProjection { get; private set; }
        public Matrix4x4 RightViewProjection { get; private set; }

        private static bool renderingReflection;
        private Camera leftCamera, rightCamera;
        private RenderTexture leftTexture, rightTexture;
        private Renderer surface;
        private MaterialPropertyBlock properties;
        private readonly Plane[] frustum = new Plane[6];
        private readonly UniversalRenderPipeline.SingleCameraRequest request = new();
        private static readonly int LeftId = Shader.PropertyToID("_MirrorLeft");
        private static readonly int RightId = Shader.PropertyToID("_MirrorRight");
        private static readonly int LeftVpId = Shader.PropertyToID("_MirrorLeftVP");
        private static readonly int RightVpId = Shader.PropertyToID("_MirrorRightVP");
        private static readonly int ReadyId = Shader.PropertyToID("_MirrorReady");

        private void OnEnable()
        {
            surface = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            if(surface != null && properties != null)
            { surface.GetPropertyBlock(properties); properties.SetFloat(ReadyId, 0); surface.SetPropertyBlock(properties); }
            ReleaseTexture(ref leftTexture); ReleaseTexture(ref rightTexture);
            ReleaseCamera(ref leftCamera); ReleaseCamera(ref rightCamera);
        }

        private void BeginCamera(ScriptableRenderContext context, Camera source)
        {
            if(renderingReflection || source == null || surface == null || !surface.enabled ||
               source.cameraType == CameraType.Reflection || source.cameraType == CameraType.Preview ||
               (source.cameraType == CameraType.SceneView && !PreviewInSceneView) ||
               (source.cullingMask & (1 << gameObject.layer)) == 0) return;
            if(source.TryGetComponent<UniversalAdditionalCameraData>(out var sourceData) &&
               sourceData.renderType == CameraRenderType.Overlay) return;
            // One-sided mirror. No cost from cameras looking away or from behind it.
            if(Vector3.Dot(-transform.forward, source.transform.position - transform.position) <= .01f) return;
            GeometryUtility.CalculateFrustumPlanes(source, frustum);
            if(!GeometryUtility.TestPlanesAABB(frustum, surface.bounds)) return;

            bool stereo = source.stereoEnabled;
            int width = source.pixelWidth, height = source.pixelHeight;
            if(stereo)
            { width = UnityEngine.XR.XRSettings.eyeTextureWidth; height = UnityEngine.XR.XRSettings.eyeTextureHeight; }
            float scale = Mathf.Clamp(ResolutionScale, .25f, 1);
            scale = Mathf.Min(scale, Mathf.Clamp(MaximumResolution, 256, 2048) / (float)Mathf.Max(width, height, 1));
            width = Mathf.Max(128, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(128, Mathf.RoundToInt(height * scale));
            EnsureTexture(ref leftTexture, width, height, "Liquid Mirror Left");
            if(stereo) EnsureTexture(ref rightTexture, width, height, "Liquid Mirror Right");
            else ReleaseTexture(ref rightTexture);
            if(leftCamera == null) leftCamera = CreateCamera("Liquid Planar Reflection Left");
            if(stereo && rightCamera == null) rightCamera = CreateCamera("Liquid Planar Reflection Right");

            renderingReflection = true;
            try
            {
                LeftViewProjection = RenderEye(source, leftCamera, leftTexture, Camera.StereoscopicEye.Left, stereo);
                RightViewProjection = stereo
                    ? RenderEye(source, rightCamera, rightTexture, Camera.StereoscopicEye.Right, true)
                    : LeftViewProjection;
                surface.GetPropertyBlock(properties);
                properties.SetTexture(LeftId, leftTexture);
                properties.SetTexture(RightId, stereo ? rightTexture : leftTexture);
                properties.SetMatrix(LeftVpId, LeftViewProjection);
                properties.SetMatrix(RightVpId, RightViewProjection);
                properties.SetFloat(ReadyId, 1);
                surface.SetPropertyBlock(properties);
                LastStereo = stereo; RenderCount++;
            }
            finally { renderingReflection = false; }
        }

        private Matrix4x4 RenderEye(Camera source, Camera target, RenderTexture texture,
            Camera.StereoscopicEye eye, bool stereo)
        {
            target.CopyFrom(source);
            target.enabled = false;
            target.cameraType = CameraType.Reflection;
            target.targetTexture = texture;
            target.rect = new Rect(0, 0, 1, 1);
            target.cullingMask = source.cullingMask & ReflectionMask.value & ~(1 << MirrorLayer);
            target.useOcclusionCulling = false;
            target.allowMSAA = false;
            // Clear every time, even if the source uses a camera stack/depth-only clear.
            target.clearFlags = CameraClearFlags.SolidColor;
            target.backgroundColor = new Color(.025f, .045f, .05f, 1);
            var data = target.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderShadows = RenderShadows;
            data.renderPostProcessing = false;
            data.requiresDepthOption = CameraOverrideOption.On;
            data.requiresColorOption = CameraOverrideOption.On;

            Matrix4x4 view = stereo ? source.GetStereoViewMatrix(eye) : source.worldToCameraMatrix;
            Matrix4x4 projection = stereo ? source.GetStereoProjectionMatrix(eye) : source.projectionMatrix;
            Vector3 normal = -transform.forward.normalized;
            Matrix4x4 reflection = ReflectionMatrix(transform.position, normal);
            Matrix4x4 reflectedView = view * reflection;
            Vector3 eyePosition = view.inverse.MultiplyPoint(Vector3.zero);
            target.transform.SetPositionAndRotation(reflection.MultiplyPoint(eyePosition),
                Quaternion.LookRotation(reflection.MultiplyVector(source.transform.forward),
                    reflection.MultiplyVector(source.transform.up)));
            target.worldToCameraMatrix = reflectedView;
            target.projectionMatrix = projection;
            Vector3 point = reflectedView.MultiplyPoint(transform.position + normal * Mathf.Max(.001f, ClipOffset));
            Vector3 cameraNormal = reflectedView.MultiplyVector(normal).normalized;
            target.projectionMatrix = target.CalculateObliqueMatrix(new Vector4(
                cameraNormal.x, cameraNormal.y, cameraNormal.z, -Vector3.Dot(point, cameraNormal)));
            target.cullingMatrix = target.projectionMatrix * reflectedView;
            Matrix4x4 result = GL.GetGPUProjectionMatrix(target.projectionMatrix, true) * reflectedView;
            bool oldInvert = GL.invertCulling;
            try
            {
                GL.invertCulling = !oldInvert;
                request.destination = texture;
                RenderPipeline.SubmitRenderRequest(target, request);
            }
            finally { GL.invertCulling = oldInvert; target.ResetCullingMatrix(); }
            return result;
        }

        public static Matrix4x4 ReflectionMatrix(Vector3 point, Vector3 normal)
        {
            normal.Normalize();
            var plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point));
            var result = Matrix4x4.identity;
            for(int r = 0; r < 3; r++)
                for(int c = 0; c < 4; c++) result[r,c] -= 2 * plane[r] * plane[c];
            return result;
        }
        private static Camera CreateCamera(string label)
        {
            var go = new GameObject(label) { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            return camera;
        }
        private static void EnsureTexture(ref RenderTexture texture, int width, int height, string label)
        {
            if(texture != null && texture.width == width && texture.height == height) return;
            ReleaseTexture(ref texture);
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            { name = label, antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear,
              wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
        }
        private static void ReleaseTexture(ref RenderTexture texture)
        { if(texture == null) return; texture.Release(); CoreUtils.Destroy(texture); texture = null; }
        private static void ReleaseCamera(ref Camera camera)
        { if(camera == null) return; CoreUtils.Destroy(camera.gameObject); camera = null; }
    }
}
