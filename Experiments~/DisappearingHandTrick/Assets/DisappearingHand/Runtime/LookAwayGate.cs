using UnityEngine;

namespace DisappearingHand
{
    // HMD field-of-view proxy, not eye gaze or an inference about attention.
    public struct HandViewObservation
    {
        public bool valid, bothOutside, bothVisible, candidateOutside;
    }

    public sealed class LookAwayGate
    {
        public bool Armed { get; private set; }
        public bool Returned { get; private set; }
        public float AwaySeconds { get; private set; }
        float visibleSeconds;
        public void Reset() { Armed = Returned = false; AwaySeconds = visibleSeconds = 0; }
        public bool Tick(float dt, HandViewObservation view, TrialSettings settings)
        {
            if (!view.valid || !Finite(dt) || dt <= 0 || dt > .5f) { Reset(); return false; }
            if (view.bothVisible)
            {
                AwaySeconds = 0;
                visibleSeconds += dt;
                Returned = visibleSeconds >= settings.lookBackDwellSeconds;
                if (Returned) Armed = true;
                return false;
            }
            visibleSeconds = 0; Returned = false;
            if (!view.bothOutside || !view.candidateOutside) { AwaySeconds = 0; return false; }
            if (!Armed) return false;
            AwaySeconds += dt;
            if (AwaySeconds < settings.lookAwayDwellSeconds) return false;
            Armed = false; AwaySeconds = 0; return true;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    public sealed class HandViewDetector
    {
        readonly Plane[] leftPlanes = new Plane[6], rightPlanes = new Plane[6];
        public HandViewObservation Observe(Camera camera, Vector3 left, Vector3 right, Vector3 nextLeft, Vector3 nextRight, float radius)
        {
            if (!camera || !Finite(left) || !Finite(right) || !Finite(nextLeft) || !Finite(nextRight)) return default;
            bool stereo = camera.stereoEnabled;
            Matrix4x4 first = stereo
                ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left)
                : camera.projectionMatrix * camera.worldToCameraMatrix;
            Matrix4x4 second = stereo
                ? camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right) : first;
            if (!Finite(first) || !Finite(second)) return default;
            GeometryUtility.CalculateFrustumPlanes(first, leftPlanes);
            GeometryUtility.CalculateFrustumPlanes(second, rightPlanes);
            return Evaluate(leftPlanes, rightPlanes, left, right, nextLeft, nextRight, radius);
        }
        public static HandViewObservation Evaluate(Plane[] first, Plane[] second, Vector3 left, Vector3 right, Vector3 nextLeft, Vector3 nextRight, float radius)
        {
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius) || !Valid(first) || !Valid(second) ||
                !Finite(left) || !Finite(right) || !Finite(nextLeft) || !Finite(nextRight)) return default;
            bool leftSeen = InView(first,second,left,radius), rightSeen = InView(first,second,right,radius);
            return new HandViewObservation {
                valid = true, bothOutside = !leftSeen && !rightSeen,
                // Both wrist centres must be visible to re-arm; peripheral intersection alone does not re-arm.
                bothVisible = InView(first,second,left,0) && InView(first,second,right,0),
                candidateOutside = !InView(first,second,nextLeft,radius) && !InView(first,second,nextRight,radius)
            };
        }
        static bool InView(Plane[] a, Plane[] b, Vector3 point, float radius)
        {
            var bounds = new Bounds(point, Vector3.one * (2 * radius));
            return GeometryUtility.TestPlanesAABB(a,bounds) || GeometryUtility.TestPlanesAABB(b,bounds);
        }
        static bool Valid(Plane[] planes)
        {
            if (planes == null || planes.Length != 6) return false;
            foreach (var p in planes) if (!Finite(p.normal) || p.normal.sqrMagnitude < .5f || float.IsNaN(p.distance) || float.IsInfinity(p.distance)) return false;
            return true;
        }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));
        static bool Finite(Matrix4x4 m) { for(int i=0;i<16;i++) if(float.IsNaN(m[i])||float.IsInfinity(m[i])) return false; return true; }
    }
}
