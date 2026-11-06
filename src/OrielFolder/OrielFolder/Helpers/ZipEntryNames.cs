using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OrielFolder.Helpers;

/// <summary>
/// Reads filename bytes and EFS flags together because .NET 7/8 ignores EFS when a custom
/// ZipArchive encoding is supplied. Entry objects still handle data, CRC and compression.
/// </summary>
internal static class ZipEntryNames
{
    internal static IReadOnlyDictionary<ZipArchiveEntry, string> Read(
        Stream seekable, ZipArchive archive, int legacyCodePage)
    {
        if (!seekable.CanSeek || !seekable.CanRead)
            throw new ArgumentException("ZIP name metadata requires a readable, seekable stream.", nameof(seekable));
        long originalPosition = seekable.Position;
        try
        {
            var entries = archive.Entries;
            var fallback = ZipEntryNameEncoding.ForCodePage(legacyCodePage);
            int tailLength = (int)Math.Min(seekable.Length, 22 + ushort.MaxValue);
            seekable.Position = seekable.Length - tailLength;
            byte[] tail = ReadBlock(seekable, tailLength);
            int footer = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length)
                {
                    footer = i;
                    break;
                }
            }
            if (footer < 0) throw new InvalidDataException("ZIP end record is missing or truncated.");
            if (U16(tail, footer + 4) != 0 || U16(tail, footer + 6) != 0)
                throw new InvalidDataException("Multi-disk ZIP archives are unsupported.");
            ulong count = U16(tail, footer + 10);
            ulong directorySize = U32(tail, footer + 12);
            ulong directoryOffset = U32(tail, footer + 16);
            long metadataStart = seekable.Length - tailLength + footer;

            bool requiresZip64 = directorySize == uint.MaxValue || directoryOffset == uint.MaxValue;
            // Exactly 65,535 entries also fits a classic ZIP. The boundary count alone
            // does not require ZIP64 unless a locator is actually present.
            if (count == ushort.MaxValue && metadataStart >= 20)
            {
                seekable.Position = metadataStart - 20;
                requiresZip64 |= U32(ReadBlock(seekable, 4), 0) == 0x07064b50;
            }
            if (requiresZip64)
            {
                if (metadataStart < 20) throw new InvalidDataException("ZIP64 locator is missing.");
                seekable.Position = metadataStart - 20;
                byte[] locator = ReadBlock(seekable, 20);
                if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
                    throw new InvalidDataException("Invalid or multi-disk ZIP64 locator.");
                long recordOffset = checked((long)U64(locator, 8));
                if (recordOffset < 0 || recordOffset > metadataStart - 20 - 56)
                    throw new InvalidDataException("ZIP64 end record is outside the archive.");
                seekable.Position = recordOffset;
                byte[] record = ReadBlock(seekable, 56);
                ulong recordSize = U64(record, 4);
                if (U32(record, 0) != 0x06064b50 || recordSize < 44 ||
                    recordSize > (ulong)(metadataStart - 20 - recordOffset - 12) ||
                    U32(record, 16) != 0 || U32(record, 20) != 0 || U64(record, 24) != U64(record, 32))
                    throw new InvalidDataException("Invalid ZIP64 end record.");
                count = U64(record, 32);
                directorySize = U64(record, 40);
                directoryOffset = U64(record, 48);
                metadataStart = recordOffset;
            }
            else if (U16(tail, footer + 8) != count)
            {
                throw new InvalidDataException("ZIP entry counts disagree.");
            }

            if (count != (ulong)entries.Count || directoryOffset > (ulong)metadataStart ||
                directorySize > (ulong)metadataStart - directoryOffset)
                throw new InvalidDataException("ZIP central directory is outside its declared bounds.");
            long directoryEnd = checked((long)(directoryOffset + directorySize));
            seekable.Position = checked((long)directoryOffset);
            var result = new Dictionary<ZipArchiveEntry, string>(entries.Count);
            Span<byte> header = stackalloc byte[46];
            foreach (var entry in entries)
            {
                if (seekable.Position > directoryEnd - header.Length)
                    throw new InvalidDataException("ZIP central directory entry is truncated.");
                seekable.ReadExactly(header);
                if (U32(header, 0) != 0x02014b50)
                    throw new InvalidDataException("Invalid ZIP central directory entry.");
                int nameLength = U16(header, 28);
                int extraLength = U16(header, 30);
                int commentLength = U16(header, 32);
                if (nameLength + extraLength + commentLength > directoryEnd - seekable.Position)
                    throw new InvalidDataException("ZIP filename metadata is truncated.");
                byte[] nameBytes = ReadBlock(seekable, nameLength);
                Encoding encoding = (U16(header, 8) & 0x0800) != 0 ? Encoding.UTF8 : fallback;
                result.Add(entry, encoding.GetString(nameBytes));
                seekable.Position += extraLength + commentLength;
            }
            return result;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("ZIP filename metadata is truncated.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("ZIP filename metadata exceeds supported bounds.", ex);
        }
        finally
        {
            seekable.Position = originalPosition;
        }
    }

    private static byte[] ReadBlock(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static ulong U64(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
}
