using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DisappearingHand
{
    public sealed class TrialLog : IDisposable
    {
        StreamWriter writer;
        public string Path { get; private set; }
        public string Error { get; private set; }
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public bool Open(TrialSettings settings, bool preview)
        {
            Dispose(); Error = null;
            try
            {
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Trials");
                Directory.CreateDirectory(dir);
                Path = System.IO.Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + Guid.NewGuid().ToString("N").Substring(0,8) + ".csv");
                writer = new StreamWriter(Path, false, new System.Text.UTF8Encoding(false));
                writer.WriteLine("# schema=1; units=metres; coordinates=Unity_world; utc=" + DateTime.UtcNow.ToString("O"));
                writer.WriteLine("# settings=" + JsonUtility.ToJson(settings) + "; desktop_preview=" + preview);
                writer.WriteLine("event,time_s,phase,shift_m,left_valid,right_valid,left_source,right_source,left_raw_x,left_raw_y,left_raw_z,right_raw_x,right_raw_y,right_raw_z,left_visual_x,left_visual_y,left_visual_z,right_visual_x,right_visual_y,right_visual_z,search_tip_x,search_tip_y,search_tip_z,left_visible,right_visible");
                writer.Flush(); return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { Error = e.Message; Dispose(); Debug.LogWarning("Trial logging unavailable: " + Error); return false; }
        }
        public void Write(string reason, TrialProtocol p, Vector3 left, Vector3 right, Vector3 leftVisual, Vector3 rightVisual,
            Vector3 searchTip, bool leftValid, bool rightValid, string leftSource, string rightSource)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine(string.Join(",", reason, F(p.Elapsed), p.Phase, F(p.Shift), leftValid?"1":"0", rightValid?"1":"0",
                    leftSource, rightSource, V(left), V(right), V(leftVisual), V(rightVisual), V(searchTip), p.IsVisible(true)?"1":"0", p.IsVisible(false)?"1":"0"));
                if (reason != "sample") writer.Flush();
            }
            catch (IOException e) { Error = e.Message; Dispose(); Debug.LogWarning("Trial log write failed: " + Error); }
        }
        static string F(float v) => v.ToString("F6", Invariant);
        static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
        public void Dispose()
        {
            try { writer?.Dispose(); } catch (IOException e) { Error = e.Message; }
            writer = null;
        }
    }
}
