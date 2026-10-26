using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TendonHand
{
    public sealed class TendonLog : IDisposable
    {
        StreamWriter writer;
        double start;
        public string Path { get; private set; }
        public bool IsOpen => writer != null;
        public void Open(TendonSettings settings, bool desktop, bool dryRun, string port, Pose baseline, Vector3 pivot, Vector3 axis)
        {
            Dispose(); start = TendonOutput.Now;
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "TendonTrials");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
            writer = new StreamWriter(Path, false, new System.Text.UTF8Encoding(false));
            writer.WriteLine("# schema=1; metres/degrees; Unity_world; utc=" + DateTime.UtcNow.ToString("O"));
            writer.WriteLine("# settings=" + JsonUtility.ToJson(settings));
            writer.WriteLine("# desktop=" + desktop + "; dry_run=" + dryRun + "; port=" + port + "; pc_monotonic_start_s=" + F(start));
            writer.WriteLine("# pivot=" + V(pivot) + "; axis=" + V(axis) + "; baseline=" + V(baseline.position));
            writer.WriteLine("# write_complete=PC serial write returned; device_apply_time and physical vibration timing are NOT measured");
            writer.WriteLine("event,pc_monotonic_s,trial_wall_s,phase,phase_s,visual_angle_deg,target_visible,tracking_valid,source,raw_x,raw_y,raw_z,raw_qx,raw_qy,raw_qz,raw_qw,visual_x,visual_y,visual_z,visual_qx,visual_qy,visual_qz,visual_qw,output_write_active,reported_angle_deg,detail");
            writer.Flush();
        }
        public void Write(string name, TendonProtocol p, Pose raw, Pose visual, bool valid, string source,
            bool output, string detail = "", float? reportedAngle = null, double? timestamp = null)
        {
            if (writer == null) return;
            double now = timestamp ?? TendonOutput.Now;
            writer.WriteLine(string.Join(",", Escape(name), F(now), F(now-start), p.Phase, F(p.PhaseTime), F(p.Angle),
                p.TargetVisible ? "1" : "0", valid ? "1" : "0", Escape(source), V(raw.position), Q(raw.rotation),
                V(visual.position), Q(visual.rotation), output ? "1" : "0", reportedAngle.HasValue ? F(reportedAngle.Value) : "", Escape(detail)));
            if (name != "sample") writer.Flush();
        }
        static string Escape(string s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        static string F(double x) => x.ToString("F6", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);
        public void Dispose() { var old = writer; writer = null; old?.Dispose(); }
    }
}
