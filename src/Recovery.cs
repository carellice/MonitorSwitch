using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MonitorSwitch
{
    internal static class Recovery
    {
        public static readonly string FileName = Path.Combine(ProfileStore.Folder, "pending-display.bin");
        public static void Locked(Action action)
        {
            using (var mutex = new Mutex(false, @"Local\MonitorSwitch.DisplayOperation"))
            {
                bool taken = false;
                try
                {
                    try { taken = mutex.WaitOne(10000); } catch (AbandonedMutexException) { taken = true; }
                    if (!taken) throw new TimeoutException("Un cambio monitor e gia in corso."); action();
                }
                finally { if (taken) mutex.ReleaseMutex(); }
            }
        }
        public static void Restore()
        {
            Locked(delegate { if (File.Exists(FileName)) { Native.Apply(Snapshot.Load(FileName), true); File.Delete(FileName); } });
        }
        public static void Commit(DisplayState state)
        {
            Locked(delegate
            {
                if (!File.Exists(FileName)) throw new InvalidOperationException("Tempo di conferma scaduto. Riprova.");
                Native.Apply(state, true); File.Delete(FileName);
            });
        }
        public static void Log(Exception ex)
        {
            try { Directory.CreateDirectory(ProfileStore.Folder); File.AppendAllText(Path.Combine(ProfileStore.Folder, "errors.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
        }
        // No resident guardian after confirmation: confirmed monitor changes persist.
        internal static bool NeedsRecovery(WaitHandle stop, WaitHandle parent, WaitHandle armed, int armTimeout, int confirmTimeout)
        {
            int first = WaitHandle.WaitAny(new WaitHandle[] { stop, parent, armed }, armTimeout);
            if (first == 0) return false; if (first != 2) return true;
            return WaitHandle.WaitAny(new WaitHandle[] { stop, parent }, confirmTimeout) != 0;
        }
        public static int Guard(int pid, string token)
        {
            try
            {
                using (Process parent = Process.GetProcessById(pid))
                using (var ended = new EventWaitHandle(false, EventResetMode.ManualReset))
                using (var ready = EventWaitHandle.OpenExisting(token + ".ready"))
                using (var armed = EventWaitHandle.OpenExisting(token + ".arm"))
                using (var stop = EventWaitHandle.OpenExisting(token + ".stop"))
                {
                    ended.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(parent.Handle, false);
                    ready.Set(); if (!NeedsRecovery(stop, ended, armed, 10000, 30000)) return 0;
                }
                Restore(); return 0;
            }
            catch (Exception ex) { Log(ex); try { Restore(); } catch (Exception restoreError) { Log(restoreError); } return 1; }
        }
    }
    internal sealed class Guardian : IDisposable
    {
        private readonly string token = @"Local\MonitorSwitch." + Guid.NewGuid().ToString("N");
        private EventWaitHandle ready, arm, stop; private Process child;
        public Guardian()
        {
            try
            {
                ready = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".ready");
                arm = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".arm");
                stop = new EventWaitHandle(false, EventResetMode.ManualReset, token + ".stop");
                var info = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--guard " + Process.GetCurrentProcess().Id + " " + token)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                child = Process.Start(info);
                if (!ready.WaitOne(5000) || child.HasExited) throw new InvalidOperationException("Protezione del cambio monitor non disponibile.");
            }
            catch { Dispose(); throw; }
        }
        public void Arm() { arm.Set(); }
        public bool HasExited { get { return child == null || child.HasExited; } }
        public void Dispose()
        {
            if (stop != null) stop.Set();
            if (child != null) { child.WaitForExit(2000); child.Dispose(); child = null; }
            if (ready != null) { ready.Dispose(); ready = null; } if (arm != null) { arm.Dispose(); arm = null; } if (stop != null) { stop.Dispose(); stop = null; }
        }
    }
}
