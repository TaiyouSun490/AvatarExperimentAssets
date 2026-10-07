using System;
using System.Collections;
using System.Reflection;

namespace AvatarExperiments.VirtualHand
{
    public interface ITabletopHapticTransport : IDisposable
    {
        bool Connected { get; }
        void Prepare(int channel);
        void Apply(int channel, float frequency, float amplitude);
        void Mute();
        void Drain();
    }

    /// <summary>Optional bridge to the separately installed ESP32HapticController Unity client.</summary>
    public sealed class Esp32HapticTransport : ITabletopHapticTransport
    {
        readonly object client;
        readonly Type type;
        readonly PropertyInfo connected;
        readonly MethodInfo connect, mute, prepare, waveform, frequency, amplitude, softMute, isSoftMuted, drain, dispose;
        readonly object muteAll, sine;
        public bool Connected => client != null && (bool)connected.GetValue(client);
        public static bool Available => FindClient() != null;
        static Type FindClient()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var found = assembly.GetType("Esp32VibrationController.VibrationControllerClient", false);
                if (found != null) return found;
            }
            return null;
        }
        public Esp32HapticTransport(string port)
        {
            type = FindClient() ?? throw new InvalidOperationException("Install the Unity Esp32VibrationController Runtime folder from TaiyouSun490/ESP32HapticController, or use Vibration=None.");
            connected = type.GetProperty("IsConnected") ?? throw new MissingMemberException(type.FullName, "IsConnected");
            MethodInfo Method(string name) => type.GetMethod(name) ?? throw new MissingMethodException(type.FullName, name);
            connect = Method("Connect"); mute = Method("SetGroup"); prepare = Method("StopAmplitudeModulation");
            waveform = Method("SetWaveform"); frequency = Method("SetFrequency"); amplitude = Method("SetAmplitude");
            softMute = Method("SetSoftMute"); isSoftMuted = Method("IsSoftMuted"); drain = Method("DrainMessages"); dispose = Method("Dispose");
            muteAll = Enum.Parse(mute.GetParameters()[0].ParameterType, "MuteAll");
            sine = Enum.Parse(waveform.GetParameters()[1].ParameterType, "Sine");
            client = Activator.CreateInstance(type);
            try
            {
                type.GetProperty("ChannelCount").SetValue(client, 16); type.GetProperty("AmpIcCount").SetValue(client, 8);
                Call(connect, port, 921600); Mute();
            }
            catch { Call(dispose); throw; }
        }
        object Call(MethodInfo method, params object[] args)
        {
            try { return method.Invoke(client, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        void RequireConnection() { if (!Connected) throw new InvalidOperationException("ESP32 serial port is closed."); }
        public void Prepare(int channel) { RequireConnection(); Mute(); Call(prepare, channel, 0f); Call(waveform, channel, sine); }
        public void Apply(int channel, float hz, float amp)
        {
            RequireConnection(); Call(frequency, channel, hz); Call(amplitude, channel, amp);
            if ((bool)Call(isSoftMuted, channel)) Call(softMute, channel, false);
        }
        public void Mute() { RequireConnection(); Call(mute, muteAll); }
        public void Drain() { foreach (var ignored in (IEnumerable)Call(drain)) { } }
        public void Dispose() { try { if (Connected) Mute(); } finally { Call(dispose); } }
    }
}
