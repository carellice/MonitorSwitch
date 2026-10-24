using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorSwitch
{
    internal sealed class ShortcutEditor : UserControl
    {
        private readonly ComboBox modifiers, key;
        private static readonly uint[] ModifierValues = { 3, 6, 7, 5 };
        private readonly List<uint> keys = new List<uint> { 0 };
        public ShortcutEditor(Shortcut initial)
        {
            Height = 36; BackColor = Theme.Card;
            modifiers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 0, Top = 0, Width = 192 };
            modifiers.Items.AddRange(new object[] { "Ctrl + Alt", "Ctrl + Shift", "Ctrl + Alt + Shift", "Alt + Shift" });
            key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 204, Top = 0, Width = 156 };
            key.Items.Add("Nessuna");
            for (uint k = 112; k <= 123; k++) { keys.Add(k); key.Items.Add("F" + (k - 111)); }
            for (uint k = 65; k <= 90; k++) { keys.Add(k); key.Items.Add(((char)k).ToString()); }
            for (uint k = 48; k <= 57; k++) { keys.Add(k); key.Items.Add(((char)k).ToString()); }
            modifiers.SelectedIndex = Array.IndexOf(ModifierValues, initial.Modifiers) < 0 ? 0 : Array.IndexOf(ModifierValues, initial.Modifiers);
            key.SelectedIndex = Math.Max(0, keys.IndexOf(initial.Key)); key.SelectedIndexChanged += delegate { modifiers.Enabled = key.SelectedIndex > 0; };
            modifiers.Enabled = initial.Key != 0; Controls.AddRange(new Control[] { modifiers, key });
        }
        public Shortcut Value { get { uint k = keys[key.SelectedIndex]; return new Shortcut { Key = k, Modifiers = k == 0 ? 0 : ModifierValues[modifiers.SelectedIndex] }; } }
    }
    internal sealed class ShortcutDialog : Form
    {
        private readonly ShortcutEditor editor;
        public Shortcut Result { get { return editor.Value; } }
        public ShortcutDialog(Shortcut current)
        {
            Theme.Dialog(this, "Tasto rapido dell'interruttore", new Size(440, 180));
            var label = Theme.Label("Alterna i due profili scelti nella schermata principale.", 10, Theme.Muted); label.SetBounds(22, 16, 396, 43);
            editor = new ShortcutEditor(current.Clone()); editor.SetBounds(22, 66, 396, 36);
            var save = Theme.Button("Salva", true); save.SetBounds(22, 122, 188, 36); save.DialogResult = DialogResult.OK;
            var cancel = Theme.Button("Annulla", false); cancel.SetBounds(230, 122, 188, 36); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { label, editor, save, cancel }); CancelButton = cancel;
        }
    }
    internal sealed class ConfirmDialog : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        public ConfirmDialog(string name, Stopwatch elapsed)
        {
            Theme.Dialog(this, "Mantieni questa configurazione?", new Size(470, 246));
            ControlBox = false; TopMost = true;
            var title = Theme.Label("Profilo: " + name, 13, Theme.Text); title.SetBounds(22, 18, 426, 48);
            var text = Theme.Label("Controlla che gli schermi e l'immagine siano corretti.\r\nSenza conferma torni alla configurazione precedente.", 10, Theme.Muted); text.SetBounds(22, 75, 426, 52);
            var count = Theme.Label("", 10, Theme.Text); count.SetBounds(22, 137, 426, 27);
            var yes = Theme.Button("Mantieni", true); yes.SetBounds(22, 186, 200, 40); yes.DialogResult = DialogResult.OK;
            var no = Theme.Button("Ripristina (Esc)", false); no.SetBounds(248, 186, 200, 40); no.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { title, text, count, yes, no }); CancelButton = no; ActiveControl = no;
            timer = new System.Windows.Forms.Timer { Interval = 150 };
            Action tick = delegate { int left = Math.Max(0, (int)Math.Ceiling((20000 - elapsed.ElapsedMilliseconds) / 1000.0)); count.Text = "Ripristino automatico tra " + left + " secondi"; if (left == 0) CancelChange(); };
            timer.Tick += delegate { tick(); }; Shown += delegate { tick(); timer.Start(); };
        }
        public void CancelChange() { DialogResult = DialogResult.Cancel; Close(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

