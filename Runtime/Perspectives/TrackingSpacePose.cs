using System;
using UnityEngine;

namespace AvatarExperiments.Perspectives
{
    /// <summary>Convert a source pose to a separate body/display frame without applying observer offset twice.</summary>
    public static class TrackingSpacePose
    {
        public static Pose MapWorld(Pose sourceWorldPose, Transform sourceTrackingOrigin, Transform destinationOrigin)
        {
            if (!sourceTrackingOrigin || !destinationOrigin) throw new ArgumentNullException("Explicit source and destination frames required.");
            if (!PerspectiveCatalog.Finite(sourceWorldPose.position) || !ValidRotation(sourceWorldPose.rotation))
                throw new ArgumentException("Pose must be finite with a unit quaternion.");
            RequireRigid(sourceTrackingOrigin);
            RequireRigid(destinationOrigin);
            Vector3 localPosition = sourceTrackingOrigin.InverseTransformPoint(sourceWorldPose.position);
            Quaternion localRotation = Quaternion.Inverse(sourceTrackingOrigin.rotation) * sourceWorldPose.rotation;
            return new Pose(destinationOrigin.TransformPoint(localPosition), destinationOrigin.rotation * localRotation);
        }

        public static Pose FromLocal(Pose trackingLocalPose, Transform destinationOrigin)
        {
            if (!destinationOrigin) throw new ArgumentNullException(nameof(destinationOrigin));
            RequireRigid(destinationOrigin);
            if (!PerspectiveCatalog.Finite(trackingLocalPose.position) || !ValidRotation(trackingLocalPose.rotation))
                throw new ArgumentException("Pose must be finite with a unit quaternion.");
            return new Pose(destinationOrigin.TransformPoint(trackingLocalPose.position), destinationOrigin.rotation * trackingLocalPose.rotation);
        }

        private static bool ValidRotation(Quaternion q) => PerspectiveCatalog.Finite(q.x) && PerspectiveCatalog.Finite(q.y) &&
            PerspectiveCatalog.Finite(q.z) && PerspectiveCatalog.Finite(q.w) && Mathf.Abs(Quaternion.Dot(q, q) - 1) < 0.001f;
        private static void RequireRigid(Transform frame)
        {
            if ((frame.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new ArgumentException("Tracking frames must have unit world scale.");
        }
    }
}
