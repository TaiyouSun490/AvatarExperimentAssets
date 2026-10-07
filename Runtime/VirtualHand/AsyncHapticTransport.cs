using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;


namespace AvatarExperiments.VirtualHand
{
    /// <summary>One worker owns serial I/O and CSV writes. Only the latest value per channel is pending.</summary>
    public sealed class AsyncHapticTransport : ITabletopHapticTransport
    {
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread worker;
        readonly Func<ITabletopHapticTransport> factory;
        readonly string logPath, logHeader;
        readonly Queue<string> logLines = new Queue<string>();
        readonly float[] frequencies = new float[16], amplitudes = new float[16];
        int dirty, prepare, cursor;
        bool muteRequested = true;
        volatile bool closing;
        volatile bool connecting = true, connected;
        volatile Exception failure;
        public bool Connecting => connecting && failure == null && !closing;
        public bool Connected => connected && failure == null && !closing;
        public int PendingChannels { get { lock (gate) { int n = 0; for (int i = 0; i < 16; i++) if ((dirty & (1 << i)) != 0) n++; return n; } } }

        public AsyncHapticTransport(Func<ITabletopHapticTransport> factory, string logPath = null, string logHeader = null)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.logPath = logPath; this.logHeader = logHeader;
            worker = new Thread(Run) { IsBackground = true, Name = "Melting ESP32 output" };
            worker.Start();
        }
        public void Prepare(int channel)
        {
            Validate(channel);
            lock (gate) { RequireOpen(); prepare |= 1 << channel; wake.Set(); }
        }
        public void Apply(int channel, float frequency, float amplitude)
        {
            Validate(channel);
            if (float.IsNaN(frequency) || float.IsInfinity(frequency) || frequency < 1 || frequency > 1000 ||
                float.IsNaN(amplitude) || float.IsInfinity(amplitude) || amplitude < 0 || amplitude > 1)
                throw new ArgumentOutOfRangeException(nameof(amplitude));
            lock (gate)
            {
                RequireOpen(); frequencies[channel] = frequency; amplitudes[channel] = amplitude;
                dirty |= 1 << channel; wake.Set();
            }
        }
        public void Mute()
        {
            lock (gate)
            {
                if (closing) return;
                // Discard every unsent vibration. At most the single in-flight write can precede this mute.
                dirty = 0; muteRequested = true; wake.Set();
            }
        }
        public void QueueLog(string line)
        {
            if (logPath == null) return;
            lock (gate)
            {
                RequireOpen();
                if (logLines.Count >= 1024) throw new IOException("Haptic log writer is not keeping up.");
                logLines.Enqueue(line); wake.Set();
            }
        }
        public void Drain()
        {
            var error = failure;
            if (error != null) throw new IOException("ESP32 output worker failed: " + error.Message, error);
        }
        void RequireOpen() { Drain(); if (closing) throw new ObjectDisposedException(nameof(AsyncHapticTransport)); }
        static void Validate(int channel) { if (channel < 0 || channel >= 16) throw new ArgumentOutOfRangeException(nameof(channel)); }

        void Run()
        {
            ITabletopHapticTransport serial = null;
            StreamWriter log = null;
            var sent = new bool[16]; var sentHz = new float[16]; var sentAmp = new float[16];
            var clock = Stopwatch.StartNew(); double nextFlush = 1;
            try
            {
                serial = factory();
                if (!serial.Connected) throw new IOException("Serial port is not open.");
                serial.Mute();
                if (logPath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    log = new StreamWriter(logPath, false); log.WriteLine(logHeader);
                }
                connected = true; connecting = false;
                while (true)
                {
                    int channel = -1; bool mute = false, initialize = false; float hz = 0, amp = 0;
                    lock (gate)
                    {
                        if (closing) break;
                        if (muteRequested) { muteRequested = false; mute = true; }
                        else if (prepare != 0)
                        {
                            for (int i = 0; i < 16; i++) if ((prepare & (1 << i)) != 0)
                            { channel = i; prepare &= ~(1 << i); initialize = true; break; }
                        }
                        else for (int i = 0; i < 16; i++)
                        {
                            int candidate = (cursor + i) % 16;
                            if ((dirty & (1 << candidate)) == 0) continue;
                            channel = candidate; dirty &= ~(1 << channel); cursor = (channel + 1) % 16;
                            hz = frequencies[channel]; amp = amplitudes[channel]; break;
                        }
                    }
                    // Never hold gate around a driver call, read, file write or flush.
                    if (!serial.Connected) throw new IOException("Serial connection lost.");
                    if (mute) { serial.Mute(); Array.Clear(sent, 0, sent.Length); }
                    else if (initialize) { serial.Prepare(channel); Array.Clear(sent, 0, sent.Length); }
                    else if (channel >= 0 && (!sent[channel] || sentHz[channel] != hz || sentAmp[channel] != amp))
                    {
                        serial.Apply(channel, hz, amp); sent[channel] = true; sentHz[channel] = hz; sentAmp[channel] = amp;
                    }
                    serial.Drain();
                    WriteLogs(log);
                    if (clock.Elapsed.TotalSeconds >= nextFlush) { log?.Flush(); nextFlush = clock.Elapsed.TotalSeconds + 1; }
                    if (!mute && channel < 0) wake.WaitOne(10);
                }
            }
            catch (Exception e) { failure = e; }
            finally
            {
                connecting = connected = false;
                try { if (serial != null && serial.Connected) serial.Mute(); } catch (Exception e) { failure = failure ?? e; }
                try { serial?.Dispose(); } catch (Exception e) { failure = failure ?? e; }
                try { WriteLogs(log); log?.Dispose(); } catch (Exception e) { failure = failure ?? e; }
                lock (gate) { closing = true; dirty = prepare = 0; wake.Dispose(); }
            }
        }
        void WriteLogs(StreamWriter log)
        {
            if (log == null) return;
            for (int i = 0; i < 1024; i++)
            {
                string line;
                lock (gate) { if (logLines.Count == 0) return; line = logLines.Dequeue(); }
                log.WriteLine(line);
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (!closing) { closing = true; dirty = prepare = 0; wake.Set(); }
            }
            // Only explicit disconnect / domain reload waits for the final mute, never the frame update.
            if (Thread.CurrentThread != worker && !worker.Join(2500))
                throw new TimeoutException("ESP32 worker is still closing; final mute has not been confirmed.");
            Drain();
        }
    }
}
