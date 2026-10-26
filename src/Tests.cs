using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonitorSwitch
{
    internal static class Tests
    {
        private static int count;
        private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; }
        private static void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (InvalidDataException) { rejected = true; } catch (System.Xml.XmlException) { rejected = true; }
            Check(rejected, label);
        }
        private static DisplayPath Route(uint source, uint target)
        { return new DisplayPath { Source = new PathSource { Id = source, ModeIndex = 0 }, Target = new PathTarget { Id = target, ModeIndex = 1, Rotation = 1, Scaling = 1, Available = 1, Technology = 5 }, Flags = 1 }; }
        private static MonitorInfo Monitor(string key, uint target, params uint[] sources)
        {
            var m = new MonitorInfo { Key = key, Name = key, Adapter = "Test GPU" };
            foreach (uint source in sources) m.Candidates.Add(Route(source, target)); return m;
        }
        private static MonitorSetting Setting(string key, int group, int x)
        { return new MonitorSetting { DevicePath = key, Name = key, Group = group, Width = 1920, Height = 1080, X = x, Y = 0, Rotation = 1, Scaling = 4, RefreshNum = 120, RefreshDen = 1 }; }
        private static DisplayState StateFixture()
        {
            return new DisplayState { Paths = new DisplayPath[] { Route(0, 0) }, Modes = new Mode[] {
                new Mode { Type = 1, Source = new SourceMode { Width = 3440, Height = 1440, Format = 4 } },
                new Mode { Type = 2, Target = new Signal { Active = new Region { Width = 3440, Height = 1440 }, VSync = new Rational { Num = 120, Den = 1 }, PixelRate = 600000000 } } } };
        }
        private static Settings Fixture()
        {
            var s = Settings.Defaults();
            for (int i = 0; i < 4; i++) s.Profiles.Add(new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Profilo " + i, Mode = 0,
                Hotkey = new Shortcut { Modifiers = 3, Key = (uint)(112 + i) }, Layout = new List<MonitorSetting> { Setting("screen-" + i, 0, 0) } });
            return s;
        }
        public static int Run(string report)
        {
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "monitor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Check(Marshal.SizeOf(typeof(DisplayPath)) == 72, "Path ABI"); Check(Marshal.SizeOf(typeof(Mode)) == 64, "Mode ABI");
                Check(Marshal.SizeOf(typeof(Signal)) == 48, "Signal ABI"); Check(Marshal.SizeOf(typeof(TargetName)) == 420, "Target identity ABI");
                Check(Marshal.SizeOf(typeof(DesktopImageInfo)) == 40, "Virtual desktop image ABI");
                Check(Marshal.SizeOf(typeof(SourceName)) == 84, "Source ABI"); Check(Marshal.SizeOf(typeof(DisplayDevice)) == 840, "GPU name ABI");
                Check(Marshal.SizeOf(typeof(DeviceMode)) == 220, "Resolution DEVMODEW ABI");
                Check(Marshal.SizeOf(typeof(KnownMonitors.InterfaceData)) == 32 && Marshal.SizeOf(typeof(KnownMonitors.DeviceData)) == 32, "SetupAPI x64 monitor interface ABI");
                Check(Marshal.SizeOf(typeof(PreferredMode)) == 80, "Preferred target timing ABI");
                Check(AppAssets.TrayIcon.Width == 16 && AppAssets.Icon.Width == 48, "Embedded multi-size application and tray icons");
                using (var window = new MainForm(true))
                {
                    window.ShowInTaskbar = false; window.StartPosition = System.Windows.Forms.FormStartPosition.Manual; window.Location = new System.Drawing.Point(-30000, -30000);
                    window.Show(); System.Windows.Forms.Application.DoEvents();
                    Check(window.Visible, "Preview window visible before background action");
                    window.StayInTray(); System.Windows.Forms.Application.DoEvents();
                    Check(!window.Visible && window.WindowState == System.Windows.Forms.FormWindowState.Minimized, "Background switch leaves main window hidden in tray");
                }
                using (var window = new MainForm(true, true))
                {
                    window.StartPosition = System.Windows.Forms.FormStartPosition.Manual; window.Location = new System.Drawing.Point(-30000, -30000);
                    bool shown = false; window.Shown += delegate { shown = true; };
                    window.Show(); System.Windows.Forms.Application.DoEvents();
                    Check(window.IsHandleCreated && !window.Visible && !shown && window.WindowState == System.Windows.Forms.FormWindowState.Minimized, "Tray startup creates handle without showing the main window");
                    window.Show(); window.WindowState = System.Windows.Forms.FormWindowState.Normal;
                    System.Windows.Forms.Application.DoEvents();
                    Check(window.Visible && shown, "Tray startup window can be opened afterwards");
                }
                string startupKey = @"Software\MonitorSwitch.Tests-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(startupKey))
                    {
                        string executable = Path.Combine(folder, "Cartella con spazi", "MonitorSwitch.exe");
                        Check(!Startup.IsEnabled(key, executable), "Windows startup is opt-in");
                        key.SetValue("OtherApp", "untouched");
                        Startup.SetEnabled(key, executable, true);
                        Check(Startup.IsEnabled(key, executable) && (string)key.GetValue("MonitorSwitch") == "\"" + executable + "\" --tray", "Startup registration quotes executable paths and requests tray mode");
                        Check(!Startup.IsEnabled(key, Path.Combine(folder, "Moved.exe")), "Startup registration detects a moved portable executable");
                        Startup.SetEnabled(key, executable, false); Startup.SetEnabled(key, executable, false);
                        Check(!Startup.IsEnabled(key, executable) && (string)key.GetValue("OtherApp") == "untouched", "Disabling startup is idempotent and preserves other entries");
                    }
                }
                finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(startupKey, false); }
                uint width, height; LayoutEditor.Resolution(" 2560 × 1440 ", out width, out height);
                Check(width == 2560 && height == 1440, "Parse resolution with multiplication sign");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("1920", out w, out h); }, "Reject incomplete resolution");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("0 x 1080", out w, out h); }, "Reject zero resolution");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("999999999999 x 1080", out w, out h); }, "Reject overflowing resolution");
                Settings empty = Settings.Defaults(); empty.Check();
                Check(empty.Version == 2 && empty.Profiles.Count == 0 && empty.Monitors.Count == 0, "New archive starts with history and no invented profiles");
                Settings defaults = Fixture(); defaults.Check();
                Check(defaults.ConfirmChanges, "Display confirmation enabled by default");
                Check(defaults.Profiles.All(p => p.HasLayout && p.ProtectResolution), "New profiles have explicit layouts and protection by default");
                string file = Path.Combine(folder, "profiles.xml"); ProfileStore.Save(file, defaults);
                Settings loaded = ProfileStore.Load(file); Check(loaded.Profiles[0].Id == defaults.Profiles[0].Id && loaded.Version == 2, "Settings round trip");
                loaded.Profiles[0].Name = "Gioco & relax à"; ProfileStore.Save(file, loaded);
                Check(ProfileStore.Load(file).Profiles[0].Name == "Gioco & relax à", "Atomic replacement and Unicode XML");
                loaded.ConfirmChanges = false; ProfileStore.Save(file, loaded);
                Check(!ProfileStore.Load(file).ConfirmChanges, "One-click switch preference persists");
                var duplicate = ProfileStore.Load(file); duplicate.Profiles[1].Hotkey = duplicate.Profiles[0].Hotkey.Clone();
                Reject(delegate { duplicate.Check(); }, "Reject duplicate profile hotkey");
                duplicate = ProfileStore.Load(file); duplicate.Profiles[1].Name = duplicate.Profiles[0].Name;
                Reject(delegate { duplicate.Check(); }, "Reject duplicate names");
                duplicate = ProfileStore.Load(file); duplicate.ActiveProfileId = "missing";
                Reject(delegate { duplicate.Check(); }, "Reject missing active profile");
                duplicate = ProfileStore.Load(file); duplicate.Profiles[0].Mode = 6;
                Reject(delegate { duplicate.Check(); }, "Reject invalid topology flags");
                Reject(delegate { new Shortcut { Key = 65 }.Check(); }, "Reject unmodified global letter");
                Reject(delegate { new Shortcut { Key = 112, Modifiers = 8 }.Check(); }, "Reject unsupported modifier");
                var single = Fixture(); single.Profiles.RemoveRange(1, 3); single.Check(); count++;
                single.Profiles.Clear(); single.Check(); count++;
                File.WriteAllText(file, "<!DOCTYPE Settings [<!ENTITY bad SYSTEM 'file:///not-read'>]><Settings>&bad;</Settings>");
                Reject(delegate { ProfileStore.Load(file); }, "DTD and external entity rejected");
                var a = Monitor("panel", 7, 0, 1); var b = Monitor("external", 9, 0, 1);
                var inventory = new List<MonitorInfo> { a, b };
                var extend = new List<MonitorSetting> { Setting("panel", 0, 0), Setting("external", 1, 1920) };
                DisplayPlan plan = Native.BuildPlan(extend, inventory);
                Check(plan.Paths.Length == 2 && plan.Modes.Length == 2, "Extended profile plan");
                Check(Native.SourceKey(plan.Paths[0]) != Native.SourceKey(plan.Paths[1]), "Extended outputs have distinct sources");
                Check(plan.Modes[1].Source.Position.X == 1920, "Extended placement preserved");
                Check(plan.Paths.All(p => p.Target.ModeIndex == UInt32.MaxValue), "Driver solves target timing");
                var clone = new List<MonitorSetting> { Setting("panel", 0, 0), Setting("external", 0, 0) };
                plan = Native.BuildPlan(clone, inventory);
                Check(plan.Modes.Length == 1 && plan.Paths[0].Source.ModeIndex == plan.Paths[1].Source.ModeIndex, "Cloned outputs share source mode");
                Check(Native.SourceKey(plan.Paths[0]) == Native.SourceKey(plan.Paths[1]), "Cloned source assignment");
                plan = Native.BuildPlan(new List<MonitorSetting> { Setting("external", 0, 0) }, inventory);
                Check(plan.Paths.Length == 1 && plan.Paths[0].Target.Id == 9, "Saved external-only target");
                Reject(delegate { Native.BuildPlan(extend, new List<MonitorInfo> { a }); }, "Missing physical monitor rejected before changes");
                Reject(delegate { Native.BuildPlan(clone, new List<MonitorInfo> { Monitor("panel", 7, 0), Monitor("external", 9, 1) }); }, "Impossible clone route rejected");
                plan = Native.BuildPlan(extend, new List<MonitorInfo> { Monitor("panel", 7, 0, 1), Monitor("external", 9, 0) });
                Check(plan.Paths[0].Source.Id == 1 && plan.Paths[1].Source.Id == 0, "Source selection backtracks instead of stealing required source");
                var reconnected = new List<MonitorInfo> { Monitor("PANEL", 100, 4, 5), Monitor("EXTERNAL", 101, 4, 5) };
                plan = Native.BuildPlan(extend, reconnected);
                Check(plan.Paths[0].Target.Id == 100 && plan.Paths[1].Target.Id == 101 && plan.Paths.All(p => p.Source.Id >= 4), "Persistent monitor identities rebound to current route IDs");
                var custom = Setting("external", 0, 0); custom.Scaling = 5;
                plan = Native.BuildPlan(new List<MonitorSetting> { custom }, inventory); Check(plan.Paths[0].Target.Scaling == 128, "Vendor custom scaling not submitted to public API");
                var badClone = new List<MonitorSetting> { Setting("panel", 0, 0), Setting("external", 0, 10) };
                Reject(delegate { Profile.CheckLayout(badClone); }, "Inconsistent clone geometry rejected");
                Reject(delegate { Profile.CheckLayout(new List<MonitorSetting> { Setting("panel", 0, 0), Setting("PANEL", 1, 1920) }); }, "Duplicate physical monitor rejected");
                var invalid = Setting("external", 0, 0); invalid.RefreshDen = 0;
                Reject(delegate { Profile.CheckLayout(new List<MonitorSetting> { invalid }); }, "Invalid saved refresh rejected");
                Check(Native.Matches(extend, new List<MonitorSetting> { Setting("EXTERNAL", 3, 1920), Setting("PANEL", 2, 0) }), "Profile match independent of enumeration and group numbering");
                Check(!Native.Matches(extend, clone), "Extended and cloned layouts differ");
                var differentHz = new List<MonitorSetting> { Setting("panel", 0, 0), Setting("external", 1, 1920) }; differentHz[1].RefreshNum = 60;
                Check(!Native.Matches(extend, differentHz), "Saved profile does not accept large refresh reduction");
                var savedProfile = new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Layout personale", Mode = 0, Layout = extend };
                defaults.Profiles.Add(savedProfile); ProfileStore.Save(file, defaults);
                Check(ProfileStore.Load(file).Profiles.Last().Layout[1].X == 1920, "Captured layout XML round trip");
                DisplayState state = StateFixture(); string snapshot = Path.Combine(folder, "backup.bin"); Snapshot.Save(snapshot, state);
                var timingLayout = state.Capture(new List<MonitorInfo> { Monitor("physical-monitor", 0, 0) });
                plan = Native.BuildPlan(timingLayout, new List<MonitorInfo> { Monitor("physical-monitor", 20, 4) });
                Check(plan.Modes.Length == 2 && plan.Modes[1].Type == 2 && plan.Modes[1].Id == 20 && plan.Modes[1].Target.PixelRate == 600000000, "Saved profile retains full target timing and rebinds target ID");
                Check(plan.Paths[0].Source.Id == 4 && plan.Paths[0].Target.ModeIndex == 1, "Timing mode correctly referenced after reconnection");
                savedProfile.Layout = timingLayout; ProfileStore.Save(file, defaults);
                Check(ProfileStore.Load(file).Profiles.Last().Layout[0].Timing.PixelRate == 600000000, "Full native timing survives profile XML");
                var customProfile = new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Risoluzioni precise", Mode = 4, CustomResolutions = true, Layout = extend };
                customProfile.Check(); Check(customProfile.HasLayout, "Topology with per-monitor resolutions uses an exact layout");
                defaults.Profiles.Add(customProfile); ProfileStore.Save(file, defaults);
                Check(ProfileStore.Load(file).Profiles.Last().CustomResolutions, "Resolution customization survives persistence");
                var independent = new List<MonitorSetting> { Setting("panel", 0, 0), Setting("external", 1, 0) }; independent[1].Width = 3440; independent[1].Height = 1440;
                var built = LayoutEditor.Build(4, independent);
                Check(built[1].Width == 3440 && built[0].Width == 1920 && built[1].X == 1920, "Independent extended resolutions and contiguous placement");
                Check(independent[1].X == 0, "Layout editing does not mutate existing saved settings");
                Reject(delegate { LayoutEditor.Build(2, independent); }, "Duplicate topology rejects different desktop resolutions");
                Check(LayoutEditor.Build(2, clone).All(s => s.Group == 0 && s.X == 0), "Duplicate editor keeps one shared desktop");
                var shifted = new List<MonitorSetting> { Setting("external", 1, 1920) };
                Check(LayoutEditor.Build(0, shifted)[0].X == 0, "Disabling the primary monitor normalizes the new primary origin");
                Check(LayoutEditor.Aspect(1920, 1080) == "16:9" && LayoutEditor.Aspect(1920, 1200) == "16:10", "Aspect ratio computed exactly from selected resolution");
                Check((Native.PlanFlags(false) & 0x100) != 0 && (Native.PlanFlags(false) & 0x400) == 0 && (Native.PlanFlags(true) & 0x100) == 0, "Apply forces driver mode without allowing substituted resolutions");
                var stretched = Setting("panel", 0, 0); stretched.Scaling = 3; stretched.Timing = new Signal { Active = new Region { Width = 3440, Height = 1440 } };
                var wanted = new List<MonitorSetting> { Setting("panel", 0, 0) };
                Check(!Native.MatchesProtected(wanted, new List<MonitorSetting> { stretched }), "Same desktop resolution with distorted signal aspect is rejected");
                stretched.Scaling = 4;
                Check(Native.MatchesProtected(wanted, new List<MonitorSetting> { stretched }), "Aspect preserving scaling to ultrawide signal is accepted");
                stretched.Scaling = 1; stretched.Timing = new Signal { Active = new Region { Width = 1920, Height = 1080 } };
                Check(Native.MatchesProtected(wanted, new List<MonitorSetting> { stretched }), "Identity scaling at selected signal aspect is accepted");
                stretched.Rotation = 2; stretched.Width = 1080; stretched.Height = 1920;
                Check(Native.PreservesAspect(stretched), "Portrait signal aspect accounts for rotation");
                var virtualMonitor = Monitor("virtual-panel", 0, 0);
                var virtualState = StateFixture(); virtualState.Modes[0].Source.Width = 1920; virtualState.Modes[0].Source.Height = 1080;
                virtualState.Paths[0].Flags = 9; virtualState.Paths[0].Source.ModeIndex = 0xffff; virtualState.Paths[0].Target.ModeIndex = (1u << 16) | 2u;
                Array.Resize(ref virtualState.Modes, 3);
                virtualState.Modes[2] = new Mode { Type = 3, DesktopImage = new DesktopImageInfo { SourceSize = new Position { X = 3440, Y = 1440 },
                    Region = new DisplayRect { Left = 440, Right = 3000, Bottom = 1440 }, Clip = new DisplayRect { Right = 1920, Bottom = 1080 } } };
                virtualState.Check(); var virtualLayout = virtualState.Capture(new List<MonitorInfo> { virtualMonitor });
                Check(Native.SourceIndex(virtualState.Paths[0]) == 0 && Native.TargetIndex(virtualState.Paths[0]) == 1 && Native.ImageIndex(virtualState.Paths[0]) == 2, "Packed virtual source, target and desktop indexes are decoded separately");
                Check(virtualLayout[0].Width == 1920 && virtualLayout[0].Height == 1080 && virtualLayout[0].DesktopImage.HasValue && Native.PreservesAspect(virtualLayout[0]), "Virtual letterboxed desktop is verified independently of identity signal scaling");
                var virtualImage = virtualLayout[0].DesktopImage.Value; virtualImage.Region.Left = 0; virtualImage.Region.Right = 3440;
                virtualLayout[0].DesktopImage = virtualImage;
                Check(!Native.PreservesAspect(virtualLayout[0]), "Stretching inside a virtual desktop cannot masquerade as preserved aspect");
                virtualLayout[0].Scaling = 4;
                Check(!Native.PreservesAspect(virtualLayout[0]), "Aspect scaling on the outer path cannot excuse a stretched virtual image");
                virtualImage.Region.Left = 440; virtualImage.Region.Right = 3000; virtualImage.Clip.Right = 1600; virtualLayout[0].DesktopImage = virtualImage;
                Check(!Native.PreservesAspect(virtualLayout[0]), "A virtual image that crops desktop pixels is rejected");
                string virtualBackup = Path.Combine(folder, "virtual.bin"); Snapshot.Save(virtualBackup, virtualState); var virtualRestored = Snapshot.Load(virtualBackup);
                Check(virtualRestored.Modes[2].DesktopImage.Region.Left == 440 && Native.SourceIndex(virtualRestored.Paths[0]) == 0 && Native.AwarenessFlags(virtualRestored.Paths) == 0x8000 && Native.AwarenessFlags(state.Paths) == 0, "Rollback preserves virtual desktop geometry and selects compatible API flags for both snapshot formats");
                virtualState.Paths[0].Source.ModeIndex = UInt32.MaxValue;
                Reject(delegate { virtualState.Check(); }, "Invalid packed virtual source mode is rejected before recovery");
                var newVirtualRoute = Monitor("virtual-panel", 20, 4); newVirtualRoute.Candidates[0] = new DisplayPath { Source = newVirtualRoute.Candidates[0].Source, Target = newVirtualRoute.Candidates[0].Target, Flags = 9 };
                virtualLayout[0].DesktopImage = null; virtualLayout[0].Scaling = 4;
                plan = Native.BuildPlan(virtualLayout, new List<MonitorInfo> { newVirtualRoute });
                Check(plan.Modes.Length == 3 && Native.VirtualMode(plan.Paths[0]) && Native.SourceIndex(plan.Paths[0]) == 0 && Native.TargetIndex(plan.Paths[0]) == 1 && Native.ImageIndex(plan.Paths[0]) == 2 && (plan.Paths[0].Source.ModeIndex & 0xffff) == 0xffff, "Virtual plan retains capability flags, explicit source index and invalid clone group");
                Check(plan.Modes[2].DesktopImage.SourceSize.X == 1920 && plan.Modes[2].DesktopImage.Region.Right == 1920 && plan.Modes[2].DesktopImage.Clip.Right == 1920 && plan.Modes[1].Target.Active.Width == 3440, "1080p profile resets the virtual surface instead of inheriting the old ultrawide image while retaining physical timings");
                var noTiming = virtualLayout[0].Copy(); noTiming.Timing = new Signal(); newVirtualRoute.PreferredTiming = virtualLayout[0].Timing;
                plan = Native.BuildPlan(new List<MonitorSetting> { noTiming }, new List<MonitorInfo> { newVirtualRoute });
                Check(plan.Modes[Native.TargetIndex(plan.Paths[0])].Target.PixelRate == virtualLayout[0].Timing.PixelRate, "Virtual profiles without saved timings use the current monitor preferred physical signal");
                newVirtualRoute.PreferredTiming.VSync.Num = 60; newVirtualRoute.Baseline = virtualLayout[0].Copy();
                plan = Native.BuildPlan(new List<MonitorSetting> { noTiming }, new List<MonitorInfo> { newVirtualRoute });
                Check(plan.Modes[Native.TargetIndex(plan.Paths[0])].Target.VSync.Num == 120, "Fallback timing keeps the requested refresh instead of pinning an incompatible preferred signal");
                newVirtualRoute.Baseline = null;
                plan = Native.BuildPlan(new List<MonitorSetting> { noTiming }, new List<MonitorInfo> { newVirtualRoute });
                Check(Native.TargetIndex(plan.Paths[0]) == UInt32.MaxValue, "Driver chooses the signal when no observed timing matches the profile refresh");
                int reads = 0, pauses = 0;
                var oldDesktop = Setting("panel", 0, 0); oldDesktop.Width = 2580;
                DisplayObservation settled = DisplayVerification.WaitFor(wanted, delegate { reads++; return new DisplayObservation { Layout = reads == 1 ? new List<MonitorSetting> { oldDesktop } : wanted }; }, delegate { pauses++; }, 5);
                Check(reads == 3 && pauses == 2 && settled.Layout == wanted, "Post-apply verification waits for two matching samples after stale driver state");
                reads = 0;
                DisplayVerification.WaitFor(wanted, delegate { reads++; if (reads == 1) throw new InvalidOperationException("Display is reconnecting"); return new DisplayObservation { Layout = wanted }; }, delegate { }, 4);
                Check(reads == 3, "A transient query error is retried during display settling");
                reads = 0;
                DisplayVerification.WaitFor(wanted, delegate { reads++; return new DisplayObservation { Layout = reads == 2 ? new List<MonitorSetting> { oldDesktop } : wanted }; }, delegate { }, 5);
                Check(reads == 4, "A single transient match is not accepted as a settled display configuration");
                bool mismatchRejected = false;
                try { DisplayVerification.WaitFor(wanted, delegate { return new DisplayObservation { Layout = new List<MonitorSetting> { oldDesktop } }; }, delegate { }, 3); }
                catch (InvalidOperationException ex) { mismatchRejected = ex.Message.Contains("1920 x 1080") && ex.Message.Contains("2580 x 1080"); }
                Check(mismatchRejected, "Persistent resolution substitution is rejected with requested and received values");
                var history = Settings.Defaults(); var now = DateTime.UtcNow;
                var live = Monitor("persistent", 1, 1); live.Baseline = Setting("persistent", 0, 0); live.Active = true;
                Check(MonitorHistory.Observe(history, new List<MonitorInfo> { live }, now, false), "Connected monitor added to persistent history");
                history.Monitors[0].Alias = "Scrivania à";
                live.Key = "PERSISTENT"; live.Name = "Changed driver name";
                MonitorHistory.Observe(history, new List<MonitorInfo> { live }, now.AddMinutes(2), false);
                Check(history.Monitors.Count == 1 && history.Monitors[0].DisplayName == "Scrivania à" && history.Monitors[0].HardwareName == live.Name, "Reconnect merges stable identity and preserves alias");
                MonitorHistory.Observe(history, new List<MonitorInfo>(), now.AddMinutes(3), false);
                Check(history.Monitors.Count == 1 && history.Monitors[0].LastSeenUtc == now.AddMinutes(2).ToString("o", System.Globalization.CultureInfo.InvariantCulture), "Disconnected monitor and last seen survive refresh");
                ProfileStore.Save(file, history); history = ProfileStore.Load(file);
                Check(history.Monitors[0].Alias == "Scrivania à" && history.Monitors[0].FirstSeenUtc != null, "Names and history timestamps survive XML");
                Check(MonitorHistory.ImportKnown(history, new List<MonitorRecord> { new MonitorRecord { DevicePath = "old-tv", HardwareName = "Remembered TV" }, new MonitorRecord { DevicePath = "persistent", HardwareName = "Generic display" } }) && history.Monitors.Count == 2 && history.Monitors[0].Alias == "Scrivania à", "Windows remembered interfaces merge without replacing names");
                ProfileStore.Save(file, history); Check(ProfileStore.Load(file).Monitors[1].LastSetting == null, "Historical monitor without known resolution can be saved");
                history.Monitors.Add(new MonitorRecord { DevicePath = "persistent", HardwareName = "duplicate" });
                Reject(delegate { history.Check(); }, "Duplicate historical device identities rejected");
                var legacy = Fixture(); legacy.Version = 1; legacy.SwitchA = legacy.Profiles[0].Id; legacy.SwitchB = legacy.Profiles[1].Id;
                legacy.SwitchHotkey = new Shortcut { Modifiers = 3, Key = 121 }; legacy.Profiles[0].Mode = 2; legacy.Profiles[0].Layout.Clear();
                ProfileStore.Save(file, legacy); var migrated = ProfileStore.Load(file);
                Check(migrated.Version == 2 && migrated.Profiles.Count == 4 && !migrated.Profiles[0].HasLayout && !migrated.Profiles[1].ProtectResolution && migrated.SwitchHotkey.Key == 0, "Version 1 migration preserves profiles without arming a legacy controller");
                ProfileStore.Save(file, migrated); Check(ProfileStore.Load(file).Version == 2, "Upgraded settings save and load");
                Check(File.Exists(file + ".v1.bak") && File.ReadAllText(file + ".v1.bak").Contains("<Version>1</Version>"), "Migration retains the original legacy XML before replacing it");
                var protectedProfile = new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Gioco", Mode = 0, Layout = wanted };
                var policy = new ProfileProtection(); int corrections = 0;
                Func<List<MonitorSetting>> correct = delegate { corrections++; return wanted; };
                Check(!policy.Tick(protectedProfile, new List<string> { "panel" }, wanted, now, correct) && corrections == 0 && policy.State == ProtectionState.Watching, "Protection never reapplies an already matching mode");
                var drift = new List<MonitorSetting> { Setting("panel", 0, 0) }; drift[0].Width = 2560;
                Check(policy.Tick(protectedProfile, new List<string> { "PANEL" }, drift, now, correct) && corrections == 1, "Game resolution drift restores profile once");
                Check(!policy.Tick(protectedProfile, new List<string>(), drift, now, correct) && policy.State == ProtectionState.MissingMonitor && corrections == 1, "Unplug suspends protection without native changes");
                Check(policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now, correct) && policy.State == ProtectionState.Watching, "Reconnect resumes the requested profile");
                policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now, correct);
                Check(!policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now, correct) && corrections == 3 && policy.State == ProtectionState.Conflict, "Repeated game mode fights suspend after three corrections per minute");
                Check(!policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now.AddMinutes(2), correct) && corrections == 3, "A conflict requires explicit profile reactivation");
                policy.Reset();
                Check(policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now.AddMinutes(2), correct), "Reactivation resets conflict protection");
                policy.Reset(); protectedProfile.ProtectResolution = false;
                Check(!policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now, correct) && policy.State == ProtectionState.Off, "Disabled protection never changes displays");
                protectedProfile.ProtectResolution = true; policy.Reset();
                Check(!policy.Tick(protectedProfile, new List<string> { "panel" }, drift, now, delegate { return drift; }) && policy.State == ProtectionState.Conflict, "Driver substitution is reported as a conflict");
                var protectedStore = Settings.Defaults(); protectedStore.Profiles.Add(protectedProfile); protectedStore.ActiveProfileId = protectedProfile.Id;
                ProfileStore.Save(file, protectedStore);
                Check(ProfileStore.Load(file).ActiveProfileId == protectedProfile.Id && ProfileStore.Load(file).Profiles[0].ProtectResolution, "Active profile and protection preference persist");
                var selection = Settings.Defaults(); selection.Profiles.Add(protectedProfile);
                Check(ProfileSelection.Active(selection, wanted) == protectedProfile, "One matching profile is recognized from actual output");
                Check(ProfileSelection.Active(selection, drift) == null, "Different actual resolution never reports a profile active");
                var twin = new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Stesso schermo", Mode = 0, Layout = wanted };
                selection.Profiles.Add(twin);
                Check(ProfileSelection.Active(selection, wanted) == null, "Ambiguous matching profiles do not invent an active selection");
                selection.ActiveProfileId = twin.Id;
                Check(ProfileSelection.Active(selection, wanted) == twin && ProfileSelection.Active(selection, drift) == null, "Explicit activation disambiguates only if the output still matches");
                selection.ActiveProfileId = null; selection.ProfileDeactivated = true; selection.Profiles.Remove(twin);
                Check(ProfileSelection.Active(selection, wanted) == null, "Explicit deactivation overrides coincidentally matching display settings");
                ProfileStore.Save(file, selection);
                Check(ProfileStore.Load(file).ProfileDeactivated && ProfileSelection.Active(ProfileStore.Load(file), wanted) == null, "Deactivation survives restart");
                var dialogHistory = new List<MonitorRecord> {
                    new MonitorRecord { DevicePath = "panel", HardwareName = "Generic panel", Alias = "Il mio monitor", LastSetting = wanted[0] },
                    new MonitorRecord { DevicePath = "remembered", HardwareName = "Old TV", Alias = "TV salotto" } };
                using (var dialog = new ProfileDialog(protectedProfile, false, wanted, null, dialogHistory))
                {
                    var rows = dialog.Controls.OfType<System.Windows.Forms.FlowLayoutPanel>().Single().Controls.OfType<ResolutionRow>().ToList();
                    Check(rows.Count == 2 && rows[0].DisplayName == "Il mio monitor" && rows[1].DisplayName == "TV salotto" && !rows[1].EnabledMonitor.Checked, "Editor shows global aliases and an unobserved disconnected monitor");
                    dialog.ShowInTaskbar = false; dialog.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dialog.Location = new System.Drawing.Point(-30000, -30000); dialog.Show();
                    dialog.Controls.OfType<System.Windows.Forms.Button>().Single(button => button.Text == "Salva profilo").PerformClick();
                    Check(dialog.Result != null && dialog.Result.Mode == 0 && dialog.Result.Layout.Count == 1 && dialog.Result.Layout[0].Scaling == 4 && dialog.Result.ProtectResolution, "Editor saves explicit selected displays with aspect preservation and protection");
                }
                DisplayState restored = Snapshot.Load(snapshot); Check(restored.Modes[1].Target.PixelRate == 600000000 && restored.Modes[0].Source.Width == 3440, "Exact rollback snapshot round trip");
                byte[] bytes = File.ReadAllBytes(snapshot); Array.Resize(ref bytes, bytes.Length - 1); File.WriteAllBytes(snapshot, bytes);
                Reject(delegate { Snapshot.Load(snapshot); }, "Truncated recovery snapshot rejected");
                Check(state.Topology() == 8, "Single external topology"); state.Paths[0].Target.Technology = 11; Check(state.Topology() == 1, "Internal topology");
                Check(Native.IsInternal(6) && Native.IsInternal(13) && Native.IsInternal(0x80000000) && !Native.IsInternal(5), "Built-in output technologies");
                using (var stop = new ManualResetEvent(false)) using (var ended = new ManualResetEvent(false)) using (var armed = new ManualResetEvent(false))
                {
                    Check(Recovery.NeedsRecovery(stop, ended, armed, 0, 0), "Guardian startup timeout");
                    stop.Set(); Check(!Recovery.NeedsRecovery(stop, ended, armed, 0, 0), "Guardian clean stop"); stop.Reset();
                    ended.Set(); Check(Recovery.NeedsRecovery(stop, ended, armed, 0, 0), "Parent exits before apply"); ended.Reset();
                    armed.Set(); Check(Recovery.NeedsRecovery(stop, ended, armed, 0, 0), "Unconfirmed profile times out");
                    bool? recovery = null; var worker = new Thread(delegate() { recovery = Recovery.NeedsRecovery(stop, ended, armed, 1000, 2000); }); worker.IsBackground = true; worker.Start();
                    Check(!worker.Join(40), "Guardian waits during confirmation"); stop.Set();
                    Check(worker.Join(2000) && recovery == false, "Confirmed change stops guardian and stays applied");
                }
                Check(Updates.ParseVersion("v2.10.0") > Updates.ParseVersion("v2.9.9"), "Update versions compared numerically");
                Check(Updates.ParseVersion("2.2.0") == Updates.ParseVersion("v2.2.0.0"), "Release and executable versions normalized");
                Check(Updates.ParseVersion("v2.3.0-beta") == null && Updates.ParseVersion("latest") == null, "Non-stable version strings rejected");
                string releaseJson = "{\"tag_name\":\"v2.3.0\",\"body\":\"Release notes\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"MonitorSwitch.exe\",\"state\":\"uploaded\",\"size\":200000,\"digest\":\"sha256:" + new string('a', 64) + "\",\"browser_download_url\":\"https://github.com/carellice/MonitorSwitch/releases/download/v2.3.0/MonitorSwitch.exe\"}]}";
                UpdateInfo update = Updates.ParseRelease(releaseJson, new Version(2, 2, 0, 0));
                Check(update != null && update.Version == new Version(2, 3, 0, 0) && update.Hash.Length == 64, "Published stable executable recognized");
                Check(Updates.ParseRelease(releaseJson, new Version(2, 3, 0, 0)) == null, "Same version does not update");
                Check(Updates.ParseRelease(releaseJson, new Version(3, 0, 0, 0)) == null, "Older version does not downgrade");
                Check(Updates.ParseRelease(releaseJson.Replace("\"draft\":false", "\"draft\":true"), new Version(1, 0, 0, 0)) == null, "Draft release ignored");
                Check(Updates.ParseRelease(releaseJson.Replace("\"prerelease\":false", "\"prerelease\":true"), new Version(1, 0, 0, 0)) == null, "Prerelease ignored");
                Reject(delegate { Updates.ParseRelease(releaseJson.Replace("sha256:", "sha512:"), new Version(1, 0, 0, 0)); }, "Missing SHA256 prevents updates");
                Reject(delegate { Updates.ParseRelease(releaseJson.Replace("200000", "999999999"), new Version(1, 0, 0, 0)); }, "Oversized asset rejected");
                Reject(delegate { Updates.ParseRelease(releaseJson.Replace("\"uploaded\"", "\"open\""), new Version(1, 0, 0, 0)); }, "Uploading asset rejected");
                Reject(delegate { Updates.ParseRelease(releaseJson.Replace("https://github.com/", "https://example.com/"), new Version(1, 0, 0, 0)); }, "External executable URL rejected");
                string self = System.Windows.Forms.Application.ExecutablePath, hash = Updates.HashFile(self);
                Updates.VerifyBinary(self, hash, Updates.Current); count++;
                Reject(delegate { Updates.VerifyBinary(self, new string('0', 64), Updates.Current); }, "Corrupt download rejected by digest");
                Reject(delegate { Updates.VerifyBinary(self, hash, new Version(99, 0, 0, 0)); }, "Executable version must match release");
                string payload = Path.Combine(folder, "new.exe"), target = Path.Combine(folder, "installed.exe"), backup = Path.Combine(folder, "previous.exe");
                File.Copy(self, payload); File.AppendAllText(payload, "update-engine-test"); string newHash = Updates.HashFile(payload); File.Copy(self, target);
                bool started = false;
                Updates.ReplaceAndStart(payload, target, backup, newHash, Updates.Current, delegate { started = true; });
                Check(started && Updates.HashFile(target) == newHash && Updates.HashFile(backup) == hash, "Atomic replacement preserves previous executable");
                File.Copy(self, target, true); File.Delete(backup); bool rolledBack = false;
                try { Updates.ReplaceAndStart(payload, target, backup, newHash, Updates.Current, delegate { throw new IOException("Simulated startup failure"); }); }
                catch (IOException) { rolledBack = true; }
                Check(rolledBack && Updates.HashFile(target) == hash, "Failed startup restores original executable");
                Reject(delegate { Updates.ReplaceAndStart(payload, target, backup, new string('0', 64), Updates.Current, delegate { }); }, "Invalid candidate never replaces installed file");
                Check(Updates.HashFile(target) == hash, "Rejected candidate leaves installed bytes unchanged");
                string staging = Path.Combine(folder, "staging"), stage = Path.Combine(staging, "test"); Directory.CreateDirectory(stage);
                string staged = Path.Combine(stage, "MonitorSwitch.exe"); File.Copy(self, staged);
                var manifest = new UpdateManifest { Target = target, Payload = staged, Hash = hash, Version = Updates.Current.ToString(), ParentId = 1, ParentStarted = 1 };
                string manifestPath = Path.Combine(stage, "update.xml"); Updates.CheckManifest(manifest, manifestPath, staging); count++;
                Reject(delegate { Updates.CheckManifest(manifest, Path.Combine(folder, "update.xml"), staging); }, "Update manifest must stay in its staging root");
                manifest.Payload = self;
                Reject(delegate { Updates.CheckManifest(manifest, manifestPath, staging); }, "Manifest cannot select a payload outside staging directory");
                Check(Updates.Quote(@"C:\Program Files\MonitorSwitch.exe") == "\"C:\\Program Files\\MonitorSwitch.exe\"", "Updater paths with spaces quoted without shell");
                File.WriteAllText(report, "PASS: " + count + " checks. No display settings changed.\r\n"); return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
            finally
            {
                string root = Path.GetDirectoryName(Path.GetFullPath(report)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(folder).StartsWith("monitor-tests-", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
        }
    }
}
