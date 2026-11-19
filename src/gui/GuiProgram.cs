using System;
using System.Drawing;
using System.Windows.Forms;

namespace DiskManager.Gui
{
    static class GuiProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--list") { SelfTest.List(); return; }
            N.SetProcessDPIAware();
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) N.Scale = g.DpiX / 96f;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                DarkDialog.Show(null, "unexpected error", e.Exception.Message + "\n\n" + e.Exception.StackTrace, N.Crimson);
            Application.Run(new MainForm());
        }
    }

    /// <summary>Read-only dump of what the app sees, written to disks-list.txt next to the exe (for troubleshooting).</summary>
    static class SelfTest
    {
        public static void List()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var d in Storage.Load())
                {
                    sb.AppendLine("disk " + d.Number + " " + d.Name + " " + Fmt.Bytes(d.Size) + " " + d.Style + " " + d.Bus + (d.IsBoot ? " boot" : ""));
                    foreach (var s in d.Segments)
                    {
                        var p = s as PartInfo;
                        if (p == null) sb.AppendLine("   [free] @" + Fmt.Bytes(s.Offset) + " " + Fmt.Bytes(s.Size));
                        else sb.AppendLine("   #" + p.Number + " " + Look.Title(p) + " @" + Fmt.Bytes(p.Offset) + " " + Fmt.Bytes(p.Size) + " " + p.Kind + " " + p.Fs +
                                           (p.HasVolume ? " free " + Fmt.Bytes(p.VolFree) : "") + (p.Protected != null ? " [protected]" : "") + (p.CanResize ? " [resizable]" : ""));
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "disks-list.txt"), sb.ToString());
        }
    }
}
