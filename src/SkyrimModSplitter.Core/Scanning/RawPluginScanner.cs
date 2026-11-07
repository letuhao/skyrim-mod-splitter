using System.Security.Cryptography;
using System.Text;

namespace SkyrimModSplitter.Core.Scanning;

public static class RawPluginScanner
{
    public static RawPluginInfo Scan(string path)
    {
        var info = new RawPluginInfo
        {
            File = Path.GetFileName(path),
            FullPath = Path.GetFullPath(path),
            SizeBytes = new FileInfo(path).Length,
        };
        using (var sha = SHA256.Create())
        using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            info.Sha256 = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        try
        {
            var sig = ReadSig(br);
            if (sig != "TES4")
            {
                info.Errors.Add("first record signature is " + sig + ", expected TES4");
                return info;
            }
            var dataSize = br.ReadUInt32();
            info.FlagsRaw = br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt32();
            info.HeaderVersion = br.ReadUInt16();
            br.ReadUInt16();
            var headerEnd = 24L + dataSize;
            while (stream.Position < headerEnd)
            {
                var sub = ReadSig(br);
                var len = br.ReadUInt16();
                var next = stream.Position + len;
                switch (sub)
                {
                    case "HEDR":
                        info.HedrVersion = br.ReadSingle();
                        info.NumRecordsStated = br.ReadUInt32();
                        info.NextFormId = br.ReadUInt32();
                        break;
                    case "MAST":
                        info.Masters.Add(Encoding.ASCII.GetString(br.ReadBytes(len)).TrimEnd('\0'));
                        break;
                    default:
                        info.HeaderSubrecords.Add(sub);
                        br.ReadBytes(len);
                        break;
                }
                if (stream.Position != next) stream.Position = next;
            }
            stream.Position = headerEnd;
            while (stream.Position + 24 <= stream.Length)
            {
                var gsig = ReadSig(br);
                var gsize = br.ReadUInt32();
                if (gsig != "GRUP")
                {
                    info.Errors.Add("expected GRUP at 0x" + (stream.Position - 8).ToString("X") + ", got " + gsig + "; stopping walk");
                    break;
                }
                if (gsize == 0) { info.Errors.Add("zero-size GRUP"); break; }
                var groupStart = stream.Position - 8;
                WalkGroup(br, stream, groupStart + 24, groupStart + gsize, info);
                stream.Position = groupStart + gsize;
            }
        }
        catch (Exception ex)
        {
            info.Errors.Add("raw scan failed: " + ex.Message);
        }
        return info;
    }

    private static void WalkGroup(BinaryReader br, Stream stream, long contentStart, long groupEnd, RawPluginInfo info)
    {
        var pos = contentStart;
        while (pos + 24 <= groupEnd && pos + 24 <= stream.Length)
        {
            stream.Position = pos;
            var sig = ReadSig(br);
            var size = br.ReadUInt32();
            if (sig == "GRUP")
            {
                var subEnd = pos + size;
                if (subEnd <= pos || subEnd > stream.Length) { info.Errors.Add("bad GRUP size at 0x" + pos.ToString("X")); return; }
                WalkGroup(br, stream, pos + 24, subEnd, info);
                pos = subEnd;
                continue;
            }
            br.ReadUInt32();
            var formId = br.ReadUInt32();
            br.ReadUInt32();
            br.ReadUInt16();
            br.ReadUInt16();
            info.RecordCounts[sig] = info.RecordCounts.TryGetValue(sig, out var c) ? c + 1 : 1;
            info.TotalRecords++;
            if (formId != 0)
            {
                if (formId < info.MinRecordFormId) info.MinRecordFormId = formId;
                if (formId > info.MaxRecordFormId) info.MaxRecordFormId = formId;
            }
            pos = pos + 24 + size;
        }
    }

    private static string ReadSig(BinaryReader br)
    {
        var b = br.ReadBytes(4);
        return Encoding.ASCII.GetString(b);
    }
}
