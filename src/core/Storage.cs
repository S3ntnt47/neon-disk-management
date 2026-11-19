using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;

namespace DiskManager
{
    /// <summary>A slice of a disk: either a partition or unallocated space.</summary>
    abstract class Segment
    {
        public DiskInfo Disk;
        public long Offset, Size;
        public long End { get { return Offset + Size; } }
        public abstract bool IsFree { get; }
    }

    sealed class FreeSpace : Segment
    {
        public override bool IsFree { get { return true; } }
    }

    sealed class PartInfo : Segment
    {
        public override bool IsFree { get { return false; } }
        public int Number;
        public char Letter;                 // '\0' when none
        public string Kind = "";            // "basic data", "EFI system", ...
        public string GptType;
        public int MbrType;
        public bool IsSystem, IsBoot, IsHidden, IsActive, IsReadOnly, IsOffline;
        public string[] AccessPaths = new string[0];
        public ManagementObject Mo;

        // volume (null when the partition holds no mountable file system)
        public bool HasVolume;
        public string Fs = "", Label = "", VolumePath = "";
        public long VolSize, VolFree;
        public ManagementObject VolMo;

        public string Name
        {
            get
            {
                string s = Letter != '\0' ? Letter + ":" : "#" + Number;
                if (Label.Length > 0) s += " " + Label;
                return s;
            }
        }

        /// <summary>Windows can only resize NTFS and ReFS volumes.</summary>
        public bool CanResize { get { return HasVolume && (Fs == "NTFS" || Fs == "ReFS"); } }

        /// <summary>Partitions Windows needs to start; the app refuses to delete these.</summary>
        public string Protected
        {
            get
            {
                if (IsBoot) return "it holds the running copy of Windows";
                if (IsSystem) return "it is the active system/EFI partition the PC boots from";
                if (VolumePath.Length > 0 && Environment.SystemDirectory.Length > 1 && Letter == char.ToUpperInvariant(Environment.SystemDirectory[0]))
                    return "it holds the running copy of Windows";
                return null;
            }
        }
    }

    sealed class DiskInfo
    {
        public int Number;
        public string Name = "", Bus = "", Style = "";
        public int StyleCode;               // 0 raw, 1 MBR, 2 GPT
        public long Size, LargestFree;
        public bool IsBoot, IsSystem, IsOffline, IsReadOnly;
        public ManagementObject Mo;
        public List<PartInfo> Partitions = new List<PartInfo>();
        public List<Segment> Segments = new List<Segment>();
        public long FreeTotal { get { return Segments.Where(s => s.IsFree).Sum(s => s.Size); } }
        public bool Editable { get { return !IsOffline && !IsReadOnly; } }
    }

    /// <summary>Thrown when a storage operation reports failure.</summary>
    sealed class StorageException : Exception
    {
        public StorageException(string msg) : base(msg) { }
    }

    /// <summary>
    /// Talks to the Windows Storage Management API (root\Microsoft\Windows\Storage) -
    /// the same API Disk Management and the PowerShell storage cmdlets use.
    /// </summary>
    static class Storage
    {
        const string Ns = @"\\.\root\Microsoft\Windows\Storage";
        static ManagementScope scope;
        static readonly object gate = new object();

        static ManagementScope Scope
        {
            get
            {
                lock (gate)
                {
                    if (scope == null) { var s = new ManagementScope(Ns); s.Connect(); scope = s; }
                    return scope;
                }
            }
        }

        static ManagementObjectCollection Query(string wql)
        {
            return new ManagementObjectSearcher(Scope, new ObjectQuery(wql)).Get();
        }

        static long L(object o) { return o == null ? 0 : Convert.ToInt64(o); }
        static int I(object o) { return o == null ? 0 : Convert.ToInt32(o); }
        static bool B(object o) { return o != null && Convert.ToBoolean(o); }
        static string S(object o) { return o == null ? "" : o.ToString().Trim(); }

        // ───────────────────────────── reading ─────────────────────────────

        public static List<DiskInfo> Load()
        {
            var disks = new List<DiskInfo>();
            foreach (ManagementObject m in Query("SELECT * FROM MSFT_Disk"))
            {
                var d = new DiskInfo
                {
                    Mo = m,
                    Number = I(m["Number"]),
                    Name = S(m["FriendlyName"]),
                    Size = L(m["Size"]),
                    LargestFree = L(m["LargestFreeExtent"]),
                    StyleCode = I(m["PartitionStyle"]),
                    IsBoot = B(m["IsBoot"]), IsSystem = B(m["IsSystem"]),
                    IsOffline = B(m["IsOffline"]), IsReadOnly = B(m["IsReadOnly"]),
                    Bus = BusName(I(m["BusType"])),
                };
                d.Style = d.StyleCode == 1 ? "MBR" : d.StyleCode == 2 ? "GPT" : "RAW";
                if (d.Name.Length == 0) d.Name = "Disk " + d.Number;
                disks.Add(d);
            }
            disks.Sort((a, b) => a.Number.CompareTo(b.Number));

            var vols = new Dictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject v in Query("SELECT * FROM MSFT_Volume"))
            {
                string p = S(v["Path"]);
                if (p.Length > 0) vols[p] = v;
            }

            foreach (ManagementObject m in Query("SELECT * FROM MSFT_Partition"))
            {
                var d = disks.FirstOrDefault(x => x.Number == I(m["DiskNumber"]));
                if (d == null) continue;
                var p = new PartInfo
                {
                    Mo = m, Disk = d,
                    Number = I(m["PartitionNumber"]),
                    Offset = L(m["Offset"]), Size = L(m["Size"]),
                    GptType = S(m["GptType"]).ToLowerInvariant(),
                    MbrType = I(m["MbrType"]),
                    IsSystem = B(m["IsSystem"]), IsBoot = B(m["IsBoot"]), IsHidden = B(m["IsHidden"]),
                    IsActive = B(m["IsActive"]), IsReadOnly = B(m["IsReadOnly"]), IsOffline = B(m["IsOffline"]),
                };
                object dl = m["DriveLetter"];
                if (dl != null) { char c = Convert.ToChar(dl); if (char.IsLetter(c)) p.Letter = char.ToUpperInvariant(c); }
                var ap = m["AccessPaths"] as string[];
                if (ap != null) p.AccessPaths = ap;
                p.Kind = KindName(p, d.StyleCode);

                ManagementObject v = null;
                foreach (var path in p.AccessPaths)
                    if (path.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase) && vols.TryGetValue(path, out v)) break;
                if (v == null)
                    try { foreach (ManagementObject x in m.GetRelated("MSFT_Volume")) { v = x; break; } } catch { }
                if (v != null)
                {
                    p.VolMo = v;
                    p.HasVolume = true;
                    p.Fs = S(v["FileSystem"]);
                    p.Label = S(v["FileSystemLabel"]);
                    p.VolumePath = S(v["Path"]);
                    p.VolSize = L(v["Size"]);
                    p.VolFree = L(v["SizeRemaining"]);
                    if (p.Fs.Length == 0) p.Fs = "RAW";
                }
                d.Partitions.Add(p);
            }

            foreach (var d in disks) BuildSegments(d);
            return disks;
        }

        /// <summary>Partitions in disk order with the unallocated gaps between them.</summary>
        static void BuildSegments(DiskInfo d)
        {
            d.Partitions.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            d.Segments.Clear();
            if (d.StyleCode == 0) return;   // uninitialised disk - nothing to lay out
            // GPT reserves 1 MiB up front by convention and its backup table at the end
            long pos = Fmt.MiB;
            long usableEnd = d.Size - (d.StyleCode == 2 ? Fmt.MiB : 0);
            const long minGap = 8 * Fmt.MiB;   // ignore alignment slack
            foreach (var p in d.Partitions)
            {
                if (p.Offset - pos >= minGap) d.Segments.Add(Free(d, pos, p.Offset));
                d.Segments.Add(p);
                pos = Math.Max(pos, p.End);
            }
            if (usableEnd - pos >= minGap) d.Segments.Add(Free(d, pos, usableEnd));
        }

        static FreeSpace Free(DiskInfo d, long start, long end)
        {
            long a = AlignUp(start);
            return new FreeSpace { Disk = d, Offset = a, Size = AlignDown(end) - a };
        }

        public static long AlignUp(long v) { return (v + Fmt.MiB - 1) / Fmt.MiB * Fmt.MiB; }
        public static long AlignDown(long v) { return v / Fmt.MiB * Fmt.MiB; }

        static string KindName(PartInfo p, int style)
        {
            if (style == 2)
            {
                switch (p.GptType.Trim('{', '}'))
                {
                    case "c12a7328-f81f-11d2-ba4b-00a0c93ec93b": return "EFI system";
                    case "e3c9e316-0b5c-4db8-817d-f92df00215ae": return "MS reserved";
                    case "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7": return "basic data";
                    case "de94bba4-06d1-4d40-a16a-bfd50179d6ac": return "recovery";
                    case "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3": return "LDM metadata";
                    case "af9b60a0-1431-4f62-bc68-3311714a69ad": return "LDM data";
                    case "0fc63daf-8483-4772-8e79-3d69d8477de4": return "linux";
                    case "0657fd6d-a4ab-43c4-84e5-0933c84b4f4f": return "linux swap";
                    case "e6d6d379-f507-44c2-a23c-238f2a3df928": return "linux LVM";
                    case "21686148-6449-6e6f-744e-656564454649": return "BIOS boot";
                    default: return "unknown";
                }
            }
            switch (p.MbrType)
            {
                case 0x07: return "basic data";
                case 0x01: case 0x04: case 0x06: case 0x0B: case 0x0C: case 0x0E: return "FAT";
                case 0x05: case 0x0F: return "extended";
                case 0x27: return "recovery";
                case 0x42: return "dynamic";
                case 0x82: return "linux swap";
                case 0x83: return "linux";
                case 0xEF: return "EFI system";
                default: return "type 0x" + p.MbrType.ToString("X2");
            }
        }

        static string BusName(int b)
        {
            switch (b)
            {
                case 1: return "SCSI"; case 2: return "ATAPI"; case 3: return "ATA"; case 4: return "1394";
                case 5: return "SSA"; case 6: return "Fibre"; case 7: return "USB"; case 8: return "RAID";
                case 9: return "iSCSI"; case 10: return "SAS"; case 11: return "SATA"; case 12: return "SD";
                case 13: return "MMC"; case 15: return "VHD"; case 16: return "Spaces"; case 17: return "NVMe";
                default: return "disk";
            }
        }

        // ───────────────────────────── operations ─────────────────────────────

        static ManagementBaseObject Call(ManagementObject target, string method, Dictionary<string, object> args)
        {
            ManagementBaseObject inp = null;
            if (args != null && args.Count > 0)
            {
                inp = target.GetMethodParameters(method);
                foreach (var kv in args) inp[kv.Key] = kv.Value;
            }
            ManagementBaseObject outp;
            try { outp = target.InvokeMethod(method, inp, null); }
            catch (ManagementException ex) { throw new StorageException(ex.Message.Trim()); }
            catch (UnauthorizedAccessException) { throw new StorageException("Access denied - restart the app as administrator."); }
            uint rv = Convert.ToUInt32(outp["ReturnValue"]);
            if (rv != 0) throw new StorageException(Describe(rv, outp));
            return outp;
        }

        static string Describe(uint code, ManagementBaseObject outp)
        {
            string ext = null;
            try
            {
                object es = outp.Properties.Cast<PropertyData>().Any(p => p.Name == "ExtendedStatus") ? outp["ExtendedStatus"] : null;
                var mbo = es as ManagementBaseObject;
                if (mbo != null) ext = S(mbo["Message"]);
            }
            catch { }
            string known;
            switch (code)
            {
                case 1: known = "Not supported."; break;
                case 2: known = "Unspecified error."; break;
                case 3: known = "Timed out."; break;
                case 4: known = "Failed."; break;
                case 5: known = "Invalid parameter."; break;
                case 40001: known = "Access denied - restart the app as administrator."; break;
                case 40002: known = "Not enough resources."; break;
                case 41000: known = "The disk is offline."; break;
                case 41001: known = "The disk is read-only."; break;
                case 42002: known = "The requested size is not supported (out of range)."; break;
                case 42004: known = "The partition is not shrinkable/extendable this far - unmovable files or no adjacent free space."; break;
                default: known = "Error " + code + "."; break;
            }
            return string.IsNullOrEmpty(ext) ? known + " (code " + code + ")" : ext + " (code " + code + ")";
        }

        /// <summary>Smallest and largest size Windows will accept for this partition (asks the file system; can take a while).</summary>
        public static void SupportedSize(PartInfo p, out long min, out long max)
        {
            var o = Call(p.Mo, "GetSupportedSize", null);
            min = L(o["SizeMin"]);
            max = L(o["SizeMax"]);
        }

        public static void Resize(PartInfo p, long size)
        {
            Call(p.Mo, "Resize", new Dictionary<string, object> { { "Size", (ulong)size } });
        }

        public static void Delete(PartInfo p)
        {
            Call(p.Mo, "DeleteObject", null);
        }

        public static void Initialize(DiskInfo d, bool gpt)
        {
            Call(d.Mo, "Initialize", new Dictionary<string, object> { { "PartitionStyle", (ushort)(gpt ? 2 : 1) } });
        }

        public static void SetLetter(PartInfo p, char newLetter)
        {
            if (p.Letter != '\0')
                Call(p.Mo, "RemoveAccessPath", new Dictionary<string, object> { { "AccessPath", p.Letter + @":\" } });
            if (newLetter != '\0')
                Call(p.Mo, "AddAccessPath", new Dictionary<string, object> { { "AccessPath", newLetter + @":\" } });
        }

        /// <summary>Creates a partition inside a free gap and formats it. letter '\0' = none, '*' = automatic.</summary>
        public static void CreateAndFormat(FreeSpace gap, long size, char letter, string fs, string label, Action<string> progress)
        {
            var d = gap.Disk;
            size = AlignDown(Math.Min(size, gap.Size));
            if (size < Fmt.MiB) throw new StorageException("The partition must be at least 1 MB.");
            var args = new Dictionary<string, object>
            {
                { "Offset", (ulong)gap.Offset },
                { "Size", (ulong)size },
                { "AssignDriveLetter", letter == '*' },
            };
            if (d.StyleCode == 2) args["GptType"] = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";
            else args["MbrType"] = (ushort)(fs == "FAT32" ? 0x0C : 0x07);

            progress("creating partition…");
            Call(d.Mo, "CreatePartition", args);

            // find the new partition (it starts at or just after the requested offset)
            ManagementObject part = null;
            for (int i = 0; i < 20 && part == null; i++)
            {
                foreach (ManagementObject m in Query("SELECT * FROM MSFT_Partition WHERE DiskNumber = " + d.Number))
                {
                    long off = L(m["Offset"]);
                    if (off >= gap.Offset && off < gap.Offset + 16 * Fmt.MiB) { part = m; break; }
                }
                if (part == null) Thread.Sleep(250);
            }
            if (part == null) throw new StorageException("The partition was created but could not be found to format it. Refresh and format it from Explorer.");

            if (letter != '\0' && letter != '*')
            {
                progress("assigning " + letter + ":…");
                try { Call(part, "AddAccessPath", new Dictionary<string, object> { { "AccessPath", letter + @":\" } }); }
                catch (StorageException ex) { progress("could not assign " + letter + ": - " + ex.Message); }
            }

            progress("formatting " + fs + "…");
            ManagementObject vol = null;
            for (int i = 0; i < 40 && vol == null; i++)
            {
                try { part.Get(); foreach (ManagementObject v in part.GetRelated("MSFT_Volume")) { vol = v; break; } } catch { }
                if (vol == null) Thread.Sleep(250);
            }
            if (vol == null) throw new StorageException("The partition was created but Windows did not expose a volume to format.");
            Call(vol, "Format", new Dictionary<string, object>
            {
                { "FileSystem", fs },
                { "FileSystemLabel", label ?? "" },
                { "Full", false },
            });
        }

        /// <summary>Asks Windows to rescan disks so the next Load sees fresh data.</summary>
        public static void Rescan()
        {
            try
            {
                using (var c = new ManagementClass(Scope, new ManagementPath("MSFT_StorageSetting"), null))
                    c.InvokeMethod("UpdateHostStorageCache", null, null);
            }
            catch { }
        }

        public static char[] FreeLetters()
        {
            var used = new HashSet<char>(System.IO.DriveInfo.GetDrives().Select(x => char.ToUpperInvariant(x.Name[0])));
            return Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).Where(c => !used.Contains(c)).ToArray();
        }
    }
}
