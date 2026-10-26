using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace TendonHand
{
    public interface ITendonDevice : IDisposable
    {
        void Start(int channel, float hz, float amplitude);
        void Mute();
        void Query();
        IEnumerable<string> Read();
    }

    // The upstream Unity package is installed locally; its source is not redistributed here.
    public sealed class Esp32Device : ITendonDevice
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

    public sealed class SimulatedDevice : ITendonDevice
    {
        public void Start(int channel, float hz, float amplitude) { }
        public void Mute() { }
        public void Query() { }
        public IEnumerable<string> Read() { yield return "595 state=simulation"; }
        public void Dispose() { }
    }

    public struct OutputEvent
    {
        public string name, detail;
        public double time;
        public int commandId;
        public OutputEvent(string name, string detail, int commandId) { this.name = name; this.detail = detail; this.commandId = commandId; time = TendonOutput.Now; }
    }

    /// <summary>Only this worker touches serial. Stop cancels queued starts. Host heartbeat bounds output during a stalled frame.</summary>
    public sealed class TendonOutput : IDisposable
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ConcurrentQueue<OutputEvent> events = new ConcurrentQueue<OutputEvent>();
        readonly Thread thread;
        readonly Func<ITendonDevice> factory;
        TendonSettings pending;
        int sequence, startId, stopId;
        bool stop = true, closing;
        double heartbeat;
        volatile bool ready, active;
        volatile string failure;
        public bool Ready => ready && failure == null;
        public bool Active => active;
        public string Failure => failure;
        public TendonOutput(Func<ITendonDevice> factory)
        {
            this.factory = factory;
            heartbeat = Now;
            thread = new Thread(Run) { IsBackground = true, Name = "Tendon hand serial output" };
            thread.Start();
        }
        public void Heartbeat() { lock (gate) heartbeat = Now; }
        public int Start(TendonSettings settings)
        {
            lock (gate)
            {
                if (!Ready || closing) throw new InvalidOperationException("Output is not ready.");
                pending = settings.Snapshot(); startId = ++sequence; wake.Set(); return startId;
            }
        }
        public int Stop()
        {
            lock (gate) { pending = null; stop = true; stopId = ++sequence; if (!closing) wake.Set(); return stopId; }
        }
        public bool TryRead(out OutputEvent item) => events.TryDequeue(out item);
        void Emit(string name, string detail = "", int commandId = 0) => events.Enqueue(new OutputEvent(name, detail, commandId));
        void Run()
        {
            ITendonDevice device = null;
            try
            {
                device = factory(); device.Mute();
                double connectedAt = Now, lastReply = Now, nextQuery = 0, deadline = double.PositiveInfinity;
                while (true)
                {
                    TendonSettings request = null; bool mute; double lastHeartbeat; int commandId;
                    lock (gate)
                    {
                        if (closing) break;
                        mute = stop; stop = false; lastHeartbeat = heartbeat;
                        commandId = mute ? stopId : startId;
                        if (!mute) { request = pending; pending = null; }
                    }
                    if (active && (Now - lastHeartbeat > .75 || Now > deadline))
                        throw new TimeoutException("Host heartbeat or stimulation duration expired.");
                    if (mute) { device.Mute(); active = false; Emit("stop_write_complete", "", commandId); }
                    if (request != null)
                    {
                        device.Start(request.channel, request.frequencyHz, request.amplitude);
                        active = true; deadline = Now + request.inductionSeconds + 1;
                        Emit("start_write_complete", "", commandId);
                    }
                    if (Now >= nextQuery) { device.Query(); nextQuery = Now + .5; }
                    foreach (string line in device.Read())
                    {
                        if (line.StartsWith("595 state="))
                        {
                            lastReply = Now;
                            if (!ready) { ready = true; Emit("connected", line); }
                        }
                        if (line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || line.StartsWith("ERR"))
                            throw new InvalidOperationException(line);
                    }
                    if ((!ready && Now - connectedAt > 5) || (ready && Now - lastReply > 2))
                        throw new TimeoutException("No ESP32 state reply.");
                    wake.WaitOne(5);
                }
            }
            catch (Exception e) { failure = e.Message; Emit("output_fault", e.Message); }
            finally
            {
                ready = false;
                try { if (device != null) { device.Mute(); Emit("final_stop_write_complete"); } }
                catch (Exception e) { failure = e.Message; Emit("stop_write_failed", e.Message); }
                active = false;
                try { device?.Dispose(); } catch (Exception e) { failure = e.Message; }
            }
        }
        public void Dispose()
        {
            lock (gate) { if (closing) return; closing = true; pending = null; wake.Set(); }
            if (thread.Join(3000)) wake.Dispose();
            else throw new TimeoutException("Serial worker has not closed; stop delivery is unconfirmed.");
        }
    }
}
