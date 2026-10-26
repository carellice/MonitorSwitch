using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorSwitch
{
    internal sealed class ResolutionRow : Panel
    {
        public readonly MonitorSetting Baseline;
        public readonly CheckBox EnabledMonitor;
        public readonly ComboBox Resolution;
        private readonly Label aspect;
        public readonly string DisplayName;
        public ResolutionRow(MonitorSetting baseline, string displayName, bool connected, List<string> resolutions)
        {
            Baseline = baseline.Copy(); DisplayName = displayName; Height = 88; Width = 686; Margin = new Padding(0, 0, 0, 8); BackColor = Color.FromArgb(34, 43, 60);
            EnabledMonitor = new CheckBox { Left = 10, Top = 24, Width = 24, Height = 28 };
            var name = Theme.Label(displayName, 11, Theme.Text); name.SetBounds(41, 12, 345, 29);
            var role = Theme.Label(connected ? "Collegato" : "Scollegato · disponibile quando lo ricolleghi", 9, Theme.Muted); role.SetBounds(41, 47, 360, 28);
            Resolution = new ComboBox { Left = 413, Top = 12, Width = 249, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (string value in resolutions) Resolution.Items.Add(value);
            Resolution.Text = baseline.Width == 0 ? "" : baseline.Width + " x " + baseline.Height;
            aspect = Theme.Label("", 9, Theme.Muted); aspect.SetBounds(413, 47, 263, 28);
            Controls.AddRange(new Control[] { EnabledMonitor, name, role, Resolution, aspect });
            EnabledMonitor.CheckedChanged += delegate { Resolution.Enabled = EnabledMonitor.Checked; };
            Resolution.TextChanged += delegate { UpdateAspect(); }; UpdateAspect();
        }
        private void UpdateAspect()
        {
            uint width, height;
            try { LayoutEditor.Resolution(Resolution.Text, out width, out height); aspect.Text = "Formato " + LayoutEditor.Aspect(width, height) + " · proporzioni mantenute"; }
            catch { aspect.Text = "Scrivi larghezza x altezza"; }
        }
        public MonitorSetting GetSetting()
        {
            uint width, height; LayoutEditor.Resolution(Resolution.Text, out width, out height);
            MonitorSetting result = Baseline.Copy(); result.Scaling = 4; result.Width = width; result.Height = height;
            result.DesktopImage = null;
            if (result.Timing.Active.Width < width || result.Timing.Active.Height < height) result.Timing = new Signal();
            return result;
        }
        public override string ToString() { return DisplayName; }
    }
    internal sealed class ProfileDialog : Form
    {
        private readonly TextBox name;
        private readonly ShortcutEditor shortcut;
        private readonly CheckBox protect;
        private readonly ComboBox primary;
        private readonly FlowLayoutPanel rowsPanel;
        private readonly List<ResolutionRow> rows = new List<ResolutionRow>();
        private readonly string id;
        public Profile Result { get; private set; }
        public ProfileDialog(Profile existing, bool capture, List<MonitorSetting> currentLayout, List<MonitorInfo> inventory = null, List<MonitorRecord> history = null)
        {
            Theme.Dialog(this, existing == null ? "Nuovo profilo" : "Modifica profilo", new Size(760, 742)); Icon = AppAssets.Icon;
            id = existing == null ? Guid.NewGuid().ToString("N") : existing.Id;
            var current = currentLayout ?? new List<MonitorSetting>(); var connected = inventory ?? new List<MonitorInfo>();
            var known = history == null ? new List<MonitorRecord>() : new List<MonitorRecord>(history);
            foreach (MonitorInfo monitor in connected)
                if (!known.Any(m => String.Equals(m.DevicePath, monitor.Key, StringComparison.OrdinalIgnoreCase))) known.Add(new MonitorRecord { DevicePath = monitor.Key, HardwareName = monitor.Name, LastSetting = monitor.Baseline });
            var saved = existing != null && existing.HasLayout && !capture ? existing.Layout : current;
            foreach (MonitorSetting setting in saved)
                if (!known.Any(m => String.Equals(m.DevicePath, setting.DevicePath, StringComparison.OrdinalIgnoreCase))) known.Add(new MonitorRecord { DevicePath = setting.DevicePath, HardwareName = setting.Name, LastSetting = setting });
            var nameLabel = Theme.Label("Nome del profilo", 10, Theme.Muted); nameLabel.SetBounds(24, 16, 712, 24);
            name = new TextBox { Left = 24, Top = 44, Width = 712, MaxLength = 60, Text = existing == null ? "Nuovo profilo" : existing.Name };
            var hint = Theme.Label("Spunta i monitor da accendere e scegli la risoluzione. Gli altri si spengono. Piu monitor selezionati estendono il desktop.", 10, Theme.Muted); hint.SetBounds(24, 86, 712, 46);
            rowsPanel = new FlowLayoutPanel { Left = 24, Top = 143, Width = 712, Height = 265, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Background };
            foreach (MonitorRecord record in known)
            {
                MonitorInfo live = connected.FirstOrDefault(m => String.Equals(m.Key, record.DevicePath, StringComparison.OrdinalIgnoreCase));
                MonitorSetting stored = saved.FirstOrDefault(s => String.Equals(s.DevicePath, record.DevicePath, StringComparison.OrdinalIgnoreCase));
                MonitorSetting baseline = stored ?? (live == null ? record.LastSetting : live.Baseline);
                if (baseline == null) baseline = new MonitorSetting { DevicePath = record.DevicePath, Name = record.HardwareName, Rotation = 1, Scaling = 4, RefreshNum = 60, RefreshDen = 1 };
                var options = new List<string>(record.Resolutions);
                if (options.Count == 0 && live != null) { try { options = Native.Resolutions(live); } catch { } }
                string selectedResolution = baseline.Width + " x " + baseline.Height; if (baseline.Width > 0 && !options.Contains(selectedResolution)) options.Insert(0, selectedResolution);
                var row = new ResolutionRow(baseline, record.DisplayName, live != null, options);
                row.EnabledMonitor.Checked = stored != null;
                // Legacy generic profiles become explicit selections when edited.
                if (existing != null && !existing.HasLayout) row.EnabledMonitor.Checked = live != null && (existing.Mode == 1 ? live.Internal : existing.Mode == 8 ? !live.Internal : true);
                row.Resolution.Enabled = row.EnabledMonitor.Checked; rows.Add(row); rowsPanel.Controls.Add(row);
            }
            if (rows.Count == 0)
            {
                var empty = Theme.Label("Nessun monitor nello storico. Collega uno schermo e aggiorna l'elenco.", 10, Theme.Muted); empty.Size = new Size(660, 60); rowsPanel.Controls.Add(empty);
            }
            var primaryLabel = Theme.Label("Monitor principale", 10, Theme.Muted); primaryLabel.SetBounds(24, 422, 230, 26);
            primary = new ComboBox { Left = 254, Top = 419, Width = 482, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (ResolutionRow row in rows) row.EnabledMonitor.CheckedChanged += delegate { UpdatePrimary(); };
            UpdatePrimary();
            ResolutionRow originalPrimary = rows.FirstOrDefault(r => r.EnabledMonitor.Checked && r.Baseline.X == 0 && r.Baseline.Y == 0); if (originalPrimary != null) primary.SelectedItem = originalPrimary;
            var keyLabel = Theme.Label("Scorciatoia globale", 10, Theme.Muted); keyLabel.SetBounds(24, 466, 230, 27);
            shortcut = new ShortcutEditor(existing == null ? new Shortcut() : existing.Hotkey.Clone()); shortcut.SetBounds(254, 462, 482, 36);
            protect = new CheckBox { Left = 24, Top = 512, Width = 712, Height = 29, Text = "Proteggi la risoluzione mentre il profilo e attivo", ForeColor = Theme.Text, Checked = existing == null || existing.ProtectResolution };
            var explanation = Theme.Label("Il formato deriva dalla risoluzione; il driver deve mantenere le proporzioni. Nei giochi usa Finestra senza bordi / Borderless e la risoluzione del profilo. Il fullscreen esclusivo puo imporre modalita proprie; la protezione ripristina i cambi rilevati finche l'app resta aperta.", 9, Theme.Muted); explanation.SetBounds(24, 548, 712, 78);
            var save = Theme.Button("Salva profilo", true); save.SetBounds(24, 681, 346, 37);
            save.Click += delegate
            {
                try
                {
                    ResolutionRow main = primary.SelectedItem as ResolutionRow;
                    var selected = rows.Where(r => r.EnabledMonitor.Checked).OrderBy(r => r == main ? 0 : 1).Select(r => r.GetSetting()).ToList();
                    var p = new Profile { Id = id, Name = name.Text.Trim(), Mode = 0, Layout = LayoutEditor.Build(4, selected), Hotkey = shortcut.Value, ProtectResolution = protect.Checked };
                    p.Check(); Result = p; DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Profilo incompleto", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            var cancel = Theme.Button("Annulla", false); cancel.SetBounds(390, 681, 346, 37); cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            Controls.AddRange(new Control[] { nameLabel, name, hint, rowsPanel, primaryLabel, primary, keyLabel, shortcut, protect, explanation, save, cancel }); ActiveControl = name;
        }
        private void UpdatePrimary()
        {
            object selected = primary.SelectedItem; primary.Items.Clear();
            foreach (ResolutionRow row in rows.Where(r => r.EnabledMonitor.Checked)) primary.Items.Add(row);
            if (selected != null && primary.Items.Contains(selected)) primary.SelectedItem = selected;
            else if (primary.Items.Count > 0) primary.SelectedIndex = 0;
        }
    }
    internal sealed class RenameMonitorDialog : Form
    {
        private readonly TextBox alias;
        public string Alias { get { return alias.Text.Trim(); } }
        public RenameMonitorDialog(MonitorRecord monitor)
        {
            Theme.Dialog(this, "Rinomina monitor", new Size(520, 226)); Icon = AppAssets.Icon;
            var label = Theme.Label("Nome per ricordarlo (es. TV salotto o Monitor scrivania)", 10, Theme.Muted); label.SetBounds(22, 17, 476, 28);
            alias = new TextBox { Left = 22, Top = 56, Width = 476, MaxLength = 60, Text = monitor.Alias ?? "" };
            var model = Theme.Label("Modello: " + monitor.HardwareName + "\r\nLascia vuoto per usare il nome originale.", 9, Theme.Muted); model.SetBounds(22, 98, 476, 50);
            var save = Theme.Button("Salva nome", true); save.SetBounds(22, 170, 228, 36); save.DialogResult = DialogResult.OK;
            var cancel = Theme.Button("Annulla", false); cancel.SetBounds(270, 170, 228, 36); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { label, alias, model, save, cancel }); AcceptButton = save; CancelButton = cancel; ActiveControl = alias;
        }
    }
}
