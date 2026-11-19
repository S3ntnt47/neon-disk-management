using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DiskManager.Gui
{
    sealed class MainForm : Form
    {
        readonly Logo logo = new Logo();
        readonly Label adminChip = new Label();
        readonly NeonButton elevateBtn = new NeonButton("restart as administrator", N.Orange, true);
        readonly NeonButton refreshBtn = new NeonButton("⟳ refresh", N.Blue);

        readonly NeonPanel mapPanel = new NeonPanel { Title = "disks", Accent = N.Crimson };
        readonly DiskMap map = new DiskMap();

        readonly NeonPanel infoPanel = new NeonPanel { Title = "selected", Accent = N.Teal };
        readonly InfoView info = new InfoView();

        readonly NeonPanel actPanel = new NeonPanel { Title = "actions", Accent = N.Magenta };
        readonly NeonButton extendMaxBtn = new NeonButton("extend to fill free space", N.Teal, true);
        readonly NeonButton resizeBtn = new NeonButton("shrink / extend…", N.Magenta);
        readonly NeonButton createBtn = new NeonButton("new partition…", N.Lime, true);
        readonly NeonButton letterBtn = new NeonButton("change drive letter…", N.Blue);
        readonly NeonButton deleteBtn = new NeonButton("delete partition…", N.Crimson);
        readonly NeonButton initBtn = new NeonButton("initialize disk (GPT)…", N.Orange, true);
        readonly Label hint = new Label();

        readonly NeonPanel logPanel = new NeonPanel { Title = "log", Accent = N.Purple };
        readonly LogView log = new LogView();
        readonly StatusLine status = new StatusLine();

        readonly bool admin = IsAdmin();
        Thread worker;
        string busyText;

        public MainForm()
        {
            Text = "Neon Disk Management";
            BackColor = N.Bg; ForeColor = N.Text; Font = N.Mono(9.5f);
            DoubleBuffered = true; KeyPreview = true;
            MinimumSize = new Size(N.S(1100), N.S(700));
            Size = new Size(Math.Min(N.S(1500), Screen.PrimaryScreen.WorkingArea.Width - 40), Math.Min(N.S(900), Screen.PrimaryScreen.WorkingArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            logo.Subtitle = "partition manager  ·  shrink · extend · create · delete";
            adminChip.AutoSize = false; adminChip.TextAlign = ContentAlignment.MiddleRight;
            adminChip.Font = N.Mono(9f); adminChip.BackColor = N.Bg;
            adminChip.ForeColor = admin ? N.Teal : N.Orange;
            adminChip.Text = admin ? "◉ administrator" : "▲ standard user · viewing only - changes need admin";
            elevateBtn.Visible = !admin;
            hint.Font = N.Mono(8.5f); hint.ForeColor = N.Dim; hint.BackColor = N.Panel; hint.UseMnemonic = false;

            mapPanel.Controls.Add(map);
            infoPanel.Controls.Add(info);
            actPanel.Controls.AddRange(new Control[] { extendMaxBtn, resizeBtn, createBtn, letterBtn, deleteBtn, initBtn, hint });
            logPanel.Controls.Add(log);
            Controls.AddRange(new Control[] { logo, adminChip, elevateBtn, refreshBtn, mapPanel, infoPanel, actPanel, logPanel, status });

            map.SelectionChanged += OnSelection;
            map.Activated += DefaultAction;
            refreshBtn.Click += (s, e) => Reload(null);
            elevateBtn.Click += (s, e) => Elevate();
            extendMaxBtn.Click += (s, e) => ExtendMax();
            resizeBtn.Click += (s, e) => ResizeSel();
            createBtn.Click += (s, e) => Create();
            letterBtn.Click += (s, e) => ChangeLetter();
            deleteBtn.Click += (s, e) => Delete();
            initBtn.Click += (s, e) => InitDisk();
            Resize += (s, e) => LayoutAll();

            log.Add("ready · " + (admin ? "running as administrator" : "standard user - restart as administrator to make changes"), admin ? N.Teal : N.Orange);
            OnSelection();
            Shown += (s, e) => Reload(null);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); N.DarkTitleBar(this, N.Crimson); }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); LayoutAll(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { Reload(null); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && deleteBtn.Visible && deleteBtn.Enabled) { Delete(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        // ───────────────────────────── layout ─────────────────────────────

        void LayoutAll()
        {
            if (ClientSize.Width < 100) return;
            int W = ClientSize.Width, H = ClientSize.Height, gap = N.S(10), m = N.S(12);
            int headH = N.S(104), statusH = N.S(26), logH = N.S(118), sideW = N.S(380);

            logo.SetBounds(m, N.S(6), Math.Min(N.S(660), W - N.S(560)), headH - N.S(6));
            adminChip.SetBounds(W - m - N.S(520), N.S(16), N.S(520), N.S(20));
            refreshBtn.Width = refreshBtn.PreferredWidth;
            refreshBtn.Location = new Point(W - m - refreshBtn.Width, N.S(44));
            elevateBtn.Width = elevateBtn.PreferredWidth;
            elevateBtn.Location = new Point(refreshBtn.Left - N.S(10) - elevateBtn.Width, N.S(44));

            status.SetBounds(0, H - statusH, W, statusH);
            logPanel.SetBounds(m, H - statusH - gap - logH, W - 2 * m, logH);
            var li = logPanel.Inner; log.SetBounds(li.X, li.Y, li.Width, li.Height);

            int top = headH, bottom = logPanel.Top - gap;
            int sx = W - m - sideW;
            mapPanel.SetBounds(m, top, sx - gap - m, bottom - top);
            var mi = mapPanel.Inner; map.SetBounds(mi.X, mi.Y + N.S(4), mi.Width, mi.Height - N.S(4));

            int infoH = N.S(226);
            infoPanel.SetBounds(sx, top, sideW, infoH);
            var ii = infoPanel.Inner; info.SetBounds(ii.X, ii.Y + N.S(4), ii.Width, ii.Height - N.S(4));

            actPanel.SetBounds(sx, infoPanel.Bottom + gap, sideW, bottom - infoPanel.Bottom - gap);
            LayoutActions();
            Invalidate(true);
        }

        void LayoutActions()
        {
            var ai = actPanel.Inner;
            int y = ai.Y + N.S(6);
            foreach (var b in new[] { extendMaxBtn, resizeBtn, createBtn, letterBtn, deleteBtn, initBtn })
            {
                if (!b.Visible) continue;
                b.SetBounds(ai.X, y, ai.Width, N.S(32));
                y = b.Bottom + N.S(8);
            }
            hint.SetBounds(ai.X, y + N.S(4), ai.Width, Math.Max(N.S(20), ai.Bottom - y - N.S(4)));
        }

        // ───────────────────────────── data ─────────────────────────────

        static string KeyOf(object o)
        {
            var d = o as DiskInfo; if (d != null) return "d" + d.Number;
            var s = o as Segment; if (s != null) return (s.IsFree ? "f" : "p") + s.Disk.Number + ":" + s.Offset;
            return null;
        }

        /// <summary>Re-reads every disk in the background and restores the selection (or selects `prefer`).</summary>
        void Reload(string prefer)
        {
            if (worker != null) return;
            string key = prefer ?? KeyOf(map.Selected);
            busyText = "reading disks…";
            UpdateStatus();
            RunBackground(() => Storage.Load(), disks =>
            {
                map.Disks = disks;
                object sel = null;
                if (key != null)
                {
                    foreach (var d in disks)
                    {
                        if (KeyOf(d) == key) sel = d;
                        foreach (var s in d.Segments) if (KeyOf(s) == key) sel = s;
                    }
                    // a freed/created region may have shifted slightly - fall back to anything starting near it
                    if (sel == null && key.Length > 1 && (key[0] == 'f' || key[0] == 'p'))
                    {
                        var parts = key.Substring(1).Split(':');
                        int dn = int.Parse(parts[0]); long off = long.Parse(parts[1]);
                        var disk = disks.FirstOrDefault(d => d.Number == dn);
                        if (disk != null) sel = disk.Segments.FirstOrDefault(s => s.Offset <= off + 16 * Fmt.MiB && s.End > off) ?? (object)disk;
                    }
                }
                if (sel == null && disks.Count > 0) sel = disks[0].Segments.OfType<PartInfo>().FirstOrDefault(p => p.IsBoot) ?? (object)disks[0];
                map.Selected = sel;
                OnSelection();
            }, "could not read disks");
        }

        void RunBackground<TR>(Func<TR> work, Action<TR> done, string failTitle)
        {
            worker = new Thread(() =>
            {
                TR r = default(TR); Exception err = null;
                try { r = work(); } catch (Exception ex) { err = ex; }
                BeginInvoke((Action)(() =>
                {
                    worker = null; busyText = null;
                    if (err != null)
                    {
                        log.Add("✗ " + failTitle + ": " + err.Message, N.Crimson);
                        DarkDialog.Show(this, failTitle, err.Message, N.Crimson);
                    }
                    else done(r);
                    OnSelection();
                }));
            }) { IsBackground = true };
            worker.Start();
            OnSelection();
        }

        /// <summary>Runs a change on a worker thread, logs the outcome, then re-reads the disks.</summary>
        void RunOp(string what, Action work, string select)
        {
            if (worker != null) return;
            busyText = what;
            log.Add("▸ " + what, N.Text);
            RunBackground(() => { work(); Storage.Rescan(); return true; }, _ =>
            {
                log.Add("✓ done: " + what, N.Teal);
                Reload(select);
            }, what + " failed");
        }

        // ───────────────────────────── selection ─────────────────────────────

        void OnSelection()
        {
            var sel = map.Selected;
            var d = sel as DiskInfo; var p = sel as PartInfo; var f = sel as FreeSpace;
            bool busy = worker != null;

            extendMaxBtn.Visible = resizeBtn.Visible = letterBtn.Visible = deleteBtn.Visible = p != null;
            createBtn.Visible = f != null;
            initBtn.Visible = d != null && d.StyleCode == 0;
            string h = "";

            if (p != null)
            {
                var disk = p.Disk;
                var next = disk.Segments.SkipWhile(s => s != p).Skip(1).FirstOrDefault() as FreeSpace;
                bool adjFree = next != null && next.Offset - p.End < 16 * Fmt.MiB;
                extendMaxBtn.Text = adjFree ? "extend to fill " + Fmt.Bytes(next.Size) + " free" : "extend to fill free space";
                extendMaxBtn.Enabled = !busy && disk.Editable && p.CanResize && adjFree;
                resizeBtn.Enabled = !busy && disk.Editable && p.CanResize;
                letterBtn.Enabled = !busy && disk.Editable && p.HasVolume && !p.IsSystem && !p.IsBoot;
                deleteBtn.Enabled = !busy && disk.Editable && p.Protected == null;

                var tips = new List<string>();
                if (!disk.Editable) tips.Add("This disk is offline or read-only.");
                if (p.Protected != null) tips.Add("Can't delete: " + p.Protected + ".");
                if (!p.HasVolume) tips.Add("No file system Windows can mount here, so it can't be resized.");
                else if (!p.CanResize) tips.Add("Windows can only shrink or extend NTFS and ReFS volumes - this one is " + p.Fs + ".");
                else if (!adjFree) tips.Add("To extend, the space right after this partition must be unallocated. " +
                    (disk.FreeTotal > 0 ? "The free space on this disk isn't next to it." : "This disk has no unallocated space - shrink or delete another partition first."));
                if (p.Kind == "recovery") tips.Add("Recovery partition: deleting it removes Windows' reset/repair tools.");
                if (p.IsBoot) tips.Add("You can still shrink Windows' own partition to make room.");
                h = string.Join("\n\n", tips);

                info.Set(Look.Title(p), Look.For(p));
                info.Add("disk", "disk " + disk.Number + " · partition " + p.Number);
                info.Add("type", p.Kind + (p.IsBoot ? " · windows" : "") + (p.IsSystem ? " · system" : ""));
                info.Add("size", Fmt.Bytes(p.Size));
                if (p.HasVolume)
                {
                    info.Add("file system", p.Fs);
                    info.Add("used", Fmt.Bytes(p.VolSize - p.VolFree) + "  (" + (p.VolSize > 0 ? (100 - p.VolFree * 100 / p.VolSize) : 0) + "%)");
                    info.Add("free", Fmt.Bytes(p.VolFree), N.Lime);
                }
                info.Add("starts at", Fmt.Bytes(p.Offset));
                if (adjFree) info.Add("free after", Fmt.Bytes(next.Size), N.Lime);
            }
            else if (f != null)
            {
                createBtn.Enabled = !busy && f.Disk.Editable;
                h = "Unallocated space. Create a new partition here, or select the partition just before it and extend it.";
                info.Set("unallocated", N.Lime);
                info.Add("disk", "disk " + f.Disk.Number + " · " + f.Disk.Name);
                info.Add("size", Fmt.Bytes(f.Size), N.Lime);
                info.Add("starts at", Fmt.Bytes(f.Offset));
            }
            else if (d != null)
            {
                initBtn.Enabled = !busy && d.Editable;
                info.Set("disk " + d.Number, d.IsBoot ? N.Crimson : N.Magenta);
                info.Add("model", d.Name);
                info.Add("size", Fmt.Bytes(d.Size));
                info.Add("style", d.Style == "RAW" ? "not initialized" : d.Style);
                info.Add("bus", d.Bus);
                info.Add("partitions", d.Partitions.Count.ToString());
                info.Add("unallocated", Fmt.Bytes(d.FreeTotal), d.FreeTotal > 0 ? N.Lime : N.Dim);
                if (d.IsBoot) info.Add("note", "Windows runs from this disk", N.Orange);
                h = d.StyleCode == 0 ? "This disk is blank. Initialize it with a GPT partition table before creating partitions."
                                     : "Select a partition or unallocated area in the bar to act on it.";
            }
            else
            {
                info.Set("", N.Text);
                h = "Select a disk, partition or unallocated area.";
            }
            if (!admin && sel != null) h = "Viewing only - restart as administrator to make changes.\n\n" + h;
            hint.Text = h;
            info.Invalidate();
            LayoutActions();
            actPanel.Invalidate(true);
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (busyText != null) { status.Set("WORKING", N.Magenta, busyText); return; }
            int nd = map.Disks.Count, np = map.Disks.Sum(d => d.Partitions.Count);
            long free = map.Disks.Sum(d => d.FreeTotal);
            status.Set(admin ? "READY" : "VIEW", admin ? N.Lime : N.Orange,
                nd + " disks  │  " + np + " partitions  │  " + Fmt.Bytes(free) + " unallocated  │  F5 refresh · double-click = main action");
        }

        void DefaultAction()
        {
            var p = map.Selected as PartInfo;
            if (p != null && resizeBtn.Enabled) { ResizeSel(); return; }
            if (map.Selected is FreeSpace && createBtn.Enabled) { Create(); return; }
            if (map.Selected is DiskInfo && initBtn.Visible && initBtn.Enabled) InitDisk();
        }

        // ───────────────────────────── actions ─────────────────────────────

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        void Elevate()
        {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" }); Close(); }
            catch (Win32Exception) { }
        }

        bool NeedAdmin()
        {
            if (admin) return true;
            if (DarkDialog.Show(this, "administrator needed", "Changing partitions needs administrator rights.\n\nRestart Neon Disk Management as administrator now?",
                    N.Orange, "restart as admin", "cancel") == 0) Elevate();
            return false;
        }

        void ExtendMax()
        {
            var p = map.Selected as PartInfo;
            if (p == null || !NeedAdmin()) return;
            busyText = "checking how far " + Look.Title(p) + " can grow…";
            UpdateStatus();
            RunBackground(() => { long mn, mx; Storage.SupportedSize(p, out mn, out mx); return mx; }, max =>
            {
                if (max - p.Size < Fmt.MiB) { DarkDialog.Show(this, "nothing to extend into", "Windows reports " + Look.Title(p) + " is already as large as it can be.", N.Orange); return; }
                if (DarkDialog.Show(this, "extend " + Look.Title(p),
                        "Grow " + Look.Title(p) + " from " + Fmt.Bytes(p.Size) + " to " + Fmt.Bytes(max) + " (+" + Fmt.Bytes(max - p.Size) + ").\n\n" +
                        "This uses the unallocated space right after it. Your files stay where they are.", N.Teal, "extend", "cancel") != 0) return;
                RunOp("extend " + Look.Title(p) + " to " + Fmt.Bytes(max), () => Storage.Resize(p, max), KeyOf(p));
            }, "could not query " + Look.Title(p));
        }

        void ResizeSel()
        {
            var p = map.Selected as PartInfo;
            if (p == null || !NeedAdmin()) return;
            busyText = "asking Windows how far " + Look.Title(p) + " can shrink (can take a minute on big drives)…";
            log.Add("▸ measuring " + Look.Title(p) + "…", N.Dim);
            UpdateStatus();
            RunBackground(() => { long mn, mx; Storage.SupportedSize(p, out mn, out mx); return Tuple.Create(mn, mx); }, r =>
            {
                long min = Storage.AlignUp(r.Item1), max = Math.Max(r.Item2, p.Size);
                using (var dlg = new ResizeDialog(p, Math.Min(min, p.Size), max))
                {
                    dlg.ShowDialog(this);
                    if (!dlg.Ok) return;
                    long size = dlg.NewSize;
                    string verb = size < p.Size ? "shrink" : "extend";
                    if (size < p.Size && p.IsBoot &&
                        DarkDialog.Show(this, "shrink Windows partition",
                            "Shrinking " + Look.Title(p) + " by " + Fmt.Bytes(p.Size - size) + ".\n\nThis is normally safe, but make sure you have a backup and keep some free space for updates.",
                            N.Orange, "shrink", "cancel") != 0) return;
                    RunOp(verb + " " + Look.Title(p) + " " + Fmt.Bytes(p.Size) + " → " + Fmt.Bytes(size), () => Storage.Resize(p, size), KeyOf(p));
                }
            }, "could not query " + Look.Title(p));
        }

        void Create()
        {
            var f = map.Selected as FreeSpace;
            if (f == null || !NeedAdmin()) return;
            using (var dlg = new CreateDialog(f))
            {
                dlg.ShowDialog(this);
                if (!dlg.Ok) return;
                long size = dlg.PartSize; char letter = dlg.Letter; string fs = dlg.Fs, label = dlg.VolLabel;
                RunOp("create " + Fmt.Bytes(size) + " " + fs + " partition on disk " + f.Disk.Number,
                    () => Storage.CreateAndFormat(f, size, letter, fs, label, msg => BeginInvoke((Action)(() => { busyText = msg; log.Add("  " + msg, N.Dim); UpdateStatus(); }))),
                    "p" + f.Disk.Number + ":" + f.Offset);
            }
        }

        void ChangeLetter()
        {
            var p = map.Selected as PartInfo;
            if (p == null || !NeedAdmin()) return;
            using (var dlg = new LetterDialog(p))
            {
                dlg.ShowDialog(this);
                if (!dlg.Ok) return;
                char l = dlg.Letter;
                RunOp((l == '\0' ? "remove letter from " : "assign " + l + ": to ") + Look.Title(p), () => Storage.SetLetter(p, l), KeyOf(p));
            }
        }

        void Delete()
        {
            var p = map.Selected as PartInfo;
            if (p == null || !NeedAdmin()) return;
            if (p.Protected != null) { DarkDialog.Show(this, "protected", "Neon Disk Management won't delete this partition: " + p.Protected + ".", N.Orange); return; }

            string what = Look.Title(p) + " (" + p.Kind + ", " + Fmt.Bytes(p.Size) + (p.HasVolume ? ", " + p.Fs : "") + ")";
            string text = "Delete " + what + " on disk " + p.Disk.Number + "?\n\n";
            if (p.HasVolume && p.VolSize - p.VolFree > 0) text += "It holds " + Fmt.Bytes(p.VolSize - p.VolFree) + " of data. ";
            text += "Everything on it will be erased and the space becomes unallocated. This cannot be undone.";
            if (p.Kind == "recovery") text += "\n\nThis is a recovery partition - Windows' reset and repair tools live here.";
            if (p.Kind == "EFI system" || p.Kind == "MS reserved") text += "\n\nThis is a " + p.Kind + " partition - another operating system may need it to boot.";
            string word = p.Letter != '\0' ? "delete " + p.Letter : "delete";
            using (var dlg = new TypeConfirmDialog("delete partition", text, word, "delete forever"))
            {
                dlg.ShowDialog(this);
                if (!dlg.Ok) return;
            }
            string key = "f" + p.Disk.Number + ":" + Storage.AlignUp(p.Offset);
            RunOp("delete " + what, () => Storage.Delete(p), key);
        }

        void InitDisk()
        {
            var d = map.Selected as DiskInfo;
            if (d == null || d.StyleCode != 0 || !NeedAdmin()) return;
            if (DarkDialog.Show(this, "initialize disk " + d.Number,
                    "Write a GPT partition table to disk " + d.Number + " (" + d.Name + ", " + Fmt.Bytes(d.Size) + ").\n\nThe disk is currently blank. Afterwards you can create partitions on it.",
                    N.Orange, "initialize GPT", "cancel") != 0) return;
            RunOp("initialize disk " + d.Number + " as GPT", () => Storage.Initialize(d, true), "d" + d.Number);
        }
    }
}
