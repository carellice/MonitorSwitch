using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace MonitorSwitch
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Num, Den; }
    [StructLayout(LayoutKind.Sequential)] internal struct Position { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct DesktopImageInfo
    { public Position SourceSize; public DisplayRect Region, Clip; }
    [StructLayout(LayoutKind.Sequential)] public struct Region { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] internal struct SourceMode { public uint Width, Height, Format; public Position Position; }
    [StructLayout(LayoutKind.Sequential)] public struct Signal
    { public ulong PixelRate; public Rational HSync, VSync; public Region Active, Total; public uint Standard, Scanline; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode
    {
        [FieldOffset(0)] public uint Type; [FieldOffset(4)] public uint Id; [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public SourceMode Source; [FieldOffset(16)] public Signal Target;
        [FieldOffset(16)] public DesktopImageInfo DesktopImage;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct PathTarget
    { public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint Scanline; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayPath { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetName
    {
        public uint Type, Size; public Luid Adapter; public uint Id, Flags, Technology;
        public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
    {
        public uint Type, Size; public Luid Adapter; public uint Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 220)] internal struct DeviceMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(168)] public uint Bits;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
        [FieldOffset(184)] public uint Frequency;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PreferredMode
    { public uint Type, Size; public Luid Adapter; public uint Id, Width, Height; public Signal Timing; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    internal sealed class DisplayState
    {
        public DisplayPath[] Paths; public Mode[] Modes;
        public void Check()
        {
            if (Paths == null || Modes == null || Paths.Length < 1 || Paths.Length > 32 || Modes.Length < 1 || Modes.Length > 128)
                throw new InvalidDataException("Configurazione video non valida.");
            foreach (DisplayPath p in Paths)
            {
                uint sourceIndex = Native.SourceIndex(p), targetIndex = Native.TargetIndex(p), imageIndex = Native.ImageIndex(p);
                if (sourceIndex >= Modes.Length || targetIndex >= Modes.Length)
                    throw new InvalidDataException("Configurazione video incompleta.");
                Mode s = Modes[sourceIndex], t = Modes[targetIndex];
                if (s.Type != 1 || t.Type != 2 || s.Id != p.Source.Id || t.Id != p.Target.Id ||
                    Native.AdapterKey(s.Adapter) != Native.AdapterKey(p.Source.Adapter) || Native.AdapterKey(t.Adapter) != Native.AdapterKey(p.Target.Adapter) ||
                    s.Source.Width == 0 || s.Source.Height == 0 || t.Target.Active.Width == 0 || t.Target.Active.Height == 0)
                    throw new InvalidDataException("Dati video incoerenti.");
                if (imageIndex != UInt32.MaxValue && (imageIndex >= Modes.Length || Modes[imageIndex].Type != 3 ||
                    Modes[imageIndex].Id != p.Target.Id || Native.AdapterKey(Modes[imageIndex].Adapter) != Native.AdapterKey(p.Target.Adapter)))
                    throw new InvalidDataException("Immagine desktop virtuale non valida.");
            }
        }
        public List<MonitorSetting> Capture(List<MonitorInfo> monitors)
        {
            Check(); var result = new List<MonitorSetting>(); var groups = new Dictionary<string, int>();
            foreach (DisplayPath p in Paths)
            {
                MonitorInfo monitor = monitors.FirstOrDefault(m => m.Candidates.Any(c => Native.TargetKey(c) == Native.TargetKey(p)));
                if (monitor == null || String.IsNullOrEmpty(monitor.Key)) throw new InvalidOperationException("Impossibile identificare un monitor. Aggiorna l'elenco e riprova.");
                string source = Native.SourceKey(p); if (!groups.ContainsKey(source)) groups.Add(source, groups.Count);
                SourceMode s = Modes[Native.SourceIndex(p)].Source; Signal t = Modes[Native.TargetIndex(p)].Target;
                uint imageIndex = Native.ImageIndex(p);
                result.Add(new MonitorSetting { DevicePath = monitor.Key, Name = monitor.Name, Group = groups[source],
                    Width = s.Width, Height = s.Height, X = s.Position.X, Y = s.Position.Y, Rotation = p.Target.Rotation,
                    Scaling = p.Target.Scaling, RefreshNum = t.VSync.Num, RefreshDen = t.VSync.Den, Timing = t,
                    DesktopImage = imageIndex == UInt32.MaxValue ? (DesktopImageInfo?)null : Modes[imageIndex].DesktopImage });
            }
            return result;
        }
        public int Topology()
        {
            if (Paths.All(p => Native.IsInternal(p.Target.Technology))) return 1;
            if (Paths.All(p => !Native.IsInternal(p.Target.Technology))) return Paths.Length == 1 ? 8 : (Cloned() ? 2 : 4);
            return Cloned() ? 2 : 4;
        }
        private bool Cloned()
        {
            if (Paths.Length < 2) return false;
            SourceMode first = Modes[Native.SourceIndex(Paths[0])].Source;
            return Paths.All(p => { SourceMode s = Modes[Native.SourceIndex(p)].Source; return s.Width == first.Width && s.Height == first.Height && s.Position.X == first.Position.X && s.Position.Y == first.Position.Y; });
        }
    }

    internal sealed class MonitorInfo
    {
        public string Key, Name, Adapter; public bool Internal, Active;
        public uint Width, Height; public double Hz;
        public uint PreferredWidth, PreferredHeight;
        public Signal PreferredTiming;
        public readonly List<DisplayPath> Candidates = new List<DisplayPath>();
        public MonitorSetting Baseline;
        public string Summary { get { return (Internal ? "Portatile" : "Esterno") + "  ·  " + (Active ? Width + " × " + Height + "  ·  " + Hz.ToString("0.##") + " Hz" : "Collegato, non attivo"); } }
    }
    internal sealed class DisplayPlan { public DisplayPath[] Paths; public Mode[] Modes; }
    internal static class Native
    {
        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pc, out uint mc);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pc, [Out] DisplayPath[] paths, ref uint mc, [Out] Mode[] modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint pc, [In] DisplayPath[] paths, uint mc, [In] Mode[] modes, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetTargetName(ref TargetName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetSourceName(ref SourceName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int GetPreferredMode(ref PreferredMode mode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsEx(string name, int index, ref DeviceMode mode, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
        public static string AdapterKey(Luid id) { return id.High + ":" + id.Low; }
        public static string SourceKey(DisplayPath p) { return AdapterKey(p.Source.Adapter) + ":" + p.Source.Id; }
        public static string TargetKey(DisplayPath p) { return AdapterKey(p.Target.Adapter) + ":" + p.Target.Id; }
        public static bool VirtualMode(DisplayPath p) { return (p.Flags & 8) != 0; }
        // With virtual-mode awareness, source/target indexes occupy the upper
        // 16 bits; clone-group/desktop-image indexes occupy the lower 16 bits.
        public static uint SourceIndex(DisplayPath p) { return VirtualMode(p) ? (p.Source.ModeIndex >> 16) == 0xffff ? UInt32.MaxValue : p.Source.ModeIndex >> 16 : p.Source.ModeIndex; }
        public static uint TargetIndex(DisplayPath p) { return VirtualMode(p) ? (p.Target.ModeIndex >> 16) == 0xffff ? UInt32.MaxValue : p.Target.ModeIndex >> 16 : p.Target.ModeIndex; }
        public static uint ImageIndex(DisplayPath p) { return VirtualMode(p) && (p.Target.ModeIndex & 0xffff) != 0xffff ? p.Target.ModeIndex & 0xffff : UInt32.MaxValue; }
        internal static uint AwarenessFlags(DisplayPath[] paths) { return paths.Any(VirtualMode) ? 0x8000u : 0; }
        public static bool IsInternal(uint technology) { return technology == 0x80000000 || technology == 6 || technology == 11 || technology == 13; }
        public static void Check(int code, string action)
        { if (code != 0) throw new InvalidOperationException(action + ": " + new Win32Exception(code).Message + " (" + code + ")."); }
        private static DisplayState Query(uint flags)
        {
            for (int i = 0; i < 5; i++)
            {
                uint pc, mc; Check(GetDisplayConfigBufferSizes(flags, out pc, out mc), "Lettura monitor");
                if (pc == 0 || pc > 4096 || mc > 8192) throw new InvalidOperationException("Nessun monitor disponibile nella sessione Windows locale.");
                var paths = new DisplayPath[pc]; var modes = new Mode[mc];
                int code = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
                if (code == 122) continue; Check(code, "Lettura configurazione");
                Array.Resize(ref paths, (int)pc); Array.Resize(ref modes, (int)mc);
                return new DisplayState { Paths = paths, Modes = modes };
            }
            throw new InvalidOperationException("I monitor stanno cambiando. Attendi un momento e riprova.");
        }
        public static DisplayState Read() { DisplayState state = Query(2 | 0x10); state.Check(); return state; }
        public static List<MonitorInfo> Inventory(DisplayState active)
        {
            DisplayState all = Query(1 | 0x10); var found = new Dictionary<string, MonitorInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (DisplayPath p in all.Paths.OrderByDescending(p => (p.Flags & 1) != 0))
            {
                if (p.Target.Available == 0) continue;
                var name = new TargetName { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = p.Target.Adapter, Id = p.Target.Id };
                if (GetTargetName(ref name) != 0 || String.IsNullOrWhiteSpace(name.DevicePath)) continue;
                MonitorInfo monitor;
                if (!found.TryGetValue(name.DevicePath, out monitor))
                {
                    monitor = new MonitorInfo { Key = name.DevicePath, Name = String.IsNullOrWhiteSpace(name.Name) ? (IsInternal(p.Target.Technology) ? "Schermo del portatile" : "Monitor esterno") : name.Name,
                        Internal = IsInternal(p.Target.Technology), Adapter = AdapterName(p) };
                    found.Add(name.DevicePath, monitor);
                }
                monitor.Candidates.Add(p);
                foreach (DisplayPath ap in active.Paths.Where(a => TargetKey(a) == TargetKey(p)))
                {
                    SourceMode s = active.Modes[SourceIndex(ap)].Source; Signal t = active.Modes[TargetIndex(ap)].Target;
                    monitor.Active = true; monitor.Width = s.Width; monitor.Height = s.Height;
                    monitor.Hz = t.VSync.Den == 0 ? 0 : (double)t.VSync.Num / t.VSync.Den;
                }
            }
            var result = found.Values.OrderByDescending(m => m.Internal).ThenBy(m => m.Name).ToList();
            foreach (MonitorInfo monitor in result)
            {
                DisplayPath path = monitor.Candidates[0];
                var preferred = new PreferredMode { Type = 3, Size = (uint)Marshal.SizeOf(typeof(PreferredMode)), Adapter = path.Target.Adapter, Id = path.Target.Id };
                bool known = GetPreferredMode(ref preferred) == 0;
                monitor.PreferredWidth = known ? preferred.Width : 0; monitor.PreferredHeight = known ? preferred.Height : 0;
                monitor.PreferredTiming = known ? preferred.Timing : new Signal();
                monitor.Baseline = new MonitorSetting { DevicePath = monitor.Key, Name = monitor.Name, Group = result.IndexOf(monitor),
                    Width = known ? preferred.Width : 1920, Height = known ? preferred.Height : 1080, Rotation = 1, Scaling = 4,
                    RefreshNum = known && preferred.Timing.VSync.Num > 0 ? preferred.Timing.VSync.Num : 60,
                    RefreshDen = known && preferred.Timing.VSync.Den > 0 ? preferred.Timing.VSync.Den : 1,
                    Timing = known ? preferred.Timing : new Signal() };
            }
            foreach (MonitorSetting setting in active.Capture(result))
            {
                MonitorInfo monitor = result.First(m => String.Equals(m.Key, setting.DevicePath, StringComparison.OrdinalIgnoreCase));
                monitor.Baseline = setting;
            }
            return result;
        }
        public static List<string> Resolutions(MonitorInfo monitor)
        {
            var found = new Dictionary<string, ulong>();
            foreach (DisplayPath route in monitor.Candidates.Take(1))
            {
                var source = new SourceName { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = route.Source.Adapter, Id = route.Source.Id };
                if (GetSourceName(ref source) != 0) continue;
                for (int i = 0; i < 8192; i++)
                {
                    var mode = new DeviceMode { Size = 220 };
                    if (!EnumDisplaySettingsEx(source.Name, i, ref mode, 0)) break;
                    if (mode.Bits < 32 || mode.Width == 0 || mode.Height == 0) continue;
                    // An inactive target can share the GDI source currently bound to
                    // a different monitor. Keep suggestions within its physical size;
                    // custom dimensions can still be entered and driver-validated.
                    if (monitor.PreferredWidth > 0 && monitor.PreferredHeight > 0 &&
                        (mode.Width > monitor.PreferredWidth || mode.Height > monitor.PreferredHeight)) continue;
                    found[mode.Width + " x " + mode.Height] = (ulong)mode.Width * mode.Height;
                }
            }
            if (monitor.Baseline != null) found[monitor.Baseline.Width + " x " + monitor.Baseline.Height] = (ulong)monitor.Baseline.Width * monitor.Baseline.Height;
            if (monitor.PreferredWidth != 0 && monitor.PreferredHeight != 0) found[monitor.PreferredWidth + " x " + monitor.PreferredHeight] = (ulong)monitor.PreferredWidth * monitor.PreferredHeight;
            return found.OrderByDescending(p => p.Value).Select(p => p.Key).ToList();
        }
        private static string AdapterName(DisplayPath p)
        {
            var source = new SourceName { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = p.Source.Adapter, Id = p.Source.Id };
            if (GetSourceName(ref source) != 0) return "GPU non disponibile";
            for (uint i = 0; i < 32; i++)
            {
                var device = new DisplayDevice { Size = (uint)Marshal.SizeOf(typeof(DisplayDevice)) };
                if (!EnumDisplayDevices(null, i, ref device, 0)) break;
                if (device.Name == source.Name) return device.Description;
            }
            return source.Name;
        }
        public static void ApplyTopology(int topology)
        {
            if (topology != 1 && topology != 2 && topology != 4 && topology != 8) throw new ArgumentOutOfRangeException("topology");
            Check(SetDisplayConfig(0, null, 0, null, (uint)topology | 0x80), "Cambio modalita schermi");
        }
        public static void Apply(DisplayState state, bool persist)
        { state.Check(); Check(SetDisplayConfig((uint)state.Paths.Length, state.Paths, (uint)state.Modes.Length, state.Modes, 0x20u | 0x80u | AwarenessFlags(state.Paths) | (persist ? 0x200u : 0)), "Ripristino configurazione"); }
        public static void Validate(DisplayState state)
        { state.Check(); Check(SetDisplayConfig((uint)state.Paths.Length, state.Paths, (uint)state.Modes.Length, state.Modes, 0x20u | 0x40u | AwarenessFlags(state.Paths)), "Verifica configurazione nativa"); }
        internal static uint PlanFlags(bool validate)
        { return 0x20u | (validate ? 0x40u : 0x80u | 0x100u); }
        public static void Plan(DisplayPlan plan, bool validate)
        { Check(SetDisplayConfig((uint)plan.Paths.Length, plan.Paths, (uint)plan.Modes.Length, plan.Modes, PlanFlags(validate) | AwarenessFlags(plan.Paths)), validate ? "Verifica profilo" : "Applicazione profilo"); }

        // Bind saved physical monitors to CURRENT adapter/source identifiers. LUIDs
        // from a previous boot are deliberately never used as persistent identities.
        public static DisplayPlan BuildPlan(List<MonitorSetting> layout, List<MonitorInfo> monitors)
        {
            Profile.CheckLayout(layout);
            var devices = new Dictionary<MonitorSetting, MonitorInfo>();
            foreach (MonitorSetting setting in layout)
            {
                MonitorInfo monitor = monitors.FirstOrDefault(m => String.Equals(m.Key, setting.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (monitor == null) throw new InvalidOperationException("Monitor del profilo non collegato: " + setting.Name + ". Ricollegalo oppure modifica il profilo.");
                devices.Add(setting, monitor);
            }
            var groups = layout.GroupBy(s => s.Group).ToList();
            var options = groups.Select(g => devices[g.First()].Candidates.Select(SourceKey).Distinct()
                .Where(key => g.All(s => devices[s].Candidates.Any(p => SourceKey(p) == key))).ToList()).ToList();
            var assigned = new string[groups.Count];
            int budget = 20000;
            if (!Assign(options, assigned, new HashSet<string>(), 0, ref budget))
                throw new InvalidOperationException("Questi monitor non possono riprodurre il profilo con le connessioni attuali. Ricrea il profilo dalla configurazione Windows corrente.");
            var paths = new List<DisplayPath>(); var modes = new List<Mode>();
            for (int i = 0; i < groups.Count; i++)
            {
                MonitorSetting first = groups[i].First(); uint index = (uint)modes.Count;
                DisplayPath route = devices[first].Candidates.First(p => SourceKey(p) == assigned[i]);
                modes.Add(new Mode { Type = 1, Adapter = route.Source.Adapter, Id = route.Source.Id,
                    Source = new SourceMode { Width = first.Width, Height = first.Height, Format = 4, Position = new Position { X = first.X, Y = first.Y } } });
                foreach (MonitorSetting setting in groups[i])
                {
                    DisplayPath path = devices[setting].Candidates.First(p => SourceKey(p) == assigned[i]);
                    path.Flags = 1 | (path.Flags & 8);
                    path.Source.ModeIndex = VirtualMode(path) ? (index << 16) | 0xffffu : index;
                    path.Target.ModeIndex = UInt32.MaxValue;
                    path.Target.Rotation = setting.Rotation;
                    // Vendor custom scaling requires a private interface; use the
                    // driver's saved preference rather than submitting CUSTOM.
                    path.Target.Scaling = setting.Scaling == 5 ? 128u : setting.Scaling;
                    path.Target.Refresh = new Rational { Num = setting.RefreshNum, Den = setting.RefreshDen };
                    path.Target.Scanline = 0;
                    Signal timing = TimingFor(setting, devices[setting]);
                    if (timing.PixelRate != 0)
                    {
                        path.Target.ModeIndex = VirtualMode(path) ? ((uint)modes.Count << 16) | 0xffffu : (uint)modes.Count;
                        modes.Add(new Mode { Type = 2, Adapter = path.Target.Adapter, Id = path.Target.Id, Target = timing });
                    }
                    if (VirtualMode(path))
                    {
                        // Explicitly reset the virtual desktop surface as well as
                        // its source mode. Reusing the old desktop image can keep
                        // the ultrawide surface when a 16:9 profile is requested.
                        path.Target.ModeIndex = (path.Target.ModeIndex & 0xffff0000u) | (uint)modes.Count;
                        modes.Add(new Mode { Type = 3, Adapter = path.Target.Adapter, Id = path.Target.Id,
                            DesktopImage = new DesktopImageInfo { SourceSize = new Position { X = (int)setting.Width, Y = (int)setting.Height },
                                Region = new DisplayRect { Right = (int)setting.Width, Bottom = (int)setting.Height },
                                Clip = new DisplayRect { Right = (int)setting.Width, Bottom = (int)setting.Height } } });
                    }
                    paths.Add(path);
                }
            }
            return new DisplayPlan { Paths = paths.ToArray(), Modes = modes.ToArray() };
        }
        private static Signal TimingFor(MonitorSetting setting, MonitorInfo monitor)
        {
            if (setting.Timing.PixelRate != 0) return setting.Timing;
            var candidates = new List<Signal>();
            if (monitor.Baseline != null) candidates.Add(monitor.Baseline.Timing);
            candidates.Add(monitor.PreferredTiming);
            foreach (Signal timing in candidates)
                if (timing.PixelRate != 0 && timing.VSync.Den != 0 && Math.Abs((double)timing.VSync.Num / timing.VSync.Den - (double)setting.RefreshNum / setting.RefreshDen) <= 0.5)
                    return timing;
            return new Signal();
        }
        private static bool Assign(List<List<string>> options, string[] assigned, HashSet<string> used, int i, ref int budget)
        {
            if (i == options.Count) return true;
            if (--budget < 0) return false;
            foreach (string key in options[i])
            {
                if (!used.Add(key)) continue; assigned[i] = key;
                if (Assign(options, assigned, used, i + 1, ref budget)) return true;
                used.Remove(key);
            }
            return false;
        }
        public static bool Matches(List<MonitorSetting> expected, List<MonitorSetting> actual)
        {
            if (expected.Count != actual.Count) return false;
            foreach (MonitorSetting e in expected)
            {
                MonitorSetting a = actual.FirstOrDefault(s => String.Equals(s.DevicePath, e.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (a == null || a.Width != e.Width || a.Height != e.Height || a.X != e.X || a.Y != e.Y || a.Rotation != e.Rotation ||
                    e.RefreshDen == 0 || a.RefreshDen == 0 || Math.Abs((double)a.RefreshNum / a.RefreshDen - (double)e.RefreshNum / e.RefreshDen) > 0.5) return false;
            }
            // Group numbers can change after enumeration, but clone relationships cannot.
            foreach (MonitorSetting e in expected)
                foreach (MonitorSetting f in expected)
                {
                    var a = actual.First(s => String.Equals(s.DevicePath, e.DevicePath, StringComparison.OrdinalIgnoreCase));
                    var b = actual.First(s => String.Equals(s.DevicePath, f.DevicePath, StringComparison.OrdinalIgnoreCase));
                    if ((e.Group == f.Group) != (a.Group == b.Group)) return false;
                }
            return true;
        }
        public static bool MatchesProtected(List<MonitorSetting> expected, List<MonitorSetting> actual)
        {
            return Matches(expected, actual) && expected.Where(s => s.Scaling == 4).All(e => PreservesAspect(actual.First(a => String.Equals(a.DevicePath, e.DevicePath, StringComparison.OrdinalIgnoreCase))));
        }
        public static bool PreservesAspect(MonitorSetting setting)
        {
            uint sourceWidth = setting.Width, sourceHeight = setting.Height;
            if (setting.DesktopImage.HasValue)
            {
                DesktopImageInfo image = setting.DesktopImage.Value;
                long regionWidth = (long)image.Region.Right - image.Region.Left, regionHeight = (long)image.Region.Bottom - image.Region.Top;
                long clipWidth = (long)image.Clip.Right - image.Clip.Left, clipHeight = (long)image.Clip.Bottom - image.Clip.Top;
                if (image.SourceSize.X <= 0 || image.SourceSize.Y <= 0 || regionWidth <= 0 || regionHeight <= 0 ||
                    image.Region.Left < 0 || image.Region.Top < 0 || image.Region.Right > image.SourceSize.X || image.Region.Bottom > image.SourceSize.Y ||
                    clipWidth != setting.Width || clipHeight != setting.Height || regionWidth * clipHeight != regionHeight * clipWidth) return false;
                sourceWidth = (uint)image.SourceSize.X; sourceHeight = (uint)image.SourceSize.Y;
            }
            if (setting.Scaling == 2 || setting.Scaling == 4) return true;
            uint width = setting.Timing.Active.Width, height = setting.Timing.Active.Height;
            if (setting.Rotation == 2 || setting.Rotation == 4) { uint swap = width; width = height; height = swap; }
            return width > 0 && height > 0 && (ulong)sourceWidth * height == (ulong)sourceHeight * width;
        }
    }

    internal static class Snapshot
    {
        public static void Save(string file, DisplayState state)
        {
            state.Check(); Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (var stream = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(0x4D535732); w.Write(state.Paths.Length); w.Write(state.Modes.Length);
                foreach (DisplayPath p in state.Paths) w.Write(Pack(p)); foreach (Mode m in state.Modes) w.Write(Pack(m));
                w.Flush(); stream.Flush(true);
            }
            if (File.Exists(file)) throw new IOException("Ripristino gia in sospeso.");
            File.Move(file + ".tmp", file);
        }
        public static DisplayState Load(string file)
        {
            using (var r = new BinaryReader(File.OpenRead(file)))
            {
                if (r.ReadInt32() != 0x4D535732) throw new InvalidDataException("Backup non riconosciuto.");
                int pc = r.ReadInt32(), mc = r.ReadInt32();
                if (pc < 1 || pc > 32 || mc < 1 || mc > 128 || r.BaseStream.Length != 12L + pc * 72L + mc * 64L) throw new InvalidDataException("Backup incompleto o non valido.");
                var s = new DisplayState { Paths = new DisplayPath[pc], Modes = new Mode[mc] };
                for (int i = 0; i < pc; i++) s.Paths[i] = Unpack<DisplayPath>(r.ReadBytes(72));
                for (int i = 0; i < mc; i++) s.Modes[i] = Unpack<Mode>(r.ReadBytes(64)); s.Check(); return s;
            }
        }
        private static byte[] Pack<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T)); IntPtr ptr = Marshal.AllocHGlobal(size);
            try { byte[] bytes = new byte[size]; Marshal.Copy(bytes, 0, ptr, size); Marshal.StructureToPtr(value, ptr, false); Marshal.Copy(ptr, bytes, 0, size); return bytes; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        private static T Unpack<T>(byte[] bytes) where T : struct
        {
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            try { Marshal.Copy(bytes, 0, ptr, bytes.Length); return (T)Marshal.PtrToStructure(ptr, typeof(T)); }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }
}
