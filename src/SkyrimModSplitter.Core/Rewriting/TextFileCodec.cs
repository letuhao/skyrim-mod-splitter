using System.Text;

namespace SkyrimModSplitter.Core.Rewriting;

public sealed record DecodedText(string Text, Encoding Encoding, byte[] Preamble, string EncodingName);

/// <summary>
/// Reads/writes config files while preserving their original encoding bytes:
/// UTF-8 (with/without BOM), UTF-16 LE/BE with BOM, and a byte-preserving
/// Latin1 fallback for legacy single-byte files. Tokens are ASCII, so rewriting
/// never needs to reinterpret the rest of the file.
/// </summary>
public static class TextFileCodec
{
    public static DecodedText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new DecodedText(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true), new byte[] { 0xEF, 0xBB, 0xBF }, "utf-8-bom");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new DecodedText(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode, new byte[] { 0xFF, 0xFE }, "utf-16le");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode, new byte[] { 0xFE, 0xFF }, "utf-16be");
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return new DecodedText(strict.GetString(bytes), strict, Array.Empty<byte>(), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            // Latin1 is byte-preserving for the whole 0x00-0xFF range, so every
            // non-token byte round-trips unchanged.
            return new DecodedText(Encoding.Latin1.GetString(bytes), Encoding.Latin1, Array.Empty<byte>(), "latin1-fallback");
        }
    }

    public static void Write(string path, DecodedText document, string text)
    {
        var body = document.Encoding.GetBytes(text);
        using var fs = File.Create(path);
        if (document.Preamble.Length > 0) fs.Write(document.Preamble);
        fs.Write(body);
    }
}
