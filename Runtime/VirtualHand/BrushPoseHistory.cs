using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    // Timestamped positions; interpolation never invents data across tracking gaps.
    public sealed class BrushPoseHistory
    {
        readonly double[] times = new double[4096];
        readonly Vector3[] positions = new Vector3[4096];
        int first, count;
        public void Clear() { first = count = 0; }
        public void Add(double time, Vector3 position)
        {
            if(!double.IsFinite(time)||!float.IsFinite(position.sqrMagnitude))return;
            if (count > 0 && time <= times[(first + count - 1) % times.Length]) return;
            if (count == times.Length) { first = (first + 1) % times.Length; count--; }
            int index = (first + count++) % times.Length;
            times[index] = time; positions[index] = position;
        }
        public bool TrySample(double time, double maximumGap, out Vector3 position)
        {
            position = default;
            if (!double.IsFinite(time)||!double.IsFinite(maximumGap)||maximumGap<0||count == 0 || time < times[first]) return false;
            int last = (first + count - 1) % times.Length;
            if (time > times[last] + .000001) return false;
            for (int i = count - 1; i >= 0; i--)
            {
                int a = (first + i) % times.Length;
                if (times[a] > time) continue;
                if (Math.Abs(time - times[a]) < .000001) { position = positions[a]; return true; }
                int b = (a + 1) % times.Length;
                if (times[b] - times[a] > maximumGap) return false;
                position = Vector3.LerpUnclamped(positions[a], positions[b], (float)((time - times[a]) / (times[b] - times[a])));
                return true;
            }
            return false;
        }
    }

    public static class BrushCalibration
    {
        // Three matching surface points define a rigid map. Distances are never scaled.
        public static bool TryMap(Vector3[] real, Vector3[] visual, out Quaternion rotation, out Vector3 translation)
        {
            rotation = Quaternion.identity; translation = default;
            if (!Frame(real, out var a) || !Frame(visual, out var b)) return false;
            rotation = b * Quaternion.Inverse(a);
            translation = visual[0] - rotation * real[0];
            return true;
        }
        static bool Frame(Vector3[] points, out Quaternion frame)
        {
            frame = Quaternion.identity;
            if (points == null || points.Length != 3) return false;
            foreach(var point in points)if(!float.IsFinite(point.sqrMagnitude))return false;
            Vector3 forward = points[1] - points[0], side = points[2] - points[0];
            if (forward.magnitude < .02f || side.magnitude < .02f) return false;
            Vector3 up = Vector3.Cross(forward.normalized, side.normalized);
            if (up.magnitude < .35f) return false;
            frame = Quaternion.LookRotation(forward, up.normalized);
            return true;
        }
        // Pivot calibration: hold the physical brush tip still and rotate its controller.
        public static bool TryPivot(IReadOnlyList<Pose> poses, out Vector3 localTip, out float residual)
        {
            localTip = default; residual = float.PositiveInfinity;
            if (poses == null || poses.Count < 30) return false;
            foreach(var pose in poses)
                if(!float.IsFinite(pose.position.sqrMagnitude)||!float.IsFinite(Quaternion.Dot(pose.rotation,pose.rotation)))return false;
            var matrix = new double[3, 4];
            Matrix4x4 reference = Matrix4x4.Rotate(poses[0].rotation);
            for (int i = 1; i < poses.Count; i++)
            {
                var rotation = Matrix4x4.Rotate(poses[i].rotation);
                Vector3 difference = poses[0].position - poses[i].position;
                for (int row = 0; row < 3; row++)
                    for (int a = 0; a < 3; a++)
                    {
                        double value = rotation[row, a] - reference[row, a];
                        matrix[a, 3] += value * difference[row];
                        for (int b = 0; b < 3; b++) matrix[a, b] += value * (rotation[row, b] - reference[row, b]);
                    }
            }
            for (int column = 0; column < 3; column++)
            {
                int pivot = column;
                for (int row = column + 1; row < 3; row++) if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
                if (Math.Abs(matrix[pivot, column]) < .01 * poses.Count) return false;
                for (int k = column; k < 4; k++) { double temp = matrix[column, k]; matrix[column, k] = matrix[pivot, k]; matrix[pivot, k] = temp; }
                double divisor = matrix[column, column];
                for (int k = column; k < 4; k++) matrix[column, k] /= divisor;
                for (int row = 0; row < 3; row++) if (row != column)
                {
                    double factor = matrix[row, column];
                    for (int k = column; k < 4; k++) matrix[row, k] -= factor * matrix[column, k];
                }
            }
            localTip = new Vector3((float)matrix[0, 3], (float)matrix[1, 3], (float)matrix[2, 3]);
            if (!float.IsFinite(localTip.sqrMagnitude) || localTip.magnitude > .6f) return false;
            Vector3 mean = Vector3.zero;
            foreach (var pose in poses) mean += pose.position + pose.rotation * localTip;
            mean /= poses.Count;
            float error = 0;
            foreach (var pose in poses) error += (pose.position + pose.rotation * localTip - mean).sqrMagnitude;
            residual = Mathf.Sqrt(error / poses.Count);
            return residual <= .004f;
        }
    }
}
