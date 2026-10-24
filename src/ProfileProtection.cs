using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorSwitch
{
    internal enum ProtectionState { Off, Watching, MissingMonitor, Conflict }

    // The native action is injected so mode conflicts, reconnects and retry limits
    // can be tested without changing the developer's display configuration.
    internal sealed class ProfileProtection
    {
        private readonly Queue<DateTime> attempts = new Queue<DateTime>();
        public ProtectionState State { get; private set; }
        public string Detail { get; private set; }
        public void Reset() { attempts.Clear(); State = ProtectionState.Off; Detail = null; }
        public bool Tick(Profile profile, List<string> connected, List<MonitorSetting> actual, DateTime utc, Func<List<MonitorSetting>> restore)
        {
            if (profile == null || !profile.HasLayout || !profile.ProtectResolution) { State = ProtectionState.Off; return false; }
            if (State == ProtectionState.Conflict) return false;
            if (profile.Layout.Any(s => !connected.Contains(s.DevicePath, StringComparer.OrdinalIgnoreCase)))
            { State = ProtectionState.MissingMonitor; Detail = "In attesa dei monitor del profilo"; return false; }
            if (Native.MatchesProtected(profile.Layout, actual)) { State = ProtectionState.Watching; Detail = null; return false; }
            while (attempts.Count > 0 && utc - attempts.Peek() > TimeSpan.FromSeconds(60)) attempts.Dequeue();
            if (attempts.Count >= 3)
            { State = ProtectionState.Conflict; Detail = "Cambi ripetuti: usa il gioco in borderless e riapplica il profilo"; return false; }
            attempts.Enqueue(utc);
            try
            {
                if (!Native.MatchesProtected(profile.Layout, restore())) throw new InvalidOperationException("Il driver non mantiene risoluzione e proporzioni richieste.");
                State = ProtectionState.Watching; Detail = null; return true;
            }
            catch (Exception ex) { State = ProtectionState.Conflict; Detail = ex.Message; return false; }
        }
    }
}
