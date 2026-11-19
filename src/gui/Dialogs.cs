using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DiskManager.Gui
{
    /// <summary>Borderless neon dialog with a title in the frame, draggable by its body.</summary>
    class NeonDialog : Form
    {
        protected readonly Color Accent;
        readonly string titleText;
        protected int Y;                                   // layout cursor
        protected readonly int W = N.S(600), Pad = N.S(22);
        protected NeonButton OkButton;
        public bool Ok;

        protected NeonDialog(string title, Color accent)
        {
            Accent = accent; titleText = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = N.Panel; ForeColor = N.Text;
            ShowInTaskbar = false; KeyPreview = true; DoubleBuffered = true;
            Font = N.Mono(9.5f);
            Y = N.S(44);
        }

        protected Label AddText(string text, Color c, float pt = 9.5f)
        {
            var l = new Label
            {
                Text = text, ForeColor = c, BackColor = N.Panel, Font = N.Mono(pt), UseMnemonic = false,
                Location = new Point(Pad, Y), MaximumSize = new Size(W - 2 * Pad, 0), AutoSize = true
            };
            Controls.Add(l);
            Y = l.Bottom + N.S(10);
            return l;
        }

        protected Label AddCaption(string text)
        {
            var l = AddText(text, N.Dim, 8.5f);
            Y -= N.S(6);
            return l;
        }

        protected T Add<T>(T c, int h) where T : Control
        {
            c.SetBounds(Pad, Y, W - 2 * Pad, h);
            Controls.Add(c);
            Y = c.Bottom + N.S(12);
            return c;
        }

        /// <summary>Right-aligned button row; the first button is the primary action.</summary>
        protected void AddButtons(string ok, string cancel = "cancel")
        {
            Y += N.S(6);
            int x = W - Pad;
            var c = new NeonButton(cancel, N.Dim);
            x -= c.Width; c.Location = new Point(x, Y); c.Click += (s, e) => Close(); Controls.Add(c);
            OkButton = new NeonButton(ok, Accent, true);
            x -= OkButton.Width + N.S(10); OkButton.Location = new Point(x, Y);
            OkButton.Click += (s, e) => { if (OkButton.Enabled && CanAccept()) { Ok = true; Close(); } };
            Controls.Add(OkButton);
            ClientSize = new Size(W, Y + N.S(30) + N.S(20));
        }

        protected virtual bool CanAccept() { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter && OkButton != null && OkButton.Enabled && CanAccept()) { Ok = true; Close(); }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(N.Panel);
            using (var p = new Pen(Accent)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var p = new Pen(N.Alpha(Accent, 60), N.Sf(4))) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            N.Draw(g, "─ " + titleText + " ", N.Mono(10f, FontStyle.Bold), new Rectangle(N.S(14), N.S(10), Width - N.S(28), N.S(22)), Accent);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }

    /// <summary>Slider + typed size, kept in sync.</summary>
    sealed class SizePicker
    {
        public readonly NeonSlider Slider = new NeonSlider();
        public readonly NeonTextBox Box;
        bool syncing;
        public event Action Changed;

        public SizePicker(Color accent, long min, long max, long start, long mark)
        {
            Box = new NeonTextBox(accent) { Placeholder = "e.g. 250 GB or 51200 MB" };
            Slider.SetRange(min, max, start, mark);
            Box.Text = Fmt.Editable(Slider.Value);
            Slider.ValueChanged += () =>
            {
                if (!syncing) { syncing = true; Box.Text = Fmt.Editable(Slider.Value); syncing = false; }
                if (Changed != null) Changed();
            };
            Box.Box.TextChanged += (s, e) =>
            {
                if (syncing) return;
                long b;
                if (Fmt.TryParseSize(Box.Text, out b)) { syncing = true; Slider.Value = Storage.AlignDown(b); syncing = false; }
                if (Changed != null) Changed();
            };
        }

        public long Value { get { return Slider.Value; } set { Slider.Value = value; } }

        /// <summary>True when the text box holds a size inside the range.</summary>
        public bool TextValid(long min, long max)
        {
            long b;
            return Fmt.TryParseSize(Box.Text, out b) && Storage.AlignDown(b) >= min && b <= max + Fmt.MiB;
        }
    }

    sealed class ResizeDialog : NeonDialog
    {
        readonly PartInfo p;
        readonly long min, max;
        readonly SizePicker size;
        readonly Label preview;
        public long NewSize { get { return size.Value; } }

        public ResizeDialog(PartInfo p, long min, long max) : base("shrink / extend " + Look.Title(p), N.Magenta)
        {
            this.p = p; this.min = min; this.max = max;
            AddText(Look.Title(p) + "  ·  " + p.Fs + "  ·  now " + Fmt.Bytes(p.Size) +
                    (p.HasVolume ? "  ·  " + Fmt.Bytes(p.VolSize - p.VolFree) + " used" : ""), N.Text, 10f);
            AddCaption("smallest " + Fmt.Bytes(min) + "  ·  largest " + Fmt.Bytes(max) + "  ·  the white tick is the current size");
            size = new SizePicker(N.Magenta, min, max, p.Size, p.Size);
            Add(size.Slider, N.S(34));

            var row = new Panel { BackColor = N.Panel };
            Add(row, N.S(32));
            int x = row.Width;
            foreach (var q in new[] { Tuple.Create("largest", max), Tuple.Create("current", p.Size), Tuple.Create("smallest", min) })
            {
                var b = new NeonButton(q.Item1, N.Blue);
                long v = q.Item2;
                x -= b.PreferredWidth;
                b.SetBounds(x, N.S(1), b.PreferredWidth, N.S(30));
                b.Click += (s, e) => size.Value = v;
                row.Controls.Add(b);
                x -= N.S(8);
            }
            size.Box.SetBounds(0, 0, x - N.S(2), N.S(32));
            row.Controls.Add(size.Box);

            preview = AddText("", N.Lime);
            preview.MinimumSize = new Size(W - 2 * Pad, N.S(40));
            Y = preview.Top + N.S(48);
            AddCaption("Shrinking frees space at the end of the partition. Extending only uses unallocated space directly after it.\nNo data is erased, but back up anything important first.");
            AddButtons("apply");
            size.Changed += Sync;
            Sync();
        }

        void Sync()
        {
            long v = size.Value;
            bool valid = size.TextValid(min, max);
            if (!valid) { preview.Text = "type a size between " + Fmt.Bytes(min) + " and " + Fmt.Bytes(max); preview.ForeColor = N.Orange; }
            else if (Math.Abs(v - p.Size) < Fmt.MiB) { preview.Text = "no change"; preview.ForeColor = N.Dim; }
            else if (v < p.Size) { preview.Text = "▼ shrink by " + Fmt.Bytes(p.Size - v) + " → " + Fmt.Bytes(v) + "\n  leaves " + Fmt.Bytes(p.Size - v) + " unallocated after " + Look.Title(p); preview.ForeColor = N.Orange; }
            else { preview.Text = "▲ extend by " + Fmt.Bytes(v - p.Size) + " → " + Fmt.Bytes(v) + "\n  uses unallocated space right after " + Look.Title(p); preview.ForeColor = N.Teal; }
            if (OkButton != null) { OkButton.Enabled = valid && Math.Abs(v - p.Size) >= Fmt.MiB; }
        }
    }

    sealed class CreateDialog : NeonDialog
    {
        readonly SizePicker size;
        readonly NeonCombo letter = new NeonCombo();
        readonly Segmented fs = new Segmented(N.Teal, "NTFS", "exFAT", "FAT32");
        readonly NeonTextBox label = new NeonTextBox(N.Teal) { Placeholder = "volume label (optional)" };
        readonly Label warn;
        readonly FreeSpace gap;

        public long PartSize { get { return size.Value; } }
        public string Fs { get { return fs.Options[fs.Selected]; } }
        public string VolLabel { get { return label.Text.Trim(); } }
        public char Letter
        {
            get
            {
                string s = (string)letter.SelectedItem;
                return s.StartsWith("auto") ? '*' : s.StartsWith("none") ? '\0' : s[0];
            }
        }

        public CreateDialog(FreeSpace gap) : base("new partition on disk " + gap.Disk.Number, N.Teal)
        {
            this.gap = gap;
            AddText(Fmt.Bytes(gap.Size) + " of unallocated space on " + gap.Disk.Name, N.Text, 10f);
            AddCaption("size");
            size = new SizePicker(N.Teal, Fmt.MiB * 16, gap.Size, gap.Size, -1);
            Add(size.Slider, N.S(34));
            var row = new Panel { BackColor = N.Panel };
            Add(row, N.S(32));
            size.Box.SetBounds(0, 0, N.S(200), N.S(32));
            row.Controls.Add(size.Box);
            var all = new NeonButton("use all", N.Blue);
            all.SetBounds(size.Box.Right + N.S(10), N.S(1), all.PreferredWidth, N.S(30));
            all.Click += (s, e) => size.Value = gap.Size;
            row.Controls.Add(all);

            AddCaption("file system");
            Add(fs, N.S(28));
            AddCaption("drive letter");
            letter.Items.Add("auto (next free)");
            foreach (var c in Storage.FreeLetters()) letter.Items.Add(c + ":");
            letter.Items.Add("none");
            letter.SelectedIndex = 0;
            Add(letter, N.S(30));
            AddCaption("label");
            Add(label, N.S(32));
            warn = AddText("", N.Orange, 8.5f);
            warn.MinimumSize = new Size(W - 2 * Pad, N.S(18));
            AddButtons("create & format");
            size.Changed += Sync; fs.Changed += Sync;
            Sync();
        }

        void Sync()
        {
            bool ok = size.TextValid(Fmt.MiB * 16, gap.Size);
            string w = !ok ? "type a size between 16 MB and " + Fmt.Bytes(gap.Size) : "";
            if (ok && Fs == "FAT32" && size.Value > 32 * Fmt.GiB) { w = "Windows only formats FAT32 up to 32 GB - pick exFAT or NTFS, or a smaller size"; ok = false; }
            warn.Text = w;
            if (OkButton != null) OkButton.Enabled = ok;
        }
    }

    sealed class LetterDialog : NeonDialog
    {
        readonly NeonCombo letter = new NeonCombo();
        public char Letter { get { string s = (string)letter.SelectedItem; return s.StartsWith("none") ? '\0' : s[0]; } }

        public LetterDialog(PartInfo p) : base("drive letter for " + Look.Title(p), N.Blue)
        {
            AddText("Currently " + (p.Letter != '\0' ? p.Letter + ":" : "no letter") +
                    ". Programs that use the old letter (shortcuts, libraries, installed apps) may stop finding their files.", N.Text);
            foreach (var c in Storage.FreeLetters()) letter.Items.Add(c + ":");
            if (p.Letter != '\0') letter.Items.Add("none (remove letter)");
            letter.SelectedIndex = 0;
            Add(letter, N.S(30));
            AddButtons("change");
        }
    }

    /// <summary>Destructive confirmation: the primary button unlocks only after typing the given word.</summary>
    sealed class TypeConfirmDialog : NeonDialog
    {
        readonly NeonTextBox box = new NeonTextBox(N.Crimson);
        readonly string word;

        public TypeConfirmDialog(string title, string text, string word, string action) : base(title, N.Crimson)
        {
            this.word = word;
            AddText(text, N.Text);
            AddCaption("type  " + word + "  to confirm");
            Add(box, N.S(32));
            AddButtons(action);
            OkButton.Enabled = false;
            box.Box.TextChanged += (s, e) => OkButton.Enabled = string.Equals(box.Text.Trim(), word, StringComparison.OrdinalIgnoreCase);
            Shown += (s, e) => box.Box.Focus();
        }
    }
}
