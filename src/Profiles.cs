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
        public bool HasLayout { get { return Mode == 0 || CustomResolutions; } }
        public Profile() { Layout = new List<MonitorSetting>(); Hotkey = new Shortcut(); }
        public string Description { get { return ModeLabel(Mode); } }
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
        public Settings() { Version = 1; Profiles = new List<Profile>(); SwitchHotkey = new Shortcut(); ConfirmChanges = true; }
        public static Settings Defaults()
        {
            var s = new Settings();
            string[] names = { "Gioco", "Lavoro", "Due schermi", "Solo portatile" }; int[] modes = { 2, 8, 4, 1 };
            for (int i = 0; i < names.Length; i++) s.Profiles.Add(new Profile { Id = Guid.NewGuid().ToString("N"), Name = names[i], Mode = modes[i], Hotkey = new Shortcut { Modifiers = 3, Key = (uint)(112 + i) } });
            s.SwitchA = s.Profiles[0].Id; s.SwitchB = s.Profiles[1].Id; s.SwitchHotkey = new Shortcut { Modifiers = 3, Key = 121 }; return s;
        }
        public void Check()
        {
            if (Version != 1 || Profiles == null || Profiles.Count < 1 || Profiles.Count > 64 || SwitchHotkey == null)
                throw new InvalidDataException("Archivio profili non valido.");
            var ids = new HashSet<string>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var shortcuts = new HashSet<string>();
            foreach (Profile p in Profiles)
            {
                if (p == null) throw new InvalidDataException("Profilo vuoto."); p.Check();
                if (!ids.Add(p.Id) || !names.Add(p.Name.Trim())) throw new InvalidDataException("Ogni profilo deve avere un nome diverso.");
                AddShortcut(shortcuts, p.Hotkey);
            }
            SwitchHotkey.Check(); AddShortcut(shortcuts, SwitchHotkey);
            if (!ids.Contains(SwitchA) || !ids.Contains(SwitchB)) throw new InvalidDataException("Seleziona i due profili dell'interruttore.");
        }
        private static void AddShortcut(HashSet<string> used, Shortcut key)
        { if (key.Key != 0 && !used.Add(key.Modifiers + ":" + key.Key)) throw new InvalidDataException("Questa combinazione di tasti e gia assegnata a un altro profilo o all'interruttore."); }
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
            { var s = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(reader); s.Check(); return s; }
        }
        public static void Save(string file, Settings settings)
        {
            settings.Check(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            using (var stream = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { new XmlSerializer(typeof(Settings)).Serialize(stream, settings); stream.Flush(true); }
            if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
        }
    }
}
