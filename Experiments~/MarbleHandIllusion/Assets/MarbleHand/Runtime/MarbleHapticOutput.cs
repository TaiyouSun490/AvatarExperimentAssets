using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace MarbleHand
{
    public readonly struct HapticEvent
    {
        public readonly string name, detail;
        public readonly int impact;
        public readonly double time;
        public HapticEvent(string name, int impact = 0, string detail = "")
        { this.name = name; this.impact = impact; this.detail = detail; time = MarbleHapticOutput.Now; }
    }

    /// <summary>Finite pulses and cancellation are serviced independently of Unity frames.</summary>
    public sealed class MarbleHapticOutput : IDisposable
    {
        public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ConcurrentQueue<HapticEvent> events = new ConcurrentQueue<HapticEvent>();
        readonly Thread worker;
        readonly Func<IMarbleDevice> factory;
        MarbleSettings pending;
        double due, heartbeat;
        int pendingId;
        bool cancel = true, closing;
        volatile bool ready, active;
        volatile string failure;
        public bool Ready => ready && failure == null;
        public bool Active => active;
        public string Failure => failure;
        public MarbleHapticOutput(Func<IMarbleDevice> factory)
        {
            this.factory = factory; heartbeat = Now;
            worker = new Thread(Run) { IsBackground = true, Name = "Marble contact pulses" };
            worker.Start();
        }
        public void Heartbeat() { lock (gate) heartbeat = Now; }
        public void Schedule(int impact, double at, MarbleSettings settings)
        {
            var copy = settings.Snapshot();
            lock (gate)
            {
                if (!Ready || closing || pending != null) throw new InvalidOperationException("Pulse output unavailable or queue occupied.");
                if (double.IsNaN(at) || double.IsInfinity(at) || at < Now - .05 || at > Now + 1)
                    throw new ArgumentException("Pulse deadline out of range.");
                pending = copy; due = at; pendingId = impact; wake.Set();
            }
        }
        public void Stop()
        { lock (gate) { pending = null; cancel = true; if (!closing) wake.Set(); } }
        public bool TryRead(out HapticEvent e) => events.TryDequeue(out e);
        void Emit(string name, int id = 0, string detail = "") => events.Enqueue(new HapticEvent(name, id, detail));
        void Run()
        {
            IMarbleDevice device = null;
            try
            {
                device = factory(); device.Mute();
                double connected = Now, lastReply = Now, nextQuery = 0, deadline = double.PositiveInfinity;
                int current = 0;
                while (true)
                {
                    lock (gate)
                    {
                        if (closing) break;
                        if (cancel)
                        { device.Mute(); active = false; deadline = double.PositiveInfinity; cancel = false; Emit("mute_written", current); }
                        if ((active || pending != null) && Now - heartbeat > .5)
                            throw new TimeoutException("Unity heartbeat expired.");
                        if (active && Now >= deadline)
                        { device.Mute(); active = false; Emit("pulse_off_written", current); }
                        if (pending != null && Now >= due)
                        {
                            var s = pending; pending = null; current = pendingId;
                            if (Now - due > .05) throw new TimeoutException("Missed pulse deadline.");
                            device.Start(s.channel, s.frequency, s.amplitude);
                            deadline = Now + s.pulseSeconds; active = true; Emit("pulse_on_written", current);
                        }
                    }
                    if (Now >= nextQuery) { device.Query(); nextQuery = Now + .5; }
                    foreach (var line in device.Read())
                    {
                        if (!line.StartsWith("595 state=", StringComparison.Ordinal)) continue;
                        lastReply = Now;
                        if (!ready) { ready = true; Emit("device_ready", 0, line); }
                    }
                    if ((!ready && Now - connected > 5) || (ready && Now - lastReply > 2))
                        throw new TimeoutException("ESP32 state reply timeout.");
                    wake.WaitOne(2);
                }
            }
            catch (Exception e) { failure = e.Message; Emit("output_fault", 0, e.Message); }
            finally
            {
                ready = false; active = false;
                try { device?.Mute(); Emit("output_closed"); } catch (Exception e) { Emit("mute_failed", 0, e.Message); }
                try { device?.Dispose(); } catch (Exception e) { Emit("close_failed", 0, e.Message); }
            }
        }
        public void Dispose()
        {
            lock (gate) { pending = null; closing = true; wake.Set(); }
            if (worker.Join(1000)) wake.Dispose();
        }
    }
}
