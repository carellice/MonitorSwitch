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
        public static int Run(string report)
        {
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "monitor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Check(Marshal.SizeOf(typeof(DisplayPath)) == 72, "Path ABI"); Check(Marshal.SizeOf(typeof(Mode)) == 64, "Mode ABI");
                Check(Marshal.SizeOf(typeof(Signal)) == 48, "Signal ABI"); Check(Marshal.SizeOf(typeof(TargetName)) == 420, "Target identity ABI");
                Check(Marshal.SizeOf(typeof(SourceName)) == 84, "Source ABI"); Check(Marshal.SizeOf(typeof(DisplayDevice)) == 840, "GPU name ABI");
                Check(Marshal.SizeOf(typeof(DeviceMode)) == 220, "Resolution DEVMODEW ABI");
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
                uint width, height; LayoutEditor.Resolution(" 2560 × 1440 ", out width, out height);
                Check(width == 2560 && height == 1440, "Parse resolution with multiplication sign");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("1920", out w, out h); }, "Reject incomplete resolution");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("0 x 1080", out w, out h); }, "Reject zero resolution");
                Reject(delegate { uint w, h; LayoutEditor.Resolution("999999999999 x 1080", out w, out h); }, "Reject overflowing resolution");
                Settings defaults = Settings.Defaults(); defaults.Check();
                Check(defaults.ConfirmChanges, "Display confirmation enabled by default");
                Check(defaults.Profiles.Count == 4 && defaults.Profiles[0].Mode == 2 && defaults.Profiles[1].Mode == 8, "Game clone and work external defaults");
                Check(defaults.SwitchA == defaults.Profiles[0].Id && defaults.SwitchB == defaults.Profiles[1].Id, "Default toggle pair");
                Check(defaults.SwitchHotkey.Label == "Ctrl+Alt+F10", "Default toggle shortcut");
                string file = Path.Combine(folder, "profiles.xml"); ProfileStore.Save(file, defaults);
                Settings loaded = ProfileStore.Load(file); Check(loaded.Profiles[0].Id == defaults.Profiles[0].Id && loaded.SwitchB == defaults.SwitchB, "Settings round trip");
                loaded.Profiles[0].Name = "Gioco & relax à"; ProfileStore.Save(file, loaded);
                Check(ProfileStore.Load(file).Profiles[0].Name == "Gioco & relax à", "Atomic replacement and Unicode XML");
                loaded.ConfirmChanges = false; ProfileStore.Save(file, loaded);
                Check(!ProfileStore.Load(file).ConfirmChanges, "One-click switch preference persists");
                var duplicate = ProfileStore.Load(file); duplicate.Profiles[1].Hotkey = duplicate.Profiles[0].Hotkey.Clone();
                Reject(delegate { duplicate.Check(); }, "Reject duplicate profile hotkey");
                duplicate = ProfileStore.Load(file); duplicate.Profiles[0].Hotkey = duplicate.SwitchHotkey.Clone();
                Reject(delegate { duplicate.Check(); }, "Reject shortcut colliding with toggle");
                duplicate = ProfileStore.Load(file); duplicate.Profiles[1].Name = duplicate.Profiles[0].Name;
                Reject(delegate { duplicate.Check(); }, "Reject duplicate names");
                duplicate = ProfileStore.Load(file); duplicate.SwitchA = "missing";
                Reject(delegate { duplicate.Check(); }, "Reject missing toggle profile");
                duplicate = ProfileStore.Load(file); duplicate.Profiles[0].Mode = 6;
                Reject(delegate { duplicate.Check(); }, "Reject invalid topology flags");
                Reject(delegate { new Shortcut { Key = 65 }.Check(); }, "Reject unmodified global letter");
                Reject(delegate { new Shortcut { Key = 112, Modifiers = 8 }.Check(); }, "Reject unsupported modifier");
                var single = Settings.Defaults(); single.Profiles.RemoveRange(1, 3); single.SwitchA = single.SwitchB = single.Profiles[0].Id; single.Check(); count++;
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
                File.WriteAllText(report, "PASS: " + count + " checks. No display settings changed.\r\n"); return 0;
            }
            catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
            finally { foreach (string child in Directory.GetFiles(folder)) File.Delete(child); Directory.Delete(folder); }
        }
    }
}
