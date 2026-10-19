using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MonitorSwitch
{
    internal sealed class UpdateDialog : Form
    {
        private readonly Label message;
        private readonly TextBox notes;
        private readonly Button action, cancel;
        private readonly ProgressBar progress;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private UpdateInfo available;
        private bool working;
        public string Package { get; private set; }
        public UpdateDialog(UpdateInfo known, bool preview = false)
        {
            Theme.Dialog(this, "Aggiornamenti MonitorSwitch", new Size(570, 442)); Icon = AppAssets.Icon;
            var title = Theme.Label("MonitorSwitch " + Updates.CurrentLabel, 17, Theme.Text); title.SetBounds(24, 18, 520, 38);
            message = Theme.Label("Controllo delle release GitHub…", 10, Theme.Muted); message.SetBounds(24, 65, 520, 50);
            notes = new TextBox { Left = 24, Top = 123, Width = 522, Height = 179, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Background, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Text = "Gli aggiornamenti provengono da github.com/carellice/MonitorSwitch." };
            progress = new ProgressBar { Left = 24, Top = 315, Width = 522, Height = 15, Visible = false };
            var link = new LinkLabel { Left = 24, Top = 340, Width = 522, Height = 24, Text = "Apri tutte le release su GitHub", LinkColor = Color.FromArgb(178, 166, 255) };
            link.LinkClicked += delegate { Process.Start(new ProcessStartInfo(Updates.Repository + "/releases") { UseShellExecute = true }); };
            action = Theme.Button("Controlla aggiornamenti", true); action.SetBounds(24, 382, 310, 38); action.Enabled = false;
            action.Click += delegate { if (available == null) Check(); else Download(); };
            cancel = Theme.Button("Chiudi", false); cancel.SetBounds(350, 382, 196, 38); cancel.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { title, message, notes, progress, link, action, cancel }); CancelButton = cancel;
            Shown += delegate { if (known != null) ShowResult(known); if (!preview) Check(); };
            FormClosing += delegate { cancellation.Cancel(); };
        }
        private void ShowResult(UpdateInfo update)
        {
            available = update;
            message.Text = update == null ? "Hai gia la versione stabile piu recente disponibile." : "Disponibile la versione " + update.Version.ToString(3) + ". I tuoi profili vengono mantenuti.";
            notes.Text = update == null ? "Nessun aggiornamento disponibile su GitHub." : update.Notes;
            action.Text = update == null ? "Controlla di nuovo" : "Scarica e riavvia"; action.Enabled = true;
        }
        private async void Check()
        {
            if (working) return; working = true; available = null; action.Enabled = false; message.Text = "Controllo delle release GitHub…";
            try { UpdateInfo result = await Updates.CheckAsync(cancellation.Token); if (!IsDisposed) ShowResult(result); }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) { message.Text = ex.Message; action.Text = "Riprova"; action.Enabled = true; } }
            finally { working = false; }
        }
        private async void Download()
        {
            if (working || available == null) return; working = true; action.Enabled = false; progress.Visible = true; cancel.Text = "Annulla";
            message.Text = "Download e verifica dell'aggiornamento…";
            try
            {
                Action<int> report = delegate(int value)
                {
                    if (!IsDisposed && IsHandleCreated) try { BeginInvoke((Action)delegate { if (!IsDisposed) progress.Value = value; }); } catch (InvalidOperationException) { }
                };
                Package = await Updates.DownloadAsync(available, report, cancellation.Token);
                if (!IsDisposed && !cancellation.IsCancellationRequested) { DialogResult = DialogResult.OK; Close(); }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !cancellation.IsCancellationRequested) { available = null; message.Text = ex.Message; action.Enabled = true; action.Text = "Controlla di nuovo"; cancel.Text = "Chiudi"; }
            }
            finally { working = false; }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) cancellation.Cancel(); base.Dispose(disposing); }
    }
}
