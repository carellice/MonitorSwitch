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
        public bool Editable;
        public ResolutionRow(MonitorSetting baseline, MonitorInfo monitor, List<string> resolutions)
        {
            Baseline = baseline.Copy(); Height = 82; Width = 694; Margin = new Padding(0, 0, 0, 8); BackColor = Color.FromArgb(34, 43, 60);
            EnabledMonitor = new CheckBox { Left = 10, Top = 19, Width = 24, Height = 28 };
            var name = Theme.Label(baseline.Name, 11, Theme.Text); name.SetBounds(41, 10, 365, 29);
            var role = Theme.Label(monitor == null ? "Non collegato" : (monitor.Internal ? "Schermo del portatile" : "Monitor esterno"), 9, Theme.Muted); role.SetBounds(41, 43, 350, 26);
            Resolution = new ComboBox { Left = 414, Top = 10, Width = 251, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (string value in resolutions) Resolution.Items.Add(value);
            Resolution.Text = baseline.Width + " x " + baseline.Height;
            double hz = baseline.RefreshDen == 0 ? 0 : (double)baseline.RefreshNum / baseline.RefreshDen;
            var frequency = Theme.Label(hz.ToString("0.##") + " Hz · frequenza mantenuta", 9, Theme.Muted); frequency.SetBounds(414, 44, 267, 25);
            Controls.AddRange(new Control[] { EnabledMonitor, name, role, Resolution, frequency });
            EnabledMonitor.CheckedChanged += delegate { Resolution.Enabled = Editable && EnabledMonitor.Checked; };
        }
        public MonitorSetting GetSetting()
        {
            uint width, height; LayoutEditor.Resolution(Resolution.Text, out width, out height);
            MonitorSetting result = Baseline.Copy();
            if (result.Width != width || result.Height != height) result.Scaling = 4;
            result.Width = width; result.Height = height; return result;
        }
    }
    internal sealed class ProfileDialog : Form
    {
        private readonly TextBox name;
        private readonly ComboBox mode;
        private readonly ShortcutEditor shortcut;
        private readonly CheckBox custom;
        private readonly FlowLayoutPanel rowsPanel;
        private readonly List<ResolutionRow> rows = new List<ResolutionRow>();
        private readonly List<MonitorInfo> monitors;
        private readonly List<MonitorSetting> current;
        private readonly string id;
        private List<MonitorSetting> saved;
        private bool syncing;
        public Profile Result { get; private set; }
        public ProfileDialog(Profile existing, bool capture, List<MonitorSetting> currentLayout, List<MonitorInfo> inventory = null)
        {
            Theme.Dialog(this, existing == null ? "Nuovo profilo" : "Modifica profilo", new Size(760, 675));
            Icon = AppAssets.Icon;
            id = existing == null ? Guid.NewGuid().ToString("N") : existing.Id;
            current = currentLayout == null ? new List<MonitorSetting>() : currentLayout.Select(s => s.Copy()).ToList();
            saved = existing == null ? new List<MonitorSetting>() : existing.Layout.Select(s => s.Copy()).ToList();
            if (capture) saved = current.Select(s => s.Copy()).ToList();
            monitors = inventory == null ? new List<MonitorInfo>() : new List<MonitorInfo>(inventory);
            var nameLabel = Theme.Label("Nome del profilo", 10, Theme.Muted); nameLabel.SetBounds(24, 18, 710, 24);
            name = new TextBox { Left = 24, Top = 47, Width = 712, MaxLength = 60, Text = existing == null ? (capture ? "La mia configurazione" : "Nuovo profilo") : existing.Name };
            var modeLabel = Theme.Label("Cosa deve fare", 10, Theme.Muted); modeLabel.SetBounds(24, 89, 710, 24);
            mode = new ComboBox { Left = 24, Top = 118, Width = 712, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (int m in new int[] { 2, 8, 4, 1, 0 }) mode.Items.Add(new ModeChoice { Value = m, Label = Profile.ModeLabel(m) });
            int chosen = capture ? 0 : (existing == null ? 2 : existing.Mode);
            mode.SelectedItem = mode.Items.Cast<ModeChoice>().First(m => m.Value == chosen);
            var keyLabel = Theme.Label("Scorciatoia globale", 10, Theme.Muted); keyLabel.SetBounds(24, 164, 710, 25);
            shortcut = new ShortcutEditor(existing == null ? new Shortcut() : existing.Hotkey.Clone()); shortcut.SetBounds(24, 197, 712, 36);
            custom = new CheckBox { Left = 24, Top = 242, Width = 710, Height = 30, Text = "Personalizza monitor e risoluzioni", ForeColor = Theme.Text,
                Checked = chosen == 0 || (existing != null && existing.CustomResolutions) };
            rowsPanel = new FlowLayoutPanel { Left = 24, Top = 282, Width = 712, Height = 220, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Background };
            var explanation = Theme.Label("Scegli una risoluzione oppure scrivila (es. 1920 x 1080). In duplicazione e comune a tutti gli schermi. Il driver verifica la configurazione prima del cambio.", 9, Theme.Muted);
            explanation.SetBounds(24, 512, 712, 52);
            var replace = Theme.Button("Usa la configurazione attuale", false); replace.SetBounds(24, 569, 333, 36); replace.Enabled = current.Count > 0;
            replace.Click += delegate { saved = current.Select(s => s.Copy()).ToList(); custom.Checked = true; mode.SelectedItem = mode.Items.Cast<ModeChoice>().First(m => m.Value == 0); PopulateRows(); };
            var save = Theme.Button("Salva profilo", true); save.SetBounds(24, 623, 346, 34);
            save.Click += delegate
            {
                try
                {
                    int selectedMode = ((ModeChoice)mode.SelectedItem).Value;
                    var layout = custom.Checked ? LayoutEditor.Build(selectedMode, rows.Where(r => r.EnabledMonitor.Checked).Select(r => r.GetSetting()).ToList()) : new List<MonitorSetting>();
                    var p = new Profile { Id = id, Name = name.Text.Trim(), Mode = selectedMode, CustomResolutions = custom.Checked && selectedMode != 0,
                        Hotkey = shortcut.Value, Layout = layout };
                    p.Check(); Result = p; DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Profilo incompleto", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            var cancel = Theme.Button("Annulla", false); cancel.SetBounds(390, 623, 346, 34); cancel.DialogResult = DialogResult.Cancel; CancelButton = cancel;
            Controls.AddRange(new Control[] { nameLabel, name, modeLabel, mode, keyLabel, shortcut, custom, rowsPanel, replace, explanation, save, cancel });
            mode.SelectedIndexChanged += delegate { if (((ModeChoice)mode.SelectedItem).Value == 0) custom.Checked = true; PopulateRows(); };
            custom.CheckedChanged += delegate
            {
                if (((ModeChoice)mode.SelectedItem).Value == 0 && !custom.Checked) { custom.Checked = true; return; }
                UpdateEnabled();
            };
            PopulateRows(); ActiveControl = name;
        }
        private void PopulateRows()
        {
            syncing = true;
            foreach (ResolutionRow row in rows) row.Dispose(); rows.Clear(); rowsPanel.Controls.Clear();
            int selectedMode = ((ModeChoice)mode.SelectedItem).Value;
            if (selectedMode == 0 && saved.Count == 0) saved = current.Select(s => s.Copy()).ToList();
            var keys = monitors.Select(m => m.Key).Concat(saved.Select(s => s.DevicePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int nextGroup = saved.Count == 0 ? 0 : saved.Max(s => s.Group) + 1;
            int right = saved.Count == 0 ? 0 : saved.Max(s => s.X + (int)s.Width);
            foreach (string key in keys)
            {
                MonitorInfo monitor = monitors.FirstOrDefault(m => String.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
                MonitorSetting stored = saved.FirstOrDefault(s => String.Equals(s.DevicePath, key, StringComparison.OrdinalIgnoreCase));
                MonitorSetting baseline = stored ?? current.FirstOrDefault(s => String.Equals(s.DevicePath, key, StringComparison.OrdinalIgnoreCase)) ?? (monitor == null ? null : monitor.Baseline);
                if (baseline == null) continue; baseline = baseline.Copy();
                if (selectedMode == 0 && stored == null) { baseline.Group = nextGroup++; baseline.X = right; baseline.Y = 0; right += (int)baseline.Width; }
                List<string> options;
                try { options = monitor == null ? new List<string>() : Native.Resolutions(monitor); } catch { options = new List<string>(); }
                if (!options.Contains(baseline.Width + " x " + baseline.Height)) options.Insert(0, baseline.Width + " x " + baseline.Height);
                var row = new ResolutionRow(baseline, monitor, options);
                bool include = selectedMode == 0 ? stored != null : monitor != null &&
                    (selectedMode == 1 ? monitor.Internal : selectedMode == 8 ? !monitor.Internal : true);
                row.EnabledMonitor.Checked = include;
                row.Resolution.TextChanged += delegate { Synchronize(row); };
                rows.Add(row); rowsPanel.Controls.Add(row);
            }
            if (selectedMode == 2 && rows.Count > 0)
            {
                ResolutionRow first = rows.FirstOrDefault(r => r.EnabledMonitor.Checked);
                if (first != null) foreach (ResolutionRow row in rows.Where(r => r.EnabledMonitor.Checked)) row.Resolution.Text = first.Resolution.Text;
            }
            syncing = false; UpdateEnabled();
        }
        private void UpdateEnabled()
        {
            int selectedMode = ((ModeChoice)mode.SelectedItem).Value;
            foreach (ResolutionRow row in rows)
            {
                row.Editable = custom.Checked;
                row.EnabledMonitor.Enabled = custom.Checked && selectedMode == 0;
                row.Resolution.Enabled = custom.Checked && row.EnabledMonitor.Checked;
            }
        }
        private void Synchronize(ResolutionRow source)
        {
            if (syncing) return; int selectedMode = ((ModeChoice)mode.SelectedItem).Value;
            if (selectedMode != 2 && selectedMode != 0) return;
            syncing = true;
            foreach (ResolutionRow row in rows.Where(r => r != source && r.EnabledMonitor.Checked &&
                (selectedMode == 2 || r.Baseline.Group == source.Baseline.Group))) row.Resolution.Text = source.Resolution.Text;
            syncing = false;
        }
    }
}
