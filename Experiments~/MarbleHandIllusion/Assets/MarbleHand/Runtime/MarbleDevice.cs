using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace MarbleHand
{
    public interface IMarbleDevice : IDisposable
    {
        void Start(int channel, float hz, float amplitude);
        void Mute();
        void Query();
        IEnumerable<string> Read();
    }

    // The upstream Unity package is installed locally; its source is not redistributed here.
    public sealed class Esp32Device : IMarbleDevice
    {
        readonly object client;
        readonly Type type;
        public static Type ClientType
        {
            get
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = assembly.GetType("Esp32VibrationController.VibrationControllerClient");
                    if (t != null) return t;
                }
                return null;
            }
        }
        public Esp32Device(string port)
        {
            type = ClientType ?? throw new InvalidOperationException("Import ESP32HapticController Unity Runtime first.");
            client = Activator.CreateInstance(type);
            try
            {
                type.GetProperty("ChannelCount").SetValue(client, 16);
                type.GetProperty("AmpIcCount").SetValue(client, 8);
                Call("Connect", port, 921600); Mute();
            }
            catch { Call("Dispose"); throw; }
        }
        object Call(string name, params object[] args)
        {
            try { return type.GetMethod(name).Invoke(client, args); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }
        object Value(string method, int index, string name) => Enum.Parse(type.GetMethod(method).GetParameters()[index].ParameterType, name);
        void RequireConnection()
        { if (!(bool)type.GetProperty("IsConnected").GetValue(client)) throw new InvalidOperationException("Serial port closed."); }
        public void Start(int channel, float hz, float amplitude)
        {
            RequireConnection(); Mute();
            Call("StopAmplitudeModulation", channel, 0f);
            Call("SetWaveform", channel, Value("SetWaveform", 1, "Sine"));
            Call("SetFrequency", channel, hz); Call("SetAmplitude", channel, amplitude);
            Call("SetSoftMute", channel, false);
        }
        public void Mute() { RequireConnection(); Call("SetGroup", Value("SetGroup", 0, "MuteAll")); }
        public void Query() { RequireConnection(); Call("Query"); }
        public IEnumerable<string> Read()
        { foreach (var line in (IEnumerable)Call("DrainMessages")) yield return (string)line; }
        public void Dispose() { try { Mute(); } finally { Call("Dispose"); } }
    }

    public sealed class SimulatedDevice : IMarbleDevice
    {
        public void Start(int channel, float hz, float amplitude) { }
        public void Mute() { }
        public void Query() { }
        public IEnumerable<string> Read() { yield return "595 state=simulation"; }
        public void Dispose() { }
    }

}
