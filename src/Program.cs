using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Globalization;
using System.Windows.Forms;

namespace MonitorSwitch
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread] private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--apply-update") return Updates.Apply(args[1]);
            if (args.Length >= 2 && args[0] == "--updated") { Updates.StartupReceipt = args[1]; Updates.RestartInTray = args.Contains("--tray"); }
            if (args.Length == 3 && args[0] == "--guard") return Recovery.Guard(Int32.Parse(args[1]), args[2]);
            if (args.Length == 2 && args[0] == "--self-test") return Tests.Run(args[1]);
            if (args.Length == 2 && args[0] == "--test-update-download")
            {
                try
                {
                    UpdateInfo release = Updates.CheckAsync(CancellationToken.None, new Version(0, 0, 0, 0)).GetAwaiter().GetResult();
                    if (release == null) throw new InvalidDataException("Nessuna release disponibile per la verifica.");
                    string payload = Updates.DownloadAsync(release, null, CancellationToken.None, Path.GetDirectoryName(Path.GetFullPath(args[1]))).GetAwaiter().GetResult();
                    File.WriteAllText(args[1], "PASS: GitHub API, download HTTPS, SHA-256, PE x64 e versione " + release.Version + ". Nessun file installato o eseguito.");
                    File.Delete(payload); Directory.Delete(Path.GetDirectoryName(payload)); return 0;
                }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); return 1; }
            }
            if (args.Length == 2 && (args[0] == "--diagnose" || args[0] == "--validate-current" || args[0] == "--validate-editor"))
            {
                try
                {
                    DisplayState state = Native.Read(); var monitors = Native.Inventory(state);
                    string report = "MonitorSwitch " + Updates.CurrentLabel + "\r\nModalita: " + Profile.ModeLabel(state.Topology()) + "\r\n" +
                        String.Join("\r\n", monitors.Select(m => m.Name + " | " + m.Summary + " | " + m.Adapter + " | " + m.Key + "\r\nRisoluzioni: " + String.Join(", ", Native.Resolutions(m))));
                    if (args[0] == "--validate-current" || args[0] == "--validate-editor")
                    {
                        File.WriteAllText(args[1], report + "\r\nVerifica in corso...");
                        Native.Validate(state);
                        File.AppendAllText(args[1], "\r\nConfigurazione nativa: valida.");
                        Native.Plan(Native.BuildPlan(state.Capture(monitors), monitors), true);
                        report += "\r\nProfilo catturato: validazione driver riuscita. Nessuna modifica applicata.";
                    }
                    if (args[0] == "--validate-editor")
                    {
                        var extended = monitors.Select(m => m.Baseline.Copy()).ToList();
                        for (int i = 0; i < extended.Count; i++)
                        {
                            if (monitors[i].PreferredWidth > 0) { extended[i].Width = monitors[i].PreferredWidth; extended[i].Height = monitors[i].PreferredHeight; extended[i].Scaling = 4; }
                        }
                        Native.Plan(Native.BuildPlan(LayoutEditor.Build(4, extended), monitors), true);
                        report += "\r\nEditor: estensione con risoluzioni native distinte validata senza applicare modifiche.";
                        var clone = monitors.Select(m => m.Baseline.Copy()).ToList();
                        foreach (var setting in clone) { setting.Width = 1920; setting.Height = 1080; setting.Scaling = 4; }
                        Native.Plan(Native.BuildPlan(LayoutEditor.Build(2, clone), monitors), true);
                        report += "\r\nEditor: duplicazione 1920 x 1080 validata senza applicare modifiche.";
                    }
                    File.WriteAllText(args[1], report);
                    return 0;
                }
                catch (Exception ex) { File.AppendAllText(args[1], "\r\n" + ex); return 1; }
            }
            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--render-preview")
            {
                using (var form = new MainForm(true))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                    form.Show(); Application.DoEvents();
                    using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height)); bmp.Save(args[1]); }
                }
                return 0;
            }
            if (args.Length == 2 && args[0] == "--render-dialogs")
            {
                Directory.CreateDirectory(args[1]);
                var layout = new List<MonitorSetting> {
                    new MonitorSetting { DevicePath = "panel", Name = "Schermo del portatile", Group = 0, Width = 1920, Height = 1080, Rotation = 1, Scaling = 4, RefreshNum = 144, RefreshDen = 1 },
                    new MonitorSetting { DevicePath = "external", Name = "ZQE-CAA", Group = 0, Width = 1920, Height = 1080, Rotation = 1, Scaling = 4, RefreshNum = 165, RefreshDen = 1 } };
                var inventory = layout.Select(s => new MonitorInfo { Key = s.DevicePath, Name = s.Name, Internal = s.DevicePath == "panel", Baseline = s }).ToList();
                var sample = new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Gioco", Mode = 2, CustomResolutions = true, Layout = layout };
                using (var form = new ProfileDialog(sample, false, layout, inventory)) Render(form, Path.Combine(args[1], "profile-dialog.png"));
                using (var form = new ShortcutDialog(new Shortcut { Modifiers = 3, Key = 121 })) Render(form, Path.Combine(args[1], "shortcut-dialog.png"));
                using (var form = new ConfirmDialog("Gioco", Stopwatch.StartNew())) Render(form, Path.Combine(args[1], "confirmation-dialog.png"));
                using (var form = new UpdateDialog(new UpdateInfo { Version = new Version(2, 3, 0, 0), Notes = "Anteprima della finestra aggiornamenti." }, true)) Render(form, Path.Combine(args[1], "update-dialog.png"));
                return 0;
            }
            bool first;
            // Coordinate with the old Full HD app as well: only one display controller.
            using (var instance = new Mutex(true, @"Local\FullHdSwitch.App", out first))
            {
                if (!first) { MessageBox.Show("Monitor Switch o la vecchia app Full HD e gia aperta. Chiudila dall'icona vicino all'orologio e riprova.", "Monitor Switch"); return 0; }
                try
                {
                    if (File.Exists(Recovery.FileName)) Recovery.Restore();
                    if (args.Length == 1 && args[0] == "--restore") return 0;
                    Application.Run(new MainForm(false)); return 0;
                }
                catch (Exception ex) { Recovery.Log(ex); MessageBox.Show(ex.Message + "\r\n\r\nDati e log: " + ProfileStore.Folder, "Monitor Switch", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { instance.ReleaseMutex(); }
            }
        }
        private static void Render(Form form, string path)
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(path); }
        }
    }
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(17, 22, 32), Card = Color.FromArgb(27, 34, 48),
            Text = Color.FromArgb(239, 242, 250), Muted = Color.FromArgb(166, 179, 200), Accent = Color.FromArgb(119, 100, 245);
        public static Button Button(string text, bool accent)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = accent ? Accent : Color.FromArgb(44, 54, 74),
                ForeColor = Text, Font = new Font("Segoe UI", 10, accent ? FontStyle.Bold : FontStyle.Regular), Cursor = Cursors.Hand, Height = 36, Margin = new Padding(4) };
            b.FlatAppearance.BorderSize = 0; return b;
        }
        public static Label Label(string text, float size, Color color)
        { return new Label { Text = text, Font = new Font("Segoe UI", size), ForeColor = color, AutoSize = false }; }
        public static void Dialog(Form form, string title, Size size)
        {
            form.Text = title; form.ClientSize = size; form.StartPosition = FormStartPosition.CenterParent;
            form.Font = new Font("Segoe UI", 10); form.BackColor = Card; form.ForeColor = Text;
            form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MaximizeBox = false; form.MinimizeBox = false;
            form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly bool preview;
        private readonly Settings settings;
        private DisplayState state;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private readonly ListView profileList;
        private readonly DataGridView monitorGrid;
        private readonly Label status, currentMode, selectedDescription, switchHint;
        private readonly ComboBox pairA, pairB;
        private readonly Button switchButton;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly Dictionary<int, string> hotkeys = new Dictionary<int, string>();
        private bool loading, changing, updating;
        private UpdateInfo availableUpdate;
        private readonly CancellationTokenSource updateCancellation = new CancellationTokenSource();
        private ConfirmDialog confirmation;
        private Guardian guardian;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        public MainForm(bool isPreview)
        {
            preview = isPreview; bool firstLaunch = !preview && !File.Exists(ProfileStore.FileName);
            settings = preview ? Settings.Defaults() : ProfileStore.Load(ProfileStore.FileName);
            Text = "Monitor Switch"; ClientSize = new Size(1040, 738); MinimumSize = new Size(960, 740);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text; Icon = AppAssets.Icon;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 12), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); Controls.Add(root);
            var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 12) };
            var title = Theme.Label("Monitor Switch", 25, Theme.Text); title.AutoSize = true; title.Margin = Padding.Empty;
            var subtitle = Theme.Label("Una configurazione per giocare. Un'altra per tutto il resto.", 10, Theme.Muted); subtitle.AutoSize = true; subtitle.Margin = new Padding(2, 4, 0, 0);
            heading.Controls.AddRange(new Control[] { title, subtitle }); root.Controls.Add(heading, 0, 0);
            var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            string[] labels = { "Duplica", "Estendi", "Solo portatile", "Solo esterni" }; int[] values = { 2, 4, 1, 8 };
            for (int i = 0; i < labels.Length; i++)
            {
                int mode = values[i]; var b = Theme.Button(labels[i], false); b.Width = 139;
                b.Click += delegate { ApplyProfile(new Profile { Id = Guid.NewGuid().ToString("N"), Name = Profile.ModeLabel(mode), Mode = mode }); }; quick.Controls.Add(b);
            }
            var windows = Theme.Button("Impostazioni Windows ↗", false); windows.Width = 217; windows.Click += delegate { OpenSettings(); }; quick.Controls.Add(windows);
            root.Controls.Add(quick, 0, 1);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 2);
            var profiles = new TableLayoutPanel { BackColor = Theme.Card, Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
            foreach (float height in new float[] { 32, 0, 43, 43, 43, 40 }) profiles.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            var profileTitle = Theme.Label("I tuoi profili", 14, Theme.Text); profileTitle.Dock = DockStyle.Fill; profiles.Controls.Add(profileTitle, 0, 0);
            profileList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill,
                BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            profileList.Columns.Add("Profilo", 164); profileList.Columns.Add("Scorciatoia", 116);
            profileList.SelectedIndexChanged += delegate { UpdateSelected(); }; profileList.DoubleClick += delegate { ApplySelected(); }; profiles.Controls.Add(profileList, 0, 1);
            selectedDescription = Theme.Label("Seleziona un profilo.", 9, Theme.Muted); selectedDescription.Dock = DockStyle.Fill; selectedDescription.Padding = new Padding(0, 7, 0, 0); profiles.Controls.Add(selectedDescription, 0, 2);
            var apply = Theme.Button("Applica profilo selezionato", true); apply.Dock = DockStyle.Fill; apply.Margin = new Padding(0, 3, 0, 3); apply.Click += delegate { ApplySelected(); }; profiles.Controls.Add(apply, 0, 3);
            var editRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            var add = Theme.Button("Nuovo", false); add.Width = 88; add.Click += delegate { EditProfile(false, false); };
            var edit = Theme.Button("Modifica", false); edit.Width = 92; edit.Click += delegate { EditProfile(true, false); };
            var remove = Theme.Button("Elimina", false); remove.Width = 88; remove.Click += delegate { DeleteProfile(); };
            editRow.Controls.AddRange(new Control[] { add, edit, remove }); profiles.Controls.Add(editRow, 0, 4);
            var capture = Theme.Button("＋ Salva configurazione attuale", false); capture.Font = new Font("Segoe UI", 9); capture.Dock = DockStyle.Fill; capture.Margin = new Padding(0, 3, 0, 0);
            capture.Click += delegate { EditProfile(false, true); }; profiles.Controls.Add(capture, 0, 5); body.Controls.Add(profiles, 0, 0);
            var displayPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16), ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            var displayTitle = Theme.Label("Monitor riconosciuti", 14, Theme.Text); displayTitle.Dock = DockStyle.Fill; displayPanel.Controls.Add(displayTitle, 0, 0);
            currentMode = Theme.Label("Rilevamento monitor…", 10, Theme.Muted); currentMode.Dock = DockStyle.Fill; displayPanel.Controls.Add(currentMode, 0, 1);
            monitorGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(48, 58, 78), EnableHeadersVisualStyles = false };
            monitorGrid.DefaultCellStyle.BackColor = Theme.Card; monitorGrid.DefaultCellStyle.ForeColor = Theme.Text;
            monitorGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 58, 87); monitorGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            monitorGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; monitorGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            monitorGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card; monitorGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            monitorGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9); monitorGrid.ColumnHeadersHeight = 32; monitorGrid.RowTemplate.Height = 70;
            monitorGrid.Columns.Add("monitor", "Monitor"); monitorGrid.Columns.Add("state", "Stato / risoluzione"); monitorGrid.Columns.Add("gpu", "GPU uscita");
            monitorGrid.Columns[0].FillWeight = 95; monitorGrid.Columns[1].FillWeight = 135; monitorGrid.Columns[2].FillWeight = 100; displayPanel.Controls.Add(monitorGrid, 0, 2);
            var displayFooter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            var refresh = Theme.Button("Aggiorna monitor", false); refresh.Width = 158; refresh.Click += delegate { RefreshDisplays(true); };
            var help = Theme.Button("Come funziona", false); help.Width = 151; help.Click += delegate { ShowHelp(); };
            var updates = Theme.Button("Aggiornamenti", false); updates.Width = 150; updates.Click += delegate { ShowUpdates(); };
            displayFooter.Controls.AddRange(new Control[] { refresh, help, updates }); displayPanel.Controls.Add(displayFooter, 0, 3); body.Controls.Add(displayPanel, 2, 0);
            var switchCard = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16, 10, 16, 10), ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            switchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); switchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
            switchCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); switchCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); switchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var switchTitle = Theme.Label("Il tuo interruttore", 14, Theme.Text); switchTitle.Dock = DockStyle.Fill; switchCard.Controls.Add(switchTitle, 0, 0);
            var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pairA = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 0) };
            pairB = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 18, 0) };
            var arrow = Theme.Label("⇄", 17, Theme.Muted); arrow.Dock = DockStyle.Fill; arrow.TextAlign = ContentAlignment.TopCenter;
            pair.Controls.Add(pairA, 0, 0); pair.Controls.Add(arrow, 1, 0); pair.Controls.Add(pairB, 2, 0); switchCard.Controls.Add(pair, 0, 1);
            pairA.SelectedIndexChanged += PairChanged; pairB.SelectedIndexChanged += PairChanged;
            switchHint = Theme.Label("", 9, Theme.Muted); switchHint.Dock = DockStyle.Fill; switchCard.Controls.Add(switchHint, 0, 2);
            switchButton = Theme.Button("Cambia configurazione", true); switchButton.Dock = DockStyle.Fill; switchButton.Margin = new Padding(4, 2, 0, 2); switchButton.Click += delegate { TogglePair(); };
            switchCard.Controls.Add(switchButton, 1, 0); switchCard.SetRowSpan(switchButton, 2);
            var shortcut = Theme.Button("Personalizza tasto rapido", false); shortcut.Font = new Font("Segoe UI", 9); shortcut.Dock = DockStyle.Fill; shortcut.Margin = new Padding(4, 2, 0, 0);
            shortcut.Click += delegate { EditSwitchKey(); }; switchCard.Controls.Add(shortcut, 1, 2); root.Controls.Add(switchCard, 0, 4);
            var bottom = new Panel { Dock = DockStyle.Fill };
            status = Theme.Label("Pronto. Le modifiche confermate restano attive anche chiudendo l'app.", 9, Theme.Muted); status.Dock = DockStyle.Fill; status.Padding = new Padding(0, 10, 0, 0);
            var ask = new CheckBox { Text = "Conferma dopo il cambio", Checked = settings.ConfirmChanges, Dock = DockStyle.Right, Width = 235, Font = new Font("Segoe UI", 9), ForeColor = Theme.Muted };
            ask.CheckedChanged += delegate { settings.ConfirmChanges = ask.Checked; if (!preview) SaveSettings(); status.Text = ask.Checked ? "Conferma entro 20 secondi; senza conferma il cambio viene annullato." : "Cambio immediato attivo: le configurazioni riuscite vengono mantenute subito."; };
            bottom.Controls.Add(status); bottom.Controls.Add(ask); root.Controls.Add(bottom, 0, 5);
            tray = new NotifyIcon { Text = "Monitor Switch", Icon = AppAssets.TrayIcon, Visible = !preview }; tray.DoubleClick += delegate { ShowWindow(); };
            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 }; refreshTimer.Tick += delegate { refreshTimer.Stop(); if (!changing) RefreshDisplays(false); };
            Resize += delegate { if (!preview && WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (changing) { e.Cancel = true; if (confirmation != null) confirmation.CancelChange(); } };
            RebuildProfiles(null);
            if (preview) PreviewMonitors(); else RefreshDisplays(false);
            if (firstLaunch)
            {
                // Preserve the user's already working gaming setup on first launch.
                if (state != null && state.Topology() == 2)
                {
                    try
                    {
                        settings.Profiles[0].Layout = state.Capture(monitors); settings.Profiles[0].Mode = 0;
                        RebuildProfiles(settings.Profiles[0].Id); status.Text = "Gioco acquisito dalla duplicazione attuale. Lavoro passa al monitor esterno.";
                    }
                    catch (Exception ex) { Recovery.Log(ex); }
                }
                ProfileStore.Save(ProfileStore.FileName, settings);
            }
        }
        private Profile Selected { get { return profileList.SelectedItems.Count == 0 ? null : (Profile)profileList.SelectedItems[0].Tag; } }
        private void RebuildProfiles(string selected)
        {
            loading = true; profileList.Items.Clear(); pairA.Items.Clear(); pairB.Items.Clear();
            foreach (Profile p in settings.Profiles)
            {
                var item = new ListViewItem(new string[] { p.Name, p.Hotkey.Label }) { Tag = p }; profileList.Items.Add(item);
                pairA.Items.Add(p); pairB.Items.Add(p); if (p.Id == settings.SwitchA) pairA.SelectedItem = p; if (p.Id == settings.SwitchB) pairB.SelectedItem = p;
                if (p.Id == selected) item.Selected = true;
            }
            if (profileList.SelectedItems.Count == 0 && profileList.Items.Count > 0) profileList.Items[0].Selected = true;
            loading = false; UpdateSelected(); UpdateSwitch(); RebuildTray();
            if (IsHandleCreated && !preview) RegisterShortcuts();
        }
        private void UpdateSelected()
        { Profile p = Selected; selectedDescription.Text = p == null ? "Seleziona un profilo." : p.Description + (p.HasLayout ? " · " + p.Layout.Count + " monitor" : ""); }
        private void RebuildTray()
        {
            ContextMenuStrip old = tray.ContextMenuStrip; var menu = new ContextMenuStrip();
            menu.Items.Add("Apri Monitor Switch", null, delegate { ShowWindow(); });
            menu.Items.Add("Interruttore: " + settings.SwitchHotkey.Label, null, delegate { TogglePair(true); }); menu.Items.Add(new ToolStripSeparator());
            foreach (Profile profile in settings.Profiles) { Profile p = profile; menu.Items.Add(p.Name + "  (" + p.Hotkey.Label + ")", null, delegate { ApplyProfile(p, true); }); }
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Esci", null, delegate { Close(); }); tray.ContextMenuStrip = menu; if (old != null) old.Dispose();
            menu.Items.Insert(menu.Items.Count - 1, new ToolStripMenuItem("Aggiornamenti…", null, delegate { ShowUpdates(); }));
            var automatic = new ToolStripMenuItem("Controlla aggiornamenti all'avvio") { Checked = settings.CheckUpdatesOnStartup, CheckOnClick = true };
            automatic.CheckedChanged += delegate { settings.CheckUpdatesOnStartup = automatic.Checked; SaveSettings(); };
            menu.Items.Insert(menu.Items.Count - 1, automatic);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!preview) { RegisterShortcuts(); Updates.SignalStarted(); if (Updates.RestartInTray) StayInTray(); CheckUpdatesAtStartup(); }
        }
        private async void CheckUpdatesAtStartup()
        {
            DateTime previous;
            if (!settings.CheckUpdatesOnStartup || (DateTime.TryParse(settings.LastUpdateCheckUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out previous) && previous.ToUniversalTime() <= DateTime.UtcNow && DateTime.UtcNow - previous.ToUniversalTime() < TimeSpan.FromDays(1))) return;
            try
            {
                UpdateInfo result = await Updates.CheckAsync(updateCancellation.Token);
                if (IsDisposed) return;
                settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); SaveSettings(); availableUpdate = result;
                if (result != null)
                {
                    status.Text = "Disponibile MonitorSwitch " + result.Version.ToString(3) + ". Premi Aggiornamenti per installarlo.";
                    tray.ShowBalloonTip(5000, "Aggiornamento disponibile", "MonitorSwitch " + result.Version.ToString(3) + " e disponibile. Apri Aggiornamenti dal menu dell'icona.", ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { if (!updateCancellation.IsCancellationRequested) Recovery.Log(ex); }
        }
        private async void ShowUpdates()
        {
            if (preview || updating || changing) return; updating = true;
            bool inTray = !Visible || WindowState == FormWindowState.Minimized;
            try
            {
                using (var dialog = new UpdateDialog(availableUpdate))
                {
                    DialogResult result = inTray ? dialog.ShowDialog() : dialog.ShowDialog(this);
                    if (result != DialogResult.OK || String.IsNullOrEmpty(dialog.Package)) return;
                    status.Text = "Riavvio per completare l'aggiornamento…";
                    await Updates.LaunchAsync(dialog.Package, inTray);
                    updating = false; Close();
                }
            }
            catch (Exception ex) { ShowError(ex); }
            finally { updating = false; }
        }
        private void RegisterShortcuts()
        {
            foreach (int id in hotkeys.Keys) UnregisterHotKey(Handle, id); hotkeys.Clear(); var occupied = new List<string>(); int keyId = 1;
            foreach (Profile p in settings.Profiles) RegisterShortcut(keyId++, p.Id, p.Hotkey, occupied);
            RegisterShortcut(keyId, "switch", settings.SwitchHotkey, occupied);
            if (occupied.Count != 0) status.Text = "Scorciatoie occupate: " + String.Join(", ", occupied) + ". Puoi cambiarle o usare i pulsanti.";
        }
        private void RegisterShortcut(int id, string value, Shortcut key, List<string> occupied)
        {
            if (key.Key == 0) return;
            if (RegisterHotKey(Handle, id, key.Modifiers | 0x4000, key.Key)) hotkeys.Add(id, value); else occupied.Add(key.Label);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && !changing)
            {
                string value;
                if (hotkeys.TryGetValue(m.WParam.ToInt32(), out value))
                { if (value == "switch") TogglePair(true); else { Profile p = settings.Profiles.FirstOrDefault(x => x.Id == value); if (p != null) ApplyProfile(p, true); } }
            }
            if ((m.Msg == 0x7E || m.Msg == 0x219) && refreshTimer != null && !preview && !changing) { refreshTimer.Stop(); refreshTimer.Start(); }
            base.WndProc(ref m);
        }
        private void PreviewMonitors()
        {
            currentMode.Text = "Attuale: solo monitor esterno";
            monitorGrid.Rows.Add("Schermo portatile", "Portatile\r\nCollegato, non attivo", "Intel(R) Graphics");
            monitorGrid.Rows.Add("ZQE-CAA", "Esterno · attivo\r\n3440 × 1440 · 120 Hz", "Intel(R) Graphics");
        }
        private void RefreshDisplays(bool report)
        {
            if (preview || changing) return;
            try
            {
                state = Native.Read(); monitors = Native.Inventory(state); monitorGrid.Rows.Clear();
                foreach (MonitorInfo m in monitors) monitorGrid.Rows.Add(m.Name, m.Summary, m.Adapter);
                currentMode.Text = "Attuale: " + Profile.ModeLabel(state.Topology()).ToLowerInvariant(); UpdateSwitch();
                if (report) status.Text = monitors.Count + " monitor riconosciuti. Puoi applicare o salvare un profilo.";
            }
            catch (Exception ex) { state = null; currentMode.Text = "Monitor non disponibili"; status.Text = ex.Message; Recovery.Log(ex); }
        }
        private bool Matches(Profile p)
        {
            if (state == null || p == null) return false;
            if (!p.HasLayout && p.Mode == 8) return state.Paths.All(path => !Native.IsInternal(path.Target.Technology));
            if (!p.HasLayout) return state.Topology() == p.Mode;
            try { return Native.Matches(p.Layout, state.Capture(monitors)); } catch { return false; }
        }
        private void UpdateSwitch()
        {
            if (switchButton == null) return;
            Profile a = pairA.SelectedItem as Profile, b = pairB.SelectedItem as Profile;
            bool valid = a != null && b != null && a.Id != b.Id;
            switchButton.Enabled = valid && !changing;
            Profile next = valid ? (Matches(a) ? b : a) : null;
            switchButton.Text = next == null ? "Scegli due profili diversi" : "Passa a " + (next.Name.Length > 27 ? next.Name.Substring(0, 25) + "…" : next.Name);
            switchHint.Text = settings.SwitchHotkey.Label + "  ·  Alterna i due profili scelti";
        }
        private void PairChanged(object sender, EventArgs e)
        {
            if (loading) return; var a = pairA.SelectedItem as Profile; var b = pairB.SelectedItem as Profile;
            if (a == null || b == null) return; settings.SwitchA = a.Id; settings.SwitchB = b.Id;
            SaveSettings(); UpdateSwitch(); RebuildTray();
        }
        private void TogglePair(bool background = false)
        {
            if (changing || updating || preview) return; RefreshDisplays(false);
            Profile a = pairA.SelectedItem as Profile, b = pairB.SelectedItem as Profile;
            if (a == null || b == null || a.Id == b.Id) { status.Text = "Scegli due profili diversi per l'interruttore."; return; }
            ApplyProfile(Matches(a) ? b : a, background);
        }
        private void ApplySelected() { if (Selected != null) ApplyProfile(Selected); }
        private void ApplyProfile(Profile profile, bool background = false)
        {
            if (preview || changing || updating) return;
            if (background) StayInTray();
            try
            {
                profile.Check(); RefreshDisplays(false); if (state == null) throw new InvalidOperationException("Impossibile leggere la configurazione attuale.");
                DisplayPlan plan = profile.HasLayout ? Native.BuildPlan(profile.Layout, monitors) : null;
                if (plan != null) Native.Plan(plan, true);
                changing = true; UpdateSwitch(); status.Text = "Applicazione di " + profile.Name + "…"; status.Refresh();
                Recovery.Locked(delegate { Snapshot.Save(Recovery.FileName, state); }); guardian = new Guardian();
                Stopwatch deadline = Stopwatch.StartNew(); guardian.Arm();
                Recovery.Locked(delegate { if (plan == null) Native.ApplyTopology(profile.Mode); else Native.Plan(plan, false); });
                DisplayState actual = Native.Read(); List<MonitorInfo> inventory = Native.Inventory(actual);
                if (plan != null && !Native.Matches(profile.Layout, actual.Capture(inventory)))
                    throw new InvalidOperationException("Windows ha applicato impostazioni diverse dal profilo salvato. La configurazione precedente verra ripristinata.");
                if (!background) ShowWindow();
                if (settings.ConfirmChanges)
                {
                    using (var dialog = new ConfirmDialog(profile.Name, deadline))
                    {
                        if (background) { dialog.StartPosition = FormStartPosition.CenterScreen; dialog.ShowInTaskbar = false; }
                        confirmation = dialog; DialogResult answer = background ? dialog.ShowDialog() : dialog.ShowDialog(this); confirmation = null;
                        if (answer != DialogResult.OK || deadline.ElapsedMilliseconds >= 20000 || guardian.HasExited)
                        { Recovery.Restore(); status.Text = "Cambio annullato. Configurazione precedente ripristinata."; return; }
                    }
                }
                else if (deadline.ElapsedMilliseconds >= 25000 || guardian.HasExited) throw new InvalidOperationException("Il cambio ha richiesto troppo tempo. Riprova.");
                Recovery.Commit(actual); status.Text = "Profilo " + profile.Name + " applicato. Rimarra attivo anche chiudendo l'app.";
            }
            catch (Exception ex)
            {
                Recovery.Log(ex); string message = ex.Message;
                try { if (File.Exists(Recovery.FileName)) Recovery.Restore(); }
                catch (Exception restoreError) { Recovery.Log(restoreError); message += "\r\nRipristino non riuscito: " + restoreError.Message + "\r\nRicollega i monitor originali e riapri l'app."; }
                if (!background) ShowWindow(); MessageBox.Show(background ? null : this, message, "Cambio monitor non riuscito", MessageBoxButtons.OK, MessageBoxIcon.Warning); status.Text = "Cambio non completato. Dettagli nel registro errori.";
            }
            finally
            {
                confirmation = null; if (guardian != null) { guardian.Dispose(); guardian = null; }
                changing = false; RefreshDisplays(false); UpdateSwitch();
                if (background) StayInTray();
            }
        }
        private void EditProfile(bool editing, bool capture)
        {
            if (changing || preview) return;
            Profile existing = editing ? Selected : null; if (editing && existing == null) return;
            if (!editing && settings.Profiles.Count >= 64) { status.Text = "Puoi salvare fino a 64 profili."; return; }
            RefreshDisplays(false);
            List<MonitorSetting> layout = null;
            try { if (state != null) layout = state.Capture(monitors); } catch (Exception ex) { Recovery.Log(ex); }
            using (var editor = new ProfileDialog(existing, capture, layout, monitors))
            {
                while (editor.ShowDialog(this) == DialogResult.OK)
                {
                    Profile result = editor.Result;
                    int index = existing == null ? settings.Profiles.Count : settings.Profiles.IndexOf(existing);
                    if (existing == null) settings.Profiles.Add(result); else settings.Profiles[index] = result;
                    try { settings.Check(); ProfileStore.Save(ProfileStore.FileName, settings); RebuildProfiles(result.Id); status.Text = "Profilo salvato."; return; }
                    catch (Exception ex)
                    {
                        if (existing == null) settings.Profiles.RemoveAt(index); else settings.Profiles[index] = existing;
                        MessageBox.Show(this, ex.Message, "Profilo non salvato", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }
        private void DeleteProfile()
        {
            Profile p = Selected; if (p == null || changing || preview) return;
            if (settings.Profiles.Count == 1) { status.Text = "Mantieni almeno un profilo."; return; }
            if (MessageBox.Show(this, "Eliminare il profilo " + p.Name + "?", "Elimina profilo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int index = settings.Profiles.IndexOf(p); string a = settings.SwitchA, b = settings.SwitchB;
            settings.Profiles.Remove(p); if (settings.SwitchA == p.Id) settings.SwitchA = settings.Profiles[0].Id;
            if (settings.SwitchB == p.Id) settings.SwitchB = settings.Profiles.FirstOrDefault(x => x.Id != settings.SwitchA) != null ? settings.Profiles.First(x => x.Id != settings.SwitchA).Id : settings.SwitchA;
            try { ProfileStore.Save(ProfileStore.FileName, settings); RebuildProfiles(null); status.Text = "Profilo eliminato."; }
            catch (Exception ex) { settings.Profiles.Insert(index, p); settings.SwitchA = a; settings.SwitchB = b; ShowError(ex); }
        }
        private void EditSwitchKey()
        {
            if (preview || changing) return;
            using (var dialog = new ShortcutDialog(settings.SwitchHotkey))
            {
                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Shortcut old = settings.SwitchHotkey; settings.SwitchHotkey = dialog.Result;
                    try { settings.Check(); ProfileStore.Save(ProfileStore.FileName, settings); RegisterShortcuts(); UpdateSwitch(); RebuildTray(); return; }
                    catch (Exception ex) { settings.SwitchHotkey = old; ShowError(ex); }
                }
            }
        }
        private void SaveSettings() { try { ProfileStore.Save(ProfileStore.FileName, settings); } catch (Exception ex) { ShowError(ex); } }
        private void ShowError(Exception ex) { Recovery.Log(ex); MessageBox.Show(this, ex.Message, "Monitor Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void ShowWindow()
        {
            Show(); WindowState = FormWindowState.Normal;
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds)))
            { Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2)); }
            Activate();
        }
        internal void StayInTray() { Hide(); WindowState = FormWindowState.Minimized; }
        private void OpenSettings() { if (!preview) { try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } } }
        private void ShowHelp()
        {
            MessageBox.Show(this, "I pulsanti rapidi funzionano come le modalita Windows Win+P.\r\n\r\nGioco duplica lo schermo del portatile; Lavoro lascia attivo solo il monitor esterno. Windows sceglie una risoluzione compatibile nella duplicazione.\r\n\r\nPer scegliere le risoluzioni: apri Modifica, attiva Personalizza monitor e risoluzioni e scegli o scrivi le dimensioni per ogni schermo. In duplicazione la risoluzione e comune. Per frequenze, orientamento e disposizione puoi ancora usare Windows e Salva configurazione attuale.\r\n\r\nI tasti rapidi e il menu del tray cambiano profilo senza aprire la finestra principale. Con la conferma attiva appare solo la richiesta temporanea di conferma. Modifica un profilo per rinominarlo, cambiarne la modalita o assegnare un tasto rapido. Scegli due profili nell'interruttore in basso per alternarli con un solo bottone.\r\n\r\nHai 20 secondi per confermare ogni cambio. Dopo aver verificato i profili puoi togliere Conferma dopo il cambio per passare subito da uno all'altro. Le modifiche mantenute rimangono attive anche chiudendo l'app. Minimizzare lascia l'app vicino all'orologio e le scorciatoie continuano a funzionare.\r\n\r\nLa duplicazione non garantisce il formato 16:9: dipende dai monitor, dal driver e dalla risoluzione comune selezionata. Non servono modifiche ai pannelli Intel o NVIDIA.", "Come usare Monitor Switch", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsHandleCreated) foreach (int id in hotkeys.Keys) UnregisterHotKey(Handle, id);
                refreshTimer.Dispose(); tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose();
                updateCancellation.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}

