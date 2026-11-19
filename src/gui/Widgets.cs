using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DiskManager.Gui
{
    static class Look
    {
        static readonly Color[] dataCols = { N.Teal, N.Magenta, N.Blue, N.Lime, N.Orange };

        public static Color For(Segment s)
        {
            if (s == null) return N.Dim;
            if (s.IsFree) return N.Dim;
            var p = (PartInfo)s;
            switch (p.Kind)
            {
                case "EFI system": return N.Purple;
                case "MS reserved": return N.Dim;
                case "recovery": return N.Orange;
            }
            int idx = p.Disk.Partitions.Where(x => x.HasVolume && x.Kind != "EFI system" && x.Kind != "recovery").ToList().IndexOf(p);
            return dataCols[(Math.Max(0, idx) + p.Disk.Number) % dataCols.Length];
        }

        public static string Title(PartInfo p)
        {
            if (p.Letter != '\0') return p.Letter + ":" + (p.Label.Length > 0 ? " " + p.Label : "");
            if (p.Label.Length > 0) return p.Label;
            return p.Kind;
        }
    }

    /// <summary>One row per disk: a header line and a bar of partitions/unallocated space drawn to scale (with a minimum width).</summary>
    sealed class DiskMap : NeonControl
    {
        List<DiskInfo> disks = new List<DiskInfo>();
        object selected, hover;
        int scroll;
        readonly List<KeyValuePair<Rectangle, object>> hits = new List<KeyValuePair<Rectangle, object>>();

        public event Action SelectionChanged;
        public event Action Activated;
        public string EmptyText = "loading disks…";

        public DiskMap() { BackColor = N.Panel; TabStop = true; }

        public List<DiskInfo> Disks
        {
            get { return disks; }
            set { disks = value ?? new List<DiskInfo>(); ClampScroll(); Invalidate(); }
        }

        public object Selected
        {
            get { return selected; }
            set { if (value != selected) { selected = value; Invalidate(); if (SelectionChanged != null) SelectionChanged(); } }
        }

        int RowH { get { return N.S(26) + N.S(78) + N.S(18); } }
        int ContentH { get { return disks.Count * RowH; } }
        void ClampScroll() { scroll = Math.Max(0, Math.Min(scroll, ContentH - Height)); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= e.Delta / 120 * N.S(40);
            ClampScroll(); Invalidate();
        }

        object HitAt(Point pt)
        {
            foreach (var h in hits) if (h.Key.Contains(pt)) return h.Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = HitAt(e.Location);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var h = HitAt(e.Location);
            if (h != null) Selected = h;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { if (HitAt(e.Location) != null && Activated != null) Activated(); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // flat order: disk, its segments, next disk, ...
            var order = new List<object>();
            foreach (var d in disks) { order.Add(d); order.AddRange(d.Segments.Cast<object>()); }
            int i = order.IndexOf(selected);
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) i = Math.Min(order.Count - 1, i + 1);
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) i = Math.Max(0, i - 1);
            else if (e.KeyCode == Keys.Enter && Activated != null) { Activated(); return; }
            else { base.OnKeyDown(e); return; }
            if (i >= 0 && i < order.Count) Selected = order[i];
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(N.Panel);
            hits.Clear();
            if (disks.Count == 0) { N.Draw(g, EmptyText, N.Mono(10f), ClientRectangle, N.Dim, N.Center); return; }
            g.SmoothingMode = SmoothingMode.None;
            int y = -scroll;
            foreach (var d in disks)
            {
                if (y + RowH > 0 && y < Height) PaintDisk(g, d, y);
                y += RowH;
            }
            if (ContentH > Height)
            {
                int th = Math.Max(N.S(30), Height * Height / ContentH);
                int ty = (Height - th) * scroll / Math.Max(1, ContentH - Height);
                using (var b = new SolidBrush(N.Alpha(N.Purple, 160))) g.FillRectangle(b, Width - N.S(4), ty, N.S(3), th);
            }
        }

        void PaintDisk(Graphics g, DiskInfo d, int y)
        {
            int headH = N.S(26), barH = N.S(78);
            var head = new Rectangle(0, y, Width - N.S(8), headH);
            bool dsel = selected == d, dhov = hover == d;
            hits.Add(new KeyValuePair<Rectangle, object>(head, d));
            if (dsel || dhov) using (var b = new SolidBrush(dsel ? N.Alpha(N.Crimson, 40) : N.PanelAlt)) g.FillRectangle(b, head);

            string badge = " DISK " + d.Number + " ";
            var bf = N.Mono(9f, FontStyle.Bold);
            int bw = N.Width(badge, bf) + N.S(6);
            Color bc = d.IsOffline ? N.Dim : d.IsBoot ? N.Crimson : N.Magenta;
            using (var b = new SolidBrush(bc)) g.FillRectangle(b, 0, y + N.S(3), bw, headH - N.S(6));
            N.Draw(g, badge, bf, new Rectangle(0, y, bw, headH), N.Dark, N.Center);

            var tags = new List<string>();
            if (d.IsBoot) tags.Add("windows");
            if (d.IsOffline) tags.Add("offline");
            if (d.IsReadOnly) tags.Add("read-only");
            string info = d.Name + "   " + Fmt.Bytes(d.Size) + " · " + d.Style + " · " + d.Bus + (tags.Count > 0 ? " · " + string.Join(" · ", tags) : "");
            string right = d.StyleCode == 0 ? "not initialized" : d.FreeTotal > 0 ? Fmt.Bytes(d.FreeTotal) + " unallocated" : "fully allocated";
            int rw = N.Width(right, Font) + N.S(6);
            N.Draw(g, info, N.Mono(9.5f), new Rectangle(bw + N.S(10), y, head.Width - bw - rw - N.S(20), headH), dsel ? N.Text : N.Mix(N.Text, N.Dim, 0.2));
            N.Draw(g, right, Font, new Rectangle(head.Right - rw, y, rw, headH), d.FreeTotal > 0 ? N.Lime : N.Dim, N.Right);

            var bar = new Rectangle(0, y + headH + N.S(4), Width - N.S(8), barH);
            if (d.StyleCode == 0 || d.Segments.Count == 0)
            {
                PaintBox(g, bar, null, d.StyleCode == 0 ? "not initialized" : "empty", Fmt.Bytes(d.Size) + "  ·  select the disk header to initialize", N.Orange, dsel, dhov, 0, true);
                return;
            }

            int gap = N.S(4), n = d.Segments.Count;
            int avail = bar.Width - gap * (n - 1);
            int minW = Math.Min(N.S(84), avail / n);
            double total = d.Segments.Sum(s => (double)s.Size);
            int extra = avail - minW * n;
            int x = bar.X;
            for (int i = 0; i < n; i++)
            {
                var s = d.Segments[i];
                int w = i == n - 1 ? bar.Right - x : minW + (int)(extra * (s.Size / total));
                var r = new Rectangle(x, bar.Y, w, bar.Height);
                hits.Add(new KeyValuePair<Rectangle, object>(r, s));
                PaintSegment(g, s, r);
                x += w + gap;
            }
        }

        void PaintSegment(Graphics g, Segment s, Rectangle r)
        {
            bool sel = selected == s, hov = hover == s;
            if (s.IsFree)
            {
                PaintBox(g, r, null, "unallocated", Fmt.Bytes(s.Size), N.Lime, sel, hov, 0, true);
                return;
            }
            var p = (PartInfo)s;
            string l2 = Fmt.Bytes(p.Size) + (p.HasVolume ? " · " + p.Fs : "");
            string l3 = p.Letter != '\0' || p.Label.Length > 0 ? p.Kind : (p.IsSystem ? "boots the PC" : "");
            if (p.IsBoot) l3 = "windows";
            double used = p.HasVolume && p.VolSize > 0 ? 1 - (double)p.VolFree / p.VolSize : -1;
            PaintBox(g, r, l3, Look.Title(p), l2, Look.For(p), sel, hov, used, false);
        }

        void PaintBox(Graphics g, Rectangle r, string l3, string l1, string l2, Color acc, bool sel, bool hov, double used, bool hatch)
        {
            Color fill = sel ? N.Mix(N.PanelAlt, acc, 0.16) : hov ? N.Mix(N.PanelAlt, acc, 0.08) : N.PanelAlt;
            using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
            if (hatch)
                using (var hb = new HatchBrush(HatchStyle.WideDownwardDiagonal, N.Alpha(acc, sel ? 60 : 30), Color.Transparent))
                    g.FillRectangle(hb, r);
            else
                using (var b = new SolidBrush(acc)) g.FillRectangle(b, r.X, r.Y, r.Width, N.S(4));

            if (sel)
            {
                using (var p = new Pen(N.Alpha(acc, 70), N.Sf(5))) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                using (var p = new Pen(acc, N.Sf(2))) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
            }
            else using (var p = new Pen(hatch ? N.Alpha(acc, 120) : N.Alpha(acc, 90))) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);

            int pad = N.S(8), lh = N.S(17);
            var t1 = new Rectangle(r.X + pad, r.Y + N.S(8), r.Width - 2 * pad, lh);
            N.Draw(g, l1, N.Mono(9.5f, FontStyle.Bold), t1, hatch ? acc : N.Text);
            N.Draw(g, l2, N.Mono(8.5f), new Rectangle(t1.X, t1.Bottom + N.S(1), t1.Width, lh), N.Mix(N.Text, N.Dim, 0.35));
            if (!string.IsNullOrEmpty(l3))
                N.Draw(g, l3, N.Mono(8f), new Rectangle(t1.X, t1.Bottom + lh, t1.Width, lh), N.Dim);
            if (used >= 0 && r.Width > N.S(30))
            {
                var ub = new Rectangle(r.X + pad, r.Bottom - N.S(11), r.Width - 2 * pad, N.S(5));
                using (var b = new SolidBrush(N.Track)) g.FillRectangle(b, ub);
                int uw = (int)(ub.Width * used);
                if (uw > 0)
                    using (var lg = new LinearGradientBrush(new Rectangle(ub.X - 1, ub.Y, ub.Width + 2, ub.Height), acc, N.Crimson, 0f))
                    {
                        lg.InterpolationColors = N.Blend(acc, used > 0.9 ? N.Crimson : N.Mix(acc, N.Magenta, 0.5));
                        g.FillRectangle(lg, ub.X, ub.Y, uw, ub.Height);
                    }
            }
        }
    }

    /// <summary>Key / value rows.</summary>
    sealed class InfoView : NeonControl
    {
        public readonly List<Tuple<string, string, Color>> Rows = new List<Tuple<string, string, Color>>();
        public string Heading = "";
        public Color HeadingColor = N.Text;

        public void Set(string heading, Color hc) { Heading = heading; HeadingColor = hc; Rows.Clear(); }
        public void Add(string k, string v, Color? c = null) { Rows.Add(Tuple.Create(k, v, c ?? N.Text)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(N.Panel);
            int y = 0, lh = N.S(21), kw = N.S(100);
            if (Heading.Length > 0)
            {
                N.Draw(g, Heading, N.Mono(11f, FontStyle.Bold), new Rectangle(0, y, Width, N.S(26)), HeadingColor);
                y += N.S(32);
            }
            foreach (var r in Rows)
            {
                if (y + lh > Height) break;
                N.Draw(g, r.Item1, Font, new Rectangle(0, y, kw, lh), N.Dim);
                N.Draw(g, r.Item2, Font, new Rectangle(kw, y, Width - kw, lh), r.Item3);
                y += lh;
            }
        }
    }

    /// <summary>Timestamped operation log, newest at the bottom.</summary>
    sealed class LogView : NeonControl
    {
        readonly List<Tuple<string, string, Color>> lines = new List<Tuple<string, string, Color>>();

        public void Add(string text, Color c)
        {
            foreach (var l in text.Split('\n'))
                lines.Add(Tuple.Create(DateTime.Now.ToString("HH:mm:ss"), l.TrimEnd('\r'), c));
            if (lines.Count > 400) lines.RemoveRange(0, lines.Count - 400);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(N.Panel);
            int lh = N.S(18), tw = N.S(78);
            int rows = Math.Max(1, Height / lh);
            int start = Math.Max(0, lines.Count - rows);
            int y = Height - Math.Min(rows, lines.Count) * lh;
            for (int i = start; i < lines.Count; i++, y += lh)
            {
                N.Draw(g, lines[i].Item1, N.Mono(8.5f), new Rectangle(0, y, tw, lh), N.Purple);
                N.Draw(g, lines[i].Item2, N.Mono(9f), new Rectangle(tw, y, Width - tw, lh), lines[i].Item3);
            }
        }
    }

    /// <summary>Horizontal slider with a gradient fill; marks the original value with a tick.</summary>
    sealed class NeonSlider : NeonControl
    {
        long min, max = 1, value, mark = -1;
        public ColorBlend Gradient = N.Aurora;
        public event Action ValueChanged;
        bool drag;

        public NeonSlider() { Height = N.S(34); TabStop = true; Cursor = Cursors.Hand; }

        public void SetRange(long mn, long mx, long v, long markAt)
        {
            min = mn; max = Math.Max(mn + 1, mx); mark = markAt;
            value = Math.Max(min, Math.Min(max, v));
            Invalidate();
        }

        public long Value
        {
            get { return value; }
            set
            {
                long v = Math.Max(min, Math.Min(max, value));
                if (v != this.value) { this.value = v; Invalidate(); if (ValueChanged != null) ValueChanged(); }
            }
        }

        Rectangle Track { get { int p = N.S(10); return new Rectangle(p, Height / 2 - N.S(4), Width - 2 * p, N.S(8)); } }

        long ValueAt(int x)
        {
            var t = Track;
            double f = Math.Max(0, Math.Min(1, (x - t.X) / (double)Math.Max(1, t.Width)));
            return Storage.AlignDown(min + (long)((max - min) * f));
        }

        int XOf(long v) { var t = Track; return t.X + (int)(t.Width * ((v - min) / (double)(max - min))); }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); drag = true; Value = ValueAt(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) Value = ValueAt(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            long step = Math.Max(Fmt.MiB, Storage.AlignDown((max - min) / 100));
            if (e.KeyCode == Keys.Left) Value = value - step;
            else if (e.KeyCode == Keys.Right) Value = value + step;
            else if (e.KeyCode == Keys.PageDown) Value = value - step * 10;
            else if (e.KeyCode == Keys.PageUp) Value = value + step * 10;
            else if (e.KeyCode == Keys.Home) Value = min;
            else if (e.KeyCode == Keys.End) Value = max;
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : N.Panel);
            var t = Track;
            using (var b = new SolidBrush(N.Track)) g.FillRectangle(b, t);
            int hx = XOf(value);
            if (hx > t.X)
                using (var lg = new LinearGradientBrush(new Rectangle(t.X - 1, t.Y, t.Width + 2, t.Height), N.Purple, N.Teal, 0f))
                {
                    lg.InterpolationColors = Gradient;
                    g.FillRectangle(lg, t.X, t.Y, hx - t.X, t.Height);
                }
            if (mark >= min && mark <= max)
            {
                int mx = XOf(mark);
                using (var p = new Pen(N.Text, N.Sf(2))) g.DrawLine(p, mx, t.Y - N.S(7), mx, t.Bottom + N.S(7));
            }
            var h = new Rectangle(hx - N.S(6), Height / 2 - N.S(11), N.S(12), N.S(22));
            using (var b = new SolidBrush(Focused ? N.Text : N.Mix(N.Text, N.Dim, 0.3))) g.FillRectangle(b, h);
            using (var p = new Pen(N.Alpha(N.Magenta, 120), N.Sf(3))) g.DrawRectangle(p, h);
        }
    }

    /// <summary>Dark owner-drawn drop-down.</summary>
    sealed class NeonCombo : ComboBox
    {
        public Color Accent = N.Teal;

        public NeonCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = N.PanelAlt;
            ForeColor = N.Text;
            Font = N.Mono(10f);
            ItemHeight = N.S(22);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Accent : N.PanelAlt)) e.Graphics.FillRectangle(b, e.Bounds);
            N.Draw(e.Graphics, " " + Items[e.Index], Font, e.Bounds, sel ? N.Dark : N.Text);
        }
    }
}
