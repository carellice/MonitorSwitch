using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace MonitorSwitch
{
    public sealed class MonitorSetting
    {
        public string DevicePath { get; set; }
        public string Name { get; set; }
        public int Group { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public uint Rotation { get; set; }
        public uint Scaling { get; set; }
        public uint RefreshNum { get; set; }
        public uint RefreshDen { get; set; }
        public Signal Timing { get; set; }
        // Observed virtual desktop geometry is used for verification, never
        // persisted as a constraint on a future edited profile.
        internal DesktopImageInfo? DesktopImage { get; set; }
        public MonitorSetting Copy() { return (MonitorSetting)MemberwiseClone(); }
    }
    public sealed class Shortcut
    {
        public uint Modifiers { get; set; }
        public uint Key { get; set; }
        public string Label
        {
            get
            {
                if (Key == 0) return "Nessuna";
                return ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") +
                    (Key >= 112 && Key <= 123 ? "F" + (Key - 111) : ((char)Key).ToString());
            }
        }
        public Shortcut Clone() { return new Shortcut { Modifiers = Modifiers, Key = Key }; }
        public void Check()
        {
            if (Key == 0) { if (Modifiers != 0) throw new InvalidDataException("Scorciatoia senza tasto."); return; }
            if ((Modifiers & ~7u) != 0 || (Modifiers & 3u) == 0 || !((Key >= 112 && Key <= 123) || (Key >= 65 && Key <= 90) || (Key >= 48 && Key <= 57)))
                throw new InvalidDataException("Scorciatoia non valida: scegli Ctrl o Alt e un tasto.");
        }
    }
    public sealed class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Mode { get; set; }
        public Shortcut Hotkey { get; set; }
        public List<MonitorSetting> Layout { get; set; }
        public bool CustomResolutions { get; set; }
        public bool ProtectResolution { get; set; }
        public bool HasLayout { get { return Mode == 0 || CustomResolutions; } }
        public Profile() { Layout = new List<MonitorSetting>(); Hotkey = new Shortcut(); ProtectResolution = true; }
        public string Description { get { return HasLayout ? String.Join(" · ", Layout.Select(s => s.Width + " × " + s.Height + " (" + LayoutEditor.Aspect(s.Width, s.Height) + ")")) : "Da configurare: scegli i monitor"; } }
        public override string ToString() { return Name; }
        public static string ModeLabel(int mode)
        {
            switch (mode)
            {
                case 0: return "Configurazione salvata";
                case 1: return "Solo schermo del portatile";
                case 2: return "Duplica gli schermi";
                case 4: return "Estendi gli schermi";
                case 8: return "Solo monitor esterni";
                default: return "Sconosciuta";
            }
        }
        public void Check()
        {
            Guid parsed;
            if (!Guid.TryParse(Id, out parsed) || String.IsNullOrWhiteSpace(Name) || Name.Length > 60 || Hotkey == null || Layout == null)
                throw new InvalidDataException("Profilo non valido.");
            if (Mode != 0 && Mode != 1 && Mode != 2 && Mode != 4 && Mode != 8) throw new InvalidDataException("Modalita del profilo non valida.");
            Hotkey.Check(); if (HasLayout) CheckLayout(Layout);
            if (Mode == 2 && CustomResolutions && Layout.Count < 2) throw new InvalidDataException("La duplicazione richiede almeno due monitor collegati.");
        }
        public static void CheckLayout(List<MonitorSetting> layout)
        {
            if (layout == null || layout.Count < 1 || layout.Count > 32) throw new InvalidDataException("Il profilo deve contenere almeno un monitor.");
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MonitorSetting s in layout)
            {
                if (s == null || String.IsNullOrWhiteSpace(s.DevicePath) || s.DevicePath.Length > 512 || !identities.Add(s.DevicePath) ||
                    s.Width < 1 || s.Width > 32768 || s.Height < 1 || s.Height > 32768 || s.Group < 0 || s.Group > 31 ||
                    Math.Abs((long)s.X) > 131072 || Math.Abs((long)s.Y) > 131072 || s.Rotation < 1 || s.Rotation > 4 ||
                    (s.Scaling < 1 || (s.Scaling > 5 && s.Scaling != 128)) || s.RefreshNum == 0 || s.RefreshDen == 0)
                    throw new InvalidDataException("Impostazioni del monitor non valide.");
            }
            foreach (var g in layout.GroupBy(s => s.Group))
            {
                MonitorSetting first = g.First();
                if (g.Any(s => s.Width != first.Width || s.Height != first.Height || s.X != first.X || s.Y != first.Y))
                    throw new InvalidDataException("I monitor duplicati devono condividere risoluzione e posizione.");
            }
        }
    }
    public sealed class Settings
    {
        public int Version { get; set; }
        public List<Profile> Profiles { get; set; }
        public string SwitchA { get; set; }
        public string SwitchB { get; set; }
        public Shortcut SwitchHotkey { get; set; }
        public bool ConfirmChanges { get; set; }
        public bool CheckUpdatesOnStartup { get; set; }
        public string LastUpdateCheckUtc { get; set; }
        public List<MonitorRecord> Monitors { get; set; }
        public string ActiveProfileId { get; set; }
        public bool ProfileDeactivated { get; set; }
        [XmlIgnore] public bool PendingLegacyBackup { get; set; }
        public Settings() { Version = 2; Profiles = new List<Profile>(); Monitors = new List<MonitorRecord>(); SwitchHotkey = new Shortcut(); ConfirmChanges = true; CheckUpdatesOnStartup = true; }
        public static Settings Defaults()
        {
            return new Settings();
        }
        public void Upgrade()
        {
            if (Version != 1) return;
            Version = 2; ActiveProfileId = null; PendingLegacyBackup = true;
            foreach (Profile p in Profiles) p.ProtectResolution = false;
            SwitchA = SwitchB = null; SwitchHotkey = new Shortcut();
        }
        public void Check()
        {
            if ((Version != 1 && Version != 2) || Profiles == null || Profiles.Count > 64 || SwitchHotkey == null || Monitors == null || Monitors.Count > 512)
                throw new InvalidDataException("Archivio profili non valido.");
            var ids = new HashSet<string>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var shortcuts = new HashSet<string>();
            foreach (Profile p in Profiles)
            {
                if (p == null) throw new InvalidDataException("Profilo vuoto."); p.Check();
                if (!ids.Add(p.Id) || !names.Add(p.Name.Trim())) throw new InvalidDataException("Ogni profilo deve avere un nome diverso.");
                AddShortcut(shortcuts, p.Hotkey);
            }
            if (Version == 1)
            {
                SwitchHotkey.Check(); AddShortcut(shortcuts, SwitchHotkey);
                if (!ids.Contains(SwitchA) || !ids.Contains(SwitchB)) throw new InvalidDataException("Seleziona i due profili dell'interruttore.");
            }
            if (!String.IsNullOrEmpty(ActiveProfileId) && !ids.Contains(ActiveProfileId)) throw new InvalidDataException("Profilo attivo non valido.");
            var monitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MonitorRecord m in Monitors)
            {
                if (m == null || String.IsNullOrWhiteSpace(m.DevicePath) || m.DevicePath.Length > 512 || !monitorIds.Add(m.DevicePath) ||
                    String.IsNullOrWhiteSpace(m.HardwareName) || m.HardwareName.Length > 256 || (m.Alias != null && m.Alias.Length > 60) || m.Resolutions == null || m.Resolutions.Count > 512)
                    throw new InvalidDataException("Storico monitor non valido.");
                if (m.LastSetting != null)
                {
                    if (!String.Equals(m.DevicePath, m.LastSetting.DevicePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Identita del monitor nello storico non coerente.");
                    Profile.CheckLayout(new List<MonitorSetting> { m.LastSetting });
                }
            }
        }
        private static void AddShortcut(HashSet<string> used, Shortcut key)
        { if (key.Key != 0 && !used.Add(key.Modifiers + ":" + key.Key)) throw new InvalidDataException("Questa combinazione di tasti e gia assegnata a un altro profilo o all'interruttore."); }
    }
    internal static class ProfileSelection
    {
        public static Profile Active(Settings settings, List<MonitorSetting> actual)
        {
            if (settings.ProfileDeactivated || actual == null) return null;
            Profile requested = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
            if (requested != null) return requested.HasLayout && Native.MatchesProtected(requested.Layout, actual) ? requested : null;
            var matches = settings.Profiles.Where(p => p.HasLayout && Native.MatchesProtected(p.Layout, actual)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
    }
    internal static class ProfileStore
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorSwitch");
        public static readonly string FileName = Path.Combine(Folder, "profiles.xml");
        public static Settings Load(string file)
        {
            if (!File.Exists(file)) return Settings.Defaults();
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
            using (XmlReader reader = XmlReader.Create(file, options))
            { var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader); s.Check(); s.Upgrade(); return s; }
        }
        public static void Save(string file, Settings settings)
        {
            settings.Check(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            if (settings.PendingLegacyBackup && File.Exists(file) && !File.Exists(file + ".v1.bak")) File.Copy(file, file + ".v1.bak");
            using (var stream = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { new XmlSerializer(typeof(Settings)).Serialize(stream, settings); stream.Flush(true); }
            if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
            settings.PendingLegacyBackup = false;
        }
    }
}
