namespace CodeEditor.Core.Documents;

/// <summary>
/// Detects a file's text encoding from its raw bytes: byte order marks first,
/// then a NUL-byte heuristic for BOM-less UTF-16, then strict UTF-8 validation
/// with Windows-1252 ("ANSI") as the legacy fallback. Pure logic so it is
/// testable without touching the file system.
/// </summary>
public static class TextEncodingDetector
{
    /// <summary>
    /// Detects the encoding of <paramref name="bytes"/> (typically the whole file,
    /// but a prefix of a few KB is enough). Empty input is treated as UTF-8.
    /// </summary>
    public static TextEncodingKind Detect(ReadOnlySpan<byte> bytes)
    {
        // UTF-32 LE's BOM starts with UTF-16 LE's, so it must be checked first.
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return TextEncodingKind.Utf32Le;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return TextEncodingKind.Utf32Be;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return TextEncodingKind.Utf16Le;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return TextEncodingKind.Utf16Be;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return TextEncodingKind.Utf8Bom;
        }

        if (DetectBomlessUtf16(bytes) is { } utf16)
        {
            return utf16;
        }

        return IsValidUtf8(bytes) ? TextEncodingKind.Utf8 : TextEncodingKind.Ansi;
    }

    /// <summary>
    /// BOM-less UTF-16 heuristic: text files never legitimately contain NUL bytes
    /// in 8-bit encodings, while UTF-16-encoded Latin text has a NUL in every
    /// other position. Odd-position NULs mean little-endian (low byte first).
    /// </summary>
    private static TextEncodingKind? DetectBomlessUtf16(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return null;
        }

        var oddNuls = 0;
        var evenNuls = 0;
        var limit = Math.Min(bytes.Length, 4096) & ~1;
        for (var i = 0; i < limit; i++)
        {
            if (bytes[i] == 0)
            {
                if ((i & 1) == 1)
                {
                    oddNuls++;
                }
                else
                {
                    evenNuls++;
                }
            }
        }

        var pairs = limit / 2;
        if (oddNuls > pairs / 2 && evenNuls == 0)
        {
            return TextEncodingKind.Utf16Le;
        }

        if (evenNuls > pairs / 2 && oddNuls == 0)
        {
            return TextEncodingKind.Utf16Be;
        }

        return null;
    }

    /// <summary>Strict UTF-8 validation (no overlongs, surrogates, or out-of-range code points).</summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            int continuationCount;
            int codePointMin;
            if (b <= 0x7F)
            {
                i++;
                continue;
            }

            if (b >= 0xC2 && b <= 0xDF)
            {
                continuationCount = 1;
                codePointMin = 0x80;
            }
            else if (b >= 0xE0 && b <= 0xEF)
            {
                continuationCount = 2;
                codePointMin = 0x800;
            }
            else if (b >= 0xF0 && b <= 0xF4)
            {
                continuationCount = 3;
                codePointMin = 0x10000;
            }
            else
            {
                return false; // 0x80–0xC1 lead byte (continuation/overlong) or > 0xF4.
            }

            if (i + continuationCount >= bytes.Length)
            {
                return false; // Sequence truncated at end of file.
            }

            var codePoint = b & (0x3F >> continuationCount);
            for (var j = 1; j <= continuationCount; j++)
            {
                var continuation = bytes[i + j];
                if ((continuation & 0xC0) != 0x80)
                {
                    return false;
                }

                codePoint = (codePoint << 6) | (continuation & 0x3F);
            }

            if (codePoint < codePointMin || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                return false;
            }

            i += continuationCount + 1;
        }

        return true;
    }
}
