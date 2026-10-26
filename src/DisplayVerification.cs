using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MonitorSwitch
{
    internal sealed class DisplayObservation
    {
        public DisplayState State;
        public List<MonitorInfo> Monitors;
        public List<MonitorSetting> Layout;
    }
    internal static class DisplayVerification
    {
        public static DisplayObservation Read()
        {
            DisplayState state = Native.Read(); var monitors = Native.Inventory(state);
            return new DisplayObservation { State = state, Monitors = monitors, Layout = state.Capture(monitors) };
        }
        public static DisplayObservation WaitFor(List<MonitorSetting> expected)
        { return WaitFor(expected, Read, delegate { Thread.Sleep(200); }, 20); }

        internal static DisplayObservation WaitFor(List<MonitorSetting> expected, Func<DisplayObservation> read, Action pause, int maxReads)
        {
            DisplayObservation latest = null; Exception error = null; int stable = 0;
            for (int i = 0; i < maxReads; i++)
            {
                try
                {
                    latest = read(); error = null;
                    if (Native.MatchesProtected(expected, latest.Layout)) { if (++stable >= 2) return latest; }
                    else stable = 0;
                }
                catch (InvalidOperationException ex) { error = ex; stable = 0; }
                catch (InvalidDataException ex) { error = ex; stable = 0; }
                if (i + 1 < maxReads) pause();
            }
            string detail = error != null ? error.Message : latest == null ? "Nessuna configurazione leggibile." : Difference(expected, latest.Layout);
            throw new InvalidOperationException("Il driver non ha mantenuto il profilo richiesto. La configurazione precedente verra ripristinata.\r\n\r\n" + detail, error);
        }
        internal static string Difference(List<MonitorSetting> expected, List<MonitorSetting> actual)
        {
            var details = new List<string>();
            foreach (MonitorSetting wanted in expected)
            {
                MonitorSetting received = actual.FirstOrDefault(s => String.Equals(s.DevicePath, wanted.DevicePath, StringComparison.OrdinalIgnoreCase));
                string name = String.IsNullOrWhiteSpace(wanted.Name) ? "Monitor" : wanted.Name;
                if (received == null) details.Add(name + ": non attivo.");
                else if (!Native.MatchesProtected(new List<MonitorSetting> { wanted }, new List<MonitorSetting> { received }))
                    details.Add(name + ": richiesto " + Describe(wanted) + "; ricevuto " + Describe(received) +
                        (wanted.Scaling == 4 && !Native.PreservesAspect(received) ? ". Proporzioni non mantenute dal driver." : "."));
            }
            if (expected.Count != actual.Count) details.Add("Monitor attivi richiesti: " + expected.Count + "; rilevati: " + actual.Count + ".");
            if (details.Count == 0) details.Add("La relazione tra monitor duplicati ed estesi e diversa da quella richiesta.");
            return String.Join("\r\n", details.Take(4));
        }
        private static string Describe(MonitorSetting setting)
        {
            return setting.Width + " x " + setting.Height + " a " +
                (setting.RefreshDen == 0 ? "?" : ((double)setting.RefreshNum / setting.RefreshDen).ToString("0.##")) + " Hz, posizione " + setting.X + "," + setting.Y +
                ", rotazione " + setting.Rotation + ", scaling " + setting.Scaling +
                (setting.Timing.PixelRate == 0 ? "" : ", segnale " + setting.Timing.Active.Width + " x " + setting.Timing.Active.Height) +
                (!setting.DesktopImage.HasValue ? "" : ", immagine virtuale " +
                    (setting.DesktopImage.Value.Region.Right - setting.DesktopImage.Value.Region.Left) + " x " +
                    (setting.DesktopImage.Value.Region.Bottom - setting.DesktopImage.Value.Region.Top));
        }
    }
}
