using System;
using System.Collections.Generic;
using System.Linq;

namespace MonitorSwitch
{
    public sealed class MonitorRecord
    {
        public string DevicePath { get; set; }
        public string HardwareName { get; set; }
        public string Alias { get; set; }
        public string Adapter { get; set; }
        public bool Internal { get; set; }
        public string FirstSeenUtc { get; set; }
        public string LastSeenUtc { get; set; }
        public MonitorSetting LastSetting { get; set; }
        public List<string> Resolutions { get; set; }
        public string DisplayName { get { return String.IsNullOrWhiteSpace(Alias) ? HardwareName : Alias; } }
        public MonitorRecord() { Resolutions = new List<string>(); }
    }

    internal static class MonitorHistory
    {
        public static MonitorRecord Find(Settings settings, string key)
        { return settings.Monitors.FirstOrDefault(m => String.Equals(m.DevicePath, key, StringComparison.OrdinalIgnoreCase)); }

        public static bool ImportKnown(Settings settings, IEnumerable<MonitorRecord> known)
        {
            bool changed = false;
            foreach (MonitorRecord record in known)
                if (settings.Monitors.Count < 512 && Find(settings, record.DevicePath) == null)
                { settings.Monitors.Add(record); changed = true; }
            return changed;
        }

        public static bool Observe(Settings settings, List<MonitorInfo> connected, DateTime utc, bool readModes)
        {
            bool changed = false;
            // Existing saved profiles also contribute disconnected monitors to history.
            foreach (MonitorSetting entry in settings.Profiles.SelectMany(p => p.Layout))
                if (Find(settings, entry.DevicePath) == null && settings.Monitors.Count < 512)
                {
                    settings.Monitors.Add(new MonitorRecord { DevicePath = entry.DevicePath, HardwareName = String.IsNullOrWhiteSpace(entry.Name) ? "Monitor salvato" : entry.Name, LastSetting = entry.Copy() }); changed = true;
                }
            foreach (MonitorInfo monitor in connected)
            {
                MonitorRecord record = Find(settings, monitor.Key);
                bool fresh = record == null;
                if (fresh)
                {
                    if (settings.Monitors.Count >= 512) continue;
                    record = new MonitorRecord { DevicePath = monitor.Key, HardwareName = monitor.Name };
                    settings.Monitors.Add(record); changed = true;
                }
                string now = utc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                DateTime last;
                if (String.IsNullOrEmpty(record.FirstSeenUtc)) { record.FirstSeenUtc = now; changed = true; }
                if (!DateTime.TryParse(record.LastSeenUtc, out last) || utc - last.ToUniversalTime() >= TimeSpan.FromMinutes(1))
                { record.LastSeenUtc = now; changed = true; }
                if (record.HardwareName != monitor.Name || record.Adapter != monitor.Adapter || record.LastSetting == null ||
                    record.LastSetting.Width != monitor.Baseline.Width || record.LastSetting.Height != monitor.Baseline.Height)
                { record.HardwareName = monitor.Name; record.Adapter = monitor.Adapter; record.Internal = monitor.Internal; record.LastSetting = monitor.Baseline.Copy(); changed = true; }
                if (fresh || readModes || record.Resolutions.Count == 0)
                {
                    try
                    {
                        List<string> modes = Native.Resolutions(monitor);
                        if (!record.Resolutions.SequenceEqual(modes)) { record.Resolutions = modes; changed = true; }
                    }
                    catch (Exception ex) { Recovery.Log(ex); }
                }
            }
            return changed;
        }
        public static string Name(Settings settings, string key, string fallback)
        { MonitorRecord record = Find(settings, key); return record == null ? fallback : record.DisplayName; }
    }
}
