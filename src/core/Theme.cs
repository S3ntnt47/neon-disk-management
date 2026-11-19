using System;

namespace DiskManager
{
    /// <summary>MarcusUI / Aetheria palette (same as Undelete).</summary>
    static class T
    {
        public const int Bg = 0x0c0a17, Panel = 0x13101f, PanelAlt = 0x1b1630, Text = 0xcfc8e0, Dim = 0x6e6688;
        public const int Crimson = 0xe8194f, Teal = 0x1fc2b0, Magenta = 0xd63fa0, Purple = 0x9a4fd0;
        public const int Blue = 0x4b7fe0, Lime = 0xc9dc4a, Orange = 0xf2653a, Dark = 0x0c0a17;
    }

    static class Fmt
    {
        public const long MiB = 1L << 20, GiB = 1L << 30;

        public static string Bytes(long b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return i == 0 ? b + " B" : v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + u[i];
        }

        /// <summary>Parses "500", "500 MB", "12.5 GB", "1 TB" into bytes (plain numbers are MB).</summary>
        public static bool TryParseSize(string s, out long bytes)
        {
            bytes = 0;
            if (s == null) return false;
            s = s.Trim().ToUpperInvariant().Replace(" ", "").Replace(",", ".");   // accept "12,5 GB" as well as "12.5 GB"
            long mul = MiB;
            string[] units = { "TB", "GB", "MB", "KB", "T", "G", "M", "K" };
            long[] muls = { 1L << 40, GiB, MiB, 1L << 10, 1L << 40, GiB, MiB, 1L << 10 };
            for (int i = 0; i < units.Length; i++)
                if (s.EndsWith(units[i])) { mul = muls[i]; s = s.Substring(0, s.Length - units[i].Length).Trim(); break; }
            double v;
            if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) || v < 0) return false;
            bytes = (long)(v * mul);
            return true;
        }

        /// <summary>Size in the unit people type: whole MB below 10 GB, otherwise GB with two decimals.</summary>
        public static string Editable(long b)
        {
            if (b < 10 * GiB) return (b / MiB) + " MB";
            return (b / (double)GiB).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }
    }
}
