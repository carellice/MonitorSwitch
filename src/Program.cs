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
            if (args.Length == 2 && (args[0] == "--diagnose" || args[0] == "--validate-current" || args[0] == "--validate-editor" || args[0] == "--validate-profiles"))
            {
                try
                {
                    DisplayState state = Native.Read(); var monitors = Native.Inventory(state);
                    var remembered = KnownMonitors.Read();
                    string report = "MonitorSwitch " + Updates.CurrentLabel + "\r\nModalita: " + Profile.ModeLabel(state.Topology()) + "\r\n" +
                        String.Join("\r\n", monitors.Select(m => m.Name + " | " + m.Summary + " | " + m.Adapter + " | " + m.Key + "\r\nRisoluzioni: " + String.Join(", ", Native.Resolutions(m))));
                    report += "\r\nMonitor ricordati da Windows: " + remembered.Count + "\r\nIdentita dei monitor collegati presenti nello storico Windows: " + monitors.Count(m => remembered.Any(r => String.Equals(r.DevicePath, m.Key, StringComparison.OrdinalIgnoreCase))) + "/" + monitors.Count;
                    report += "\r\n" + String.Join("\r\n", state.Capture(monitors).Select(s => s.Name + ": desktop " + s.Width + " x " + s.Height +
                        ", segnale " + s.Timing.Active.Width + " x " + s.Timing.Active.Height + ", scaling " + s.Scaling + ", desktop virtuale " + s.DesktopImage.HasValue));
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
                        foreach (MonitorInfo monitor in monitors)
                        {
                            MonitorSetting selection = monitor.Baseline.Copy(); selection.Group = 0; selection.X = selection.Y = 0; selection.Scaling = 4;
                            if (monitor.PreferredWidth >= 1920 && monitor.PreferredHeight >= 1080) { selection.Width = 1920; selection.Height = 1080; }
                            Native.Plan(Native.BuildPlan(new List<MonitorSetting> { selection }, monitors), true);
                        }
                        report += "\r\nEditor: profili per singolo monitor con proporzioni mantenute validati senza applicare modifiche.";
                    }
                    if (args[0] == "--validate-profiles")
                    {
                        File.WriteAllText(args[1], report);
                        foreach (Profile profile in ProfileStore.Load(ProfileStore.FileName).Profiles.Where(p => p.HasLayout))
                        {
                            Native.Plan(Native.BuildPlan(profile.Layout, monitors), true);
                            report += "\r\nProfilo " + profile.Name + ": validazione driver riuscita. Nessuna modifica applicata.";
                        }
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
                using (var form = new RenameMonitorDialog(new MonitorRecord { HardwareName = "ZQE-CAA", Alias = "Monitor scrivania" })) Render(form, Path.Combine(args[1], "rename-dialog.png"));
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
                if (!first) { if (!args.Contains("--tray")) MessageBox.Show("Monitor Switch o la vecchia app Full HD e gia aperta. Chiudila dall'icona vicino all'orologio e riprova.", "Monitor Switch"); return 0; }
                try
                {
                    if (File.Exists(Recovery.FileName)) Recovery.Restore();
                    if (args.Length == 1 && args[0] == "--restore") return 0;
                    Application.Run(new MainForm(false, args.Contains("--tray") || Updates.RestartInTray)); return 0;
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
        private bool initialTray, startupInitialized;
        private readonly CheckBox startWithWindows;
        private readonly Settings settings;
        private DisplayState state;
        private List<MonitorInfo> monitors = new List<MonitorInfo>();
        private readonly ListView profileList;
        private readonly DataGridView monitorGrid;
        private readonly Label status, currentMode, selectedDescription, protectionStatus;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer refreshTimer, protectionTimer;
        private readonly ProfileProtection protection = new ProfileProtection();
        private bool historyInitialized;
        private string trayActiveId, historyFingerprint;
        private readonly Dictionary<int, string> hotkeys = new Dictionary<int, string>();
        private bool loading, changing, updating;
        private UpdateInfo availableUpdate;
        private readonly CancellationTokenSource updateCancellation = new CancellationTokenSource();
        private ConfirmDialog confirmation;
        private Guardian guardian;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
        public MainForm(bool isPreview, bool startInTray = false)
        {
            initialTray = startInTray; preview = isPreview;
            settings = preview ? PreviewSettings() : ProfileStore.Load(ProfileStore.FileName);
            Text = "MonitorSwitch"; ClientSize = new Size(1180, 760); MinimumSize = new Size(1100, 720);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text; Icon = AppAssets.Icon;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), ColumnCount = 1, RowCount = 6 };
            foreach (float height in new float[] { 76, 96, 0, 48, 32, 43 }) root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            Controls.Add(root);
            var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var title = Theme.Label("I tuoi monitor, i tuoi profili", 23, Theme.Text); title.SetBounds(0, 0, 720, 42);
            var subtitle = Theme.Label("Ritrova ogni schermo. Scegli dove giocare e lavorare, alla risoluzione che vuoi.", 10, Theme.Muted); subtitle.SetBounds(2, 45, 810, 25);
            heading.Controls.AddRange(new Control[] { title, subtitle }); root.Controls.Add(heading, 0, 0);
            var activeCard = new Panel { Width = 1132, Dock = DockStyle.Fill, BackColor = Theme.Card, Margin = new Padding(0, 6, 0, 12) };
            currentMode = Theme.Label("Nessun profilo attivo", 15, Theme.Text); currentMode.SetBounds(16, 10, 850, 30); currentMode.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            protectionStatus = Theme.Label("Applica un profilo per proteggere la risoluzione.", 9, Theme.Muted); protectionStatus.SetBounds(16, 43, 860, 26); protectionStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var deactivate = Theme.Button("Disattiva profilo", false); deactivate.Width = 170; deactivate.Height = 38; deactivate.Location = new Point(activeCard.Width - 186, 18); deactivate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deactivate.Click += delegate { DeactivateProfile(); };
            activeCard.Controls.AddRange(new Control[] { currentMode, protectionStatus, deactivate }); root.Controls.Add(activeCard, 0, 1);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 402)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Controls.Add(body, 0, 2);
            var profiles = new TableLayoutPanel { BackColor = Theme.Card, Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
            foreach (float height in new float[] { 34, 0, 67, 44, 44, 40 }) profiles.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            var profileTitle = Theme.Label("Profili", 14, Theme.Text); profileTitle.Dock = DockStyle.Fill; profiles.Controls.Add(profileTitle, 0, 0);
            profileList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill,
                BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            profileList.Columns.Add("Nome", 153); profileList.Columns.Add("Stato", 91); profileList.Columns.Add("Scorciatoia", 120);
            profileList.SelectedIndexChanged += delegate { UpdateSelected(); }; profileList.DoubleClick += delegate { ApplySelected(); }; profiles.Controls.Add(profileList, 0, 1);
            selectedDescription = Theme.Label("Crea il tuo primo profilo.", 9, Theme.Muted); selectedDescription.Dock = DockStyle.Fill; selectedDescription.Padding = new Padding(0, 8, 0, 0); profiles.Controls.Add(selectedDescription, 0, 2);
            var apply = Theme.Button("Attiva profilo selezionato", true); apply.Dock = DockStyle.Fill; apply.Margin = new Padding(0, 3, 0, 3); apply.Click += delegate { ApplySelected(); }; profiles.Controls.Add(apply, 0, 3);
            var editRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            var add = Theme.Button("Nuovo", false); add.Width = 112; add.Click += delegate { EditProfile(false, false); };
            var edit = Theme.Button("Modifica", false); edit.Width = 112; edit.Click += delegate { EditProfile(true, false); };
            var remove = Theme.Button("Elimina", false); remove.Width = 112; remove.Click += delegate { DeleteProfile(); };
            editRow.Controls.AddRange(new Control[] { add, edit, remove }); profiles.Controls.Add(editRow, 0, 4);
            var capture = Theme.Button("Salva configurazione attuale", false); capture.Font = new Font("Segoe UI", 9); capture.Dock = DockStyle.Fill; capture.Margin = new Padding(0, 3, 0, 0);
            capture.Click += delegate { EditProfile(false, true); }; profiles.Controls.Add(capture, 0, 5); body.Controls.Add(profiles, 0, 0);
            var displayPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(16), ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); displayPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            var displayTitle = Theme.Label("Storico monitor", 14, Theme.Text); displayTitle.Dock = DockStyle.Fill; displayPanel.Controls.Add(displayTitle, 0, 0);
            var historyHint = Theme.Label("Anche gli schermi scollegati restano qui. Seleziona un monitor per rinominarlo; il nome vale per tutti i profili.", 9, Theme.Muted); historyHint.Dock = DockStyle.Fill; displayPanel.Controls.Add(historyHint, 0, 1);
            monitorGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(48, 58, 78), EnableHeadersVisualStyles = false };
            monitorGrid.DefaultCellStyle.BackColor = Theme.Card; monitorGrid.DefaultCellStyle.ForeColor = Theme.Text; monitorGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 58, 87); monitorGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            monitorGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True; monitorGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            monitorGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Card; monitorGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted; monitorGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9); monitorGrid.ColumnHeadersHeight = 32; monitorGrid.RowTemplate.Height = 76;
            monitorGrid.Columns.Add("monitor", "Nome / modello"); monitorGrid.Columns.Add("state", "Collegamento"); monitorGrid.Columns.Add("resolution", "Risoluzione / formato"); monitorGrid.Columns.Add("seen", "Ultimo rilevamento");
            monitorGrid.Columns[0].FillWeight = 110; monitorGrid.Columns[1].FillWeight = 90; monitorGrid.Columns[2].FillWeight = 115; monitorGrid.Columns[3].FillWeight = 85;
            monitorGrid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) RenameMonitor(); }; displayPanel.Controls.Add(monitorGrid, 0, 2);
            var displayFooter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            var rename = Theme.Button("Rinomina", false); rename.Width = 120; rename.Click += delegate { RenameMonitor(); };
            var refresh = Theme.Button("Aggiorna", false); refresh.Width = 120; refresh.Click += delegate { RefreshDisplays(true); };
            var windows = Theme.Button("Schermi Windows ↗", false); windows.Width = 182; windows.Click += delegate { OpenSettings(); };
            displayFooter.Controls.AddRange(new Control[] { rename, refresh, windows }); displayPanel.Controls.Add(displayFooter, 0, 3); body.Controls.Add(displayPanel, 2, 0);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 8, 0, 0) };
            var help = Theme.Button("Come funziona", false); help.Width = 160; help.Click += delegate { ShowHelp(); };
            var updates = Theme.Button("Aggiornamenti", false); updates.Width = 160; updates.Click += delegate { ShowUpdates(); };
            toolbar.Controls.AddRange(new Control[] { help, updates }); root.Controls.Add(toolbar, 0, 3);
            startWithWindows = new CheckBox { Text = "Avvia con Windows (minimizzato)", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9) };
            if (!preview) { try { startWithWindows.Checked = Startup.Enabled; } catch (Exception ex) { Recovery.Log(ex); } }
            startWithWindows.CheckedChanged += delegate { SetStartup(startWithWindows.Checked); }; root.Controls.Add(startWithWindows, 0, 4);
            var bottom = new Panel { Dock = DockStyle.Fill };
            status = Theme.Label("Pronto. Crea un profilo scegliendo monitor e risoluzioni.", 9, Theme.Muted); status.Dock = DockStyle.Fill; status.Padding = new Padding(0, 10, 0, 0);
            var ask = new CheckBox { Text = "Conferma dopo il cambio", Checked = settings.ConfirmChanges, Dock = DockStyle.Right, Width = 235, Font = new Font("Segoe UI", 9), ForeColor = Theme.Muted };
            ask.CheckedChanged += delegate { settings.ConfirmChanges = ask.Checked; if (!preview) SaveSettings(); }; bottom.Controls.Add(status); bottom.Controls.Add(ask); root.Controls.Add(bottom, 0, 5);
            tray = new NotifyIcon { Text = "MonitorSwitch", Icon = AppAssets.TrayIcon, Visible = !preview }; tray.DoubleClick += delegate { ShowWindow(); };
            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 }; refreshTimer.Tick += delegate { refreshTimer.Stop(); if (!changing) { RefreshDisplays(false); ProtectProfile(); } };
            protectionTimer = new System.Windows.Forms.Timer { Interval = 1500 }; protectionTimer.Tick += delegate { if (!changing && !updating && protection.State != ProtectionState.Conflict && settings.Profiles.Any(p => p.Id == settings.ActiveProfileId && p.ProtectResolution)) { RefreshDisplays(false); ProtectProfile(); } };
            Resize += delegate { if (!preview && WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (changing) { e.Cancel = true; if (confirmation != null) confirmation.CancelChange(); } };
            RebuildProfiles(null);
            if (preview)
            {
                monitors = settings.Monitors.Take(2).Select(r => new MonitorInfo { Key = r.DevicePath, Name = r.HardwareName, Internal = r.Internal, Active = !r.Internal, Baseline = r.LastSetting.Copy(), Width = r.LastSetting.Width, Height = r.LastSetting.Height }).ToList();
                RenderHistory(); UpdateActiveProfile();
            }
            else
            {
                RefreshDisplays(false);
                // Restart protection only when the saved profile is actually active.
                if (!String.IsNullOrEmpty(settings.ActiveProfileId) && !Matches(settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId))) { settings.ActiveProfileId = null; SaveSettings(); }
                UpdateActiveProfile(); protectionTimer.Start();
            }
        }
        private Profile Selected { get { return profileList.SelectedItems.Count == 0 ? null : (Profile)profileList.SelectedItems[0].Tag; } }
        private void RebuildProfiles(string selected)
        {
            loading = true; profileList.Items.Clear();
            foreach (Profile p in settings.Profiles)
            {
                var item = new ListViewItem(new string[] { p.Name, "", p.Hotkey.Label }) { Tag = p }; profileList.Items.Add(item);
                if (p.Id == selected) item.Selected = true;
            }
            if (profileList.SelectedItems.Count == 0 && profileList.Items.Count > 0) profileList.Items[0].Selected = true;
            loading = false; UpdateSelected(); UpdateActiveProfile(); RebuildTray();
            if (IsHandleCreated && !preview) RegisterShortcuts();
        }
        private void UpdateSelected()
        {
            Profile p = Selected;
            selectedDescription.Text = p == null ? "Crea un profilo e scegli gli schermi da usare." : !p.HasLayout ? p.Description :
                String.Join("\r\n", p.Layout.Take(2).Select(m => MonitorHistory.Name(settings, m.DevicePath, m.Name) + " · " + m.Width + " × " + m.Height + " (" + LayoutEditor.Aspect(m.Width, m.Height) + ")")) + (p.Layout.Count > 2 ? " · altri " + (p.Layout.Count - 2) : "");
        }
        private void RebuildTray()
        {
            if (tray == null) return;
            ContextMenuStrip old = tray.ContextMenuStrip; var menu = new ContextMenuStrip();
            menu.Items.Add("Apri MonitorSwitch", null, delegate { ShowWindow(); });
            Profile active = ActiveProfile; trayActiveId = active == null ? "" : active.Id;
            menu.Items.Add(active == null ? "Nessun profilo attivo" : "Attivo: " + active.Name).Enabled = false;
            foreach (Profile profile in settings.Profiles)
            {
                Profile p = profile;
                var item = new ToolStripMenuItem(p.Name + (p.Hotkey.Key == 0 ? "" : "  (" + p.Hotkey.Label + ")"), null, delegate { ApplyProfile(p, true); }) { Checked = active != null && active.Id == p.Id };
                menu.Items.Add(item);
            }
            menu.Items.Add("Disattiva profilo", null, delegate { DeactivateProfile(); }); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Aggiornamenti…", null, delegate { ShowUpdates(); });
            var automatic = new ToolStripMenuItem("Controlla aggiornamenti all'avvio") { Checked = settings.CheckUpdatesOnStartup, CheckOnClick = true };
            automatic.CheckedChanged += delegate { settings.CheckUpdatesOnStartup = automatic.Checked; SaveSettings(); }; menu.Items.Add(automatic);
            var startup = new ToolStripMenuItem("Avvia con Windows (minimizzato)") { Checked = startWithWindows.Checked, Enabled = !preview };
            startup.Click += delegate { startWithWindows.Checked = !startWithWindows.Checked; }; menu.Items.Add(startup);
            menu.Items.Add("Esci", null, delegate { Close(); }); tray.ContextMenuStrip = menu; if (old != null) old.Dispose();
        }
        private void SetStartup(bool enabled)
        {
            if (preview || loading) return;
            try
            {
                Startup.Enabled = enabled;
                status.Text = enabled ? "Avvio con Windows attivo: l'app partira minimizzata vicino all'orologio." : "Avvio con Windows disattivato.";
            }
            catch (Exception ex)
            {
                loading = true; startWithWindows.Checked = !enabled; loading = false; ShowError(ex);
            }
            RebuildTray();
        }
        protected override void SetVisibleCore(bool value)
        {
            if (value && initialTray)
            {
                initialTray = false;
                WindowState = FormWindowState.Minimized;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                BeginInvoke(new Action(InitializeStartup));
                return;
            }
            base.SetVisibleCore(value);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            InitializeStartup();
        }
        private void InitializeStartup()
        {
            if (preview || startupInitialized) return;
            startupInitialized = true;
            RegisterShortcuts(); Updates.SignalStarted(); CheckUpdatesAtStartup();
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
                { Profile p = settings.Profiles.FirstOrDefault(x => x.Id == value); if (p != null) ApplyProfile(p, true); }
            }
            if ((m.Msg == 0x7E || m.Msg == 0x219) && refreshTimer != null && !preview && !changing) { refreshTimer.Stop(); refreshTimer.Start(); }
            base.WndProc(ref m);
        }
        private static Settings PreviewSettings()
        {
            var sample = Settings.Defaults();
            var screen = new MonitorSetting { DevicePath = "external", Name = "ZQE-CAA", Group = 0, Width = 1920, Height = 1080, Rotation = 1, Scaling = 4, RefreshNum = 120, RefreshDen = 1 };
            sample.Profiles.Add(new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Gioco 16:9", Mode = 0, Layout = new List<MonitorSetting> { screen }, Hotkey = new Shortcut { Modifiers = 3, Key = 112 } });
            var work = screen.Copy(); work.Width = 3440; work.Height = 1440;
            sample.ActiveProfileId = sample.Profiles[0].Id;
            sample.Profiles.Add(new Profile { Id = Guid.NewGuid().ToString("N"), Name = "Lavoro ultrawide", Mode = 0, Layout = new List<MonitorSetting> { work }, Hotkey = new Shortcut { Modifiers = 3, Key = 113 } });
            sample.Monitors.Add(new MonitorRecord { DevicePath = "external", HardwareName = "ZQE-CAA", Alias = "Monitor scrivania", LastSetting = screen, LastSeenUtc = DateTime.UtcNow.ToString("o") });
            var panel = screen.Copy(); panel.DevicePath = "panel"; panel.Name = "Schermo del portatile";
            sample.Monitors.Add(new MonitorRecord { DevicePath = "panel", HardwareName = "Schermo del portatile", Alias = "Portatile", Internal = true, LastSetting = panel, LastSeenUtc = DateTime.UtcNow.ToString("o") });
            sample.Monitors.Add(new MonitorRecord { DevicePath = "tv", HardwareName = "LG TV", Alias = "TV salotto", LastSetting = new MonitorSetting { DevicePath = "tv", Name = "LG TV", Width = 3840, Height = 2160, Rotation = 1, Scaling = 4, RefreshNum = 60, RefreshDen = 1 }, LastSeenUtc = DateTime.UtcNow.AddDays(-3).ToString("o") });
            return sample;
        }
        private void RenderHistory()
        {
            string fingerprint = String.Join("|", settings.Monitors.Select(r => r.DevicePath + ":" + r.DisplayName + ":" + r.HardwareName + ":" + r.LastSeenUtc + ":" + (r.LastSetting == null ? "" : r.LastSetting.Width + "x" + r.LastSetting.Height))) +
                String.Join("|", monitors.Select(m => m.Key + ":" + m.Active + ":" + m.Width + "x" + m.Height));
            if (fingerprint == historyFingerprint) return; historyFingerprint = fingerprint;
            int scroll = monitorGrid.FirstDisplayedScrollingRowIndex;
            string selected = monitorGrid.CurrentRow == null ? null : monitorGrid.CurrentRow.Tag as string;
            monitorGrid.Rows.Clear();
            foreach (MonitorRecord record in settings.Monitors.OrderByDescending(r => monitors.Any(m => String.Equals(m.Key, r.DevicePath, StringComparison.OrdinalIgnoreCase))).ThenBy(r => r.DisplayName))
            {
                MonitorInfo live = monitors.FirstOrDefault(m => String.Equals(m.Key, record.DevicePath, StringComparison.OrdinalIgnoreCase));
                string connection = live == null ? "Scollegato" : live.Active ? "Attivo" : "Collegato, spento";
                string last = "Data non disponibile"; DateTime seen;
                if (DateTime.TryParse(record.LastSeenUtc, out seen)) last = seen.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                MonitorSetting setting = live == null ? record.LastSetting : live.Baseline;
                string resolution = setting == null ? "Da configurare" : setting.Width + " × " + setting.Height + "\r\n" + LayoutEditor.Aspect(setting.Width, setting.Height);
                int index = monitorGrid.Rows.Add(record.DisplayName + (record.DisplayName == record.HardwareName ? "" : "\r\n" + record.HardwareName), connection, resolution, live == null ? last : "Ora");
                DataGridViewRow row = monitorGrid.Rows[index]; row.Tag = record.DevicePath;
                row.Cells[0].ToolTipText = record.DevicePath + "\r\n" + record.Adapter;
                row.DefaultCellStyle.ForeColor = live == null ? Theme.Muted : Theme.Text;
                if (record.DevicePath == selected) { row.Selected = true; monitorGrid.CurrentCell = row.Cells[0]; }
            }
            if (scroll >= 0 && scroll < monitorGrid.Rows.Count) monitorGrid.FirstDisplayedScrollingRowIndex = scroll;
        }
        private void RefreshDisplays(bool report)
        {
            if (preview || changing) return;
            try
            {
                state = Native.Read(); monitors = Native.Inventory(state);
                bool historyChanged = MonitorHistory.Observe(settings, monitors, DateTime.UtcNow, report);
                if (!historyInitialized || report) { historyChanged |= MonitorHistory.ImportKnown(settings, KnownMonitors.Read()); historyInitialized = true; }
                if (historyChanged) ProfileStore.Save(ProfileStore.FileName, settings);
                RenderHistory(); UpdateActiveProfile();
                if (report) status.Text = monitors.Count + " monitor collegati · " + settings.Monitors.Count + " nello storico.";
            }
            catch (Exception ex) { state = null; RenderHistory(); currentMode.Text = "Nessun profilo verificabile"; status.Text = ex.Message; Recovery.Log(ex); }
        }
        private bool Matches(Profile p)
        {
            if (state == null || p == null || !p.HasLayout) return false;
            try { return Native.MatchesProtected(p.Layout, state.Capture(monitors)); } catch { return false; }
        }
        private Profile ActiveProfile
        {
            get
            {
                if (preview) return settings.Profiles.FirstOrDefault();
                if (state == null) return null;
                try { return ProfileSelection.Active(settings, state.Capture(monitors)); } catch { return null; }
            }
        }
        private void UpdateActiveProfile()
        {
            if (currentMode == null) return;
            Profile active = ActiveProfile;
            currentMode.Text = active == null ? "Nessun profilo attivo" : "Profilo attivo: " + active.Name;
            Profile requested = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
            protectionStatus.Text = protection.State == ProtectionState.Conflict ? "Protezione sospesa: " + protection.Detail :
                protection.State == ProtectionState.MissingMonitor ? "Protezione in attesa: ricollega tutti i monitor del profilo." :
                requested != null && requested.ProtectResolution && active == null ? "Configurazione diversa dal profilo: verifica o ripristino in corso" :
                requested != null && requested.ProtectResolution ? "Protezione risoluzione attiva · proporzioni mantenute · lascia l'app nel tray" :
                active == null ? "Applica un profilo per scegliere gli schermi e la risoluzione." : "Configurazione rilevata · protezione non attiva";
            foreach (ListViewItem item in profileList.Items)
            {
                Profile p = (Profile)item.Tag;
                item.SubItems[1].Text = active != null && active.Id == p.Id ? "Attivo" : !p.HasLayout ? "Configura" : "";
                item.ForeColor = active != null && active.Id == p.Id ? Color.FromArgb(167, 221, 183) : Theme.Text;
            }
            if (tray != null)
            {
                string text = active == null ? "MonitorSwitch · nessun profilo attivo" : "MonitorSwitch · " + active.Name;
                tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; if (trayActiveId != (active == null ? "" : active.Id)) RebuildTray();
            }
        }
        private void DeactivateProfile()
        {
            if (changing || preview) return;
            settings.ActiveProfileId = null; settings.ProfileDeactivated = true; protection.Reset(); SaveSettings(); UpdateActiveProfile();
            status.Text = "Profilo disattivato. La configurazione degli schermi rimane quella attuale.";
        }
        private void RenameMonitor()
        {
            if (changing || preview || monitorGrid.CurrentRow == null) return;
            MonitorRecord record = MonitorHistory.Find(settings, monitorGrid.CurrentRow.Tag as string); if (record == null) return;
            using (var dialog = new RenameMonitorDialog(record))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string old = record.Alias; record.Alias = dialog.Alias;
                try { ProfileStore.Save(ProfileStore.FileName, settings); RenderHistory(); UpdateSelected(); status.Text = "Nome del monitor salvato per tutti i profili."; }
                catch (Exception ex) { record.Alias = old; ShowError(ex); }
            }
        }
        private void ProtectProfile()
        {
            if (preview || changing || updating || state == null) return;
            Profile profile = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
            if (profile == null || !profile.ProtectResolution) return;
            ProtectionState before = protection.State;
            bool corrected = protection.Tick(profile, monitors.Select(m => m.Key).ToList(), state.Capture(monitors), DateTime.UtcNow, delegate
            {
                DisplayPlan plan = Native.BuildPlan(profile.Layout, monitors); Native.Plan(plan, true);
                changing = true;
                try
                {
                    Recovery.Locked(delegate { Snapshot.Save(Recovery.FileName, state); });
                    using (var recovery = new Guardian())
                    {
                        recovery.Arm(); Recovery.Locked(delegate { Native.Plan(plan, false); });
                        DisplayObservation observed = DisplayVerification.WaitFor(profile.Layout);
                        if (recovery.HasExited) throw new InvalidOperationException("Protezione del cambio scaduta.");
                        Recovery.Commit(observed.State); state = observed.State; monitors = observed.Monitors; return observed.Layout;
                    }
                }
                catch { if (File.Exists(Recovery.FileName)) Recovery.Restore(); throw; }
                finally { changing = false; }
            });
            if (corrected) status.Text = "Ripristinata la risoluzione del profilo " + profile.Name + ".";
            if (protection.State == ProtectionState.Conflict && before != ProtectionState.Conflict)
            {
                status.Text = "Protezione sospesa: " + protection.Detail;
                Recovery.Log(new InvalidOperationException(status.Text));
                tray.ShowBalloonTip(6000, "Conflitto con il profilo", protection.Detail, ToolTipIcon.Warning);
            }
            UpdateActiveProfile();
        }
        private void ApplySelected() { if (Selected != null) ApplyProfile(Selected); }
        private void ApplyProfile(Profile profile, bool background = false)
        {
            if (preview || changing || updating) return;
            if (background) StayInTray();
            try
            {
                profile.Check(); if (!profile.HasLayout) throw new InvalidOperationException("Modifica il profilo e scegli i monitor da usare."); RefreshDisplays(false); if (state == null) throw new InvalidOperationException("Impossibile leggere la configurazione attuale.");
                DisplayPlan plan = Native.BuildPlan(profile.Layout, monitors);
                Native.Plan(plan, true);
                changing = true; status.Text = "Applicazione di " + profile.Name + "…"; status.Refresh();
                Recovery.Locked(delegate { Snapshot.Save(Recovery.FileName, state); }); guardian = new Guardian();
                Stopwatch deadline = Stopwatch.StartNew(); guardian.Arm();
                Recovery.Locked(delegate { Native.Plan(plan, false); });
                DisplayObservation observed = DisplayVerification.WaitFor(profile.Layout);
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
                Recovery.Commit(observed.State); settings.ActiveProfileId = profile.Id; settings.ProfileDeactivated = false; protection.Reset(); SaveSettings();
                status.Text = "Profilo " + profile.Name + " attivo." + (profile.ProtectResolution ? " Protezione risoluzione attiva nel tray." : "");
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
                changing = false; RefreshDisplays(false);
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
            using (var editor = new ProfileDialog(existing, capture, layout, monitors, settings.Monitors))
            {
                while (editor.ShowDialog(this) == DialogResult.OK)
                {
                    Profile result = editor.Result;
                    int index = existing == null ? settings.Profiles.Count : settings.Profiles.IndexOf(existing);
                    if (existing == null) settings.Profiles.Add(result); else settings.Profiles[index] = result;
                    string oldActive = settings.ActiveProfileId; bool oldDeactivated = settings.ProfileDeactivated;
                    if (result.Id == settings.ActiveProfileId) { settings.ActiveProfileId = null; settings.ProfileDeactivated = true; }
                    try { settings.Check(); ProfileStore.Save(ProfileStore.FileName, settings); if (oldActive == result.Id) protection.Reset(); RebuildProfiles(result.Id); status.Text = "Profilo salvato. Attivalo per applicare le impostazioni."; return; }
                    catch (Exception ex)
                    {
                        settings.ActiveProfileId = oldActive; settings.ProfileDeactivated = oldDeactivated; if (existing == null) settings.Profiles.RemoveAt(index); else settings.Profiles[index] = existing;
                        MessageBox.Show(this, ex.Message, "Profilo non salvato", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }
        private void DeleteProfile()
        {
            Profile p = Selected; if (p == null || changing || preview) return;
            if (MessageBox.Show(this, "Eliminare il profilo " + p.Name + "?", "Elimina profilo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int index = settings.Profiles.IndexOf(p); string active = settings.ActiveProfileId; bool oldDeactivated = settings.ProfileDeactivated;
            settings.Profiles.Remove(p); if (active == p.Id) { settings.ActiveProfileId = null; settings.ProfileDeactivated = true; }
            try { ProfileStore.Save(ProfileStore.FileName, settings); if (active == p.Id) protection.Reset(); RebuildProfiles(null); status.Text = "Profilo eliminato."; }
            catch (Exception ex) { settings.Profiles.Insert(index, p); settings.ActiveProfileId = active; settings.ProfileDeactivated = oldDeactivated; ShowError(ex); }
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
            MessageBox.Show(this, "Lo storico include i monitor ricordati da Windows, quelli rilevati dall'app e quelli nei profili precedenti, anche quando sono scollegati. Usa Rinomina per dare a ogni schermo un nome riconoscibile.\r\n\r\nCrea un profilo, spunta gli schermi da accendere e scegli per ciascuno la risoluzione. Gli altri schermi vengono disattivati; piu schermi selezionati estendono il desktop. Il monitor principale si sceglie nell'editor. Il formato (16:9, 16:10...) deriva dalla risoluzione.\r\n\r\nLa protezione ripristina i cambi di risoluzione e richiede proporzioni mantenute dal driver. Lascia MonitorSwitch aperto nel tray. Per i giochi usa Finestra senza bordi / Borderless e la risoluzione del profilo: il fullscreen esclusivo puo cambiare modalita autonomamente e il rendering interno dipende dal gioco. Dopo tre ripristini in un minuto la protezione si sospende per evitare cambi continui: passa a borderless e riattiva il profilo.\r\n\r\nIl profilo attivo e verificato sulla configurazione effettiva. Disattiva profilo ferma la protezione lasciando gli schermi come sono. La chiusura dell'app mantiene la configurazione ma interrompe la protezione.\r\n\r\nCon la conferma attiva hai 20 secondi per mantenere ogni cambio manuale, altrimenti viene ripristinato. I profili della versione precedente restano disponibili: se indicati come Da configurare, aprili e scegli gli schermi.", "Come usare MonitorSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsHandleCreated) foreach (int id in hotkeys.Keys) UnregisterHotKey(Handle, id);
                refreshTimer.Dispose(); protectionTimer.Dispose(); tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose();
                updateCancellation.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}

