using System.Buffers.Binary;

namespace BijliGuard.Domain.Billing;

/// <summary>
/// Removes location, camera and other metadata from JPEG and PNG files before they are stored
/// (technical design section 7.3). Pixels and colour profiles are kept. Pure byte work, no I/O.
/// Unknown or malformed input is returned unchanged rather than guessed at.
/// </summary>
public static class ImageMetadata
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // PNG text and metadata chunks. Pixel and colour chunks are kept.
    private static readonly HashSet<string> PngDropChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    public static byte[] Strip(string contentType, byte[] content) => contentType switch
    {
        "image/jpeg" => StripJpeg(content),
        "image/png" => StripPng(content),
        _ => content
    };

    /// <summary>Drops APP1 (Exif, XMP), APP13 (IPTC) and COM segments. Keeps APP0, APP2 (ICC colour) and the rest.</summary>
    public static byte[] StripJpeg(byte[] input)
    {
        if (input.Length < 4 || input[0] != 0xFF || input[1] != 0xD8) return input;

        using var output = new MemoryStream(input.Length);
        output.Write(input, 0, 2); // SOI
        var pos = 2;
        while (pos + 1 < input.Length)
        {
            if (input[pos] != 0xFF) return input; // malformed: keep the original rather than corrupt it
            var marker = input[pos + 1];

            // Start of scan: the entropy-coded image data runs to the end of the file. Copy the rest unchanged.
            if (marker == 0xDA)
            {
                output.Write(input, pos, input.Length - pos);
                return output.ToArray();
            }
            if (marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                output.Write(input, pos, 2);
                pos += 2;
                continue;
            }
            if (pos + 4 > input.Length) return input;

            var length = BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(pos + 2, 2));
            var segmentEnd = pos + 2 + length;
            if (length < 2 || segmentEnd > input.Length) return input;

            var drop = marker is 0xE1 or 0xED or 0xFE;
            if (!drop) output.Write(input, pos, segmentEnd - pos);
            pos = segmentEnd;
        }
        return output.ToArray();
    }

    /// <summary>Drops text and Exif chunks. Each chunk is length (4 bytes), type (4), data, CRC (4).</summary>
    public static byte[] StripPng(byte[] input)
    {
        if (input.Length < PngSignature.Length || !input.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            return input;

        using var output = new MemoryStream(input.Length);
        output.Write(input, 0, PngSignature.Length);
        var pos = PngSignature.Length;
        while (pos + 12 <= input.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(pos, 4));
            var type = System.Text.Encoding.ASCII.GetString(input, pos + 4, 4);
            var chunkEnd = pos + 12 + (long)length;
            if (chunkEnd > input.Length) return input;

            if (!PngDropChunks.Contains(type))
                output.Write(input, pos, (int)(chunkEnd - pos));
            pos = (int)chunkEnd;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }
}
