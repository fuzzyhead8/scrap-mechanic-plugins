using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ScrapMechanicModManager.Core.Validation;

internal static class PortableExecutableVersionReader
{
    private const uint ResourceDirectoryFlag = 0x80000000;
    private const uint ResourceOffsetMask = 0x7fffffff;
    private const uint FixedFileInfoSignature = 0xfeef04bd;
    private const int VersionResourceType = 16;
    private const int ResourceDirectoryHeaderSize = 16;
    private const int ResourceDirectoryEntrySize = 8;
    private const int ResourceDataEntrySize = 16;
    private const int FixedFileInfoSize = 52;
    private const int MaximumVersionResourceSize = 1024 * 1024;

    public static string? TryReadProductVersion(string executablePath)
    {
        try
        {
            using var stream = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var peReader = new PEReader(stream);

            DirectoryEntry resourceDirectory =
                peReader.PEHeaders.PEHeader?.ResourceTableDirectory ?? default;
            if (resourceDirectory.RelativeVirtualAddress == 0 || resourceDirectory.Size == 0)
            {
                return null;
            }

            PEMemoryBlock resourceBlock = peReader.GetSectionData(
                resourceDirectory.RelativeVirtualAddress);

            if (!TryFindEntryById(
                    resourceBlock,
                    directoryOffset: 0,
                    VersionResourceType,
                    out ResourceEntry typeEntry)
                || !typeEntry.IsDirectory
                || !TryGetFirstEntry(
                    resourceBlock,
                    typeEntry.Offset,
                    expectDirectory: true,
                    out ResourceEntry nameEntry)
                || !TryGetFirstEntry(
                    resourceBlock,
                    nameEntry.Offset,
                    expectDirectory: false,
                    out ResourceEntry languageEntry)
                || !TryReadDataEntry(
                    resourceBlock,
                    languageEntry.Offset,
                    out int dataRva,
                    out int dataSize))
            {
                return null;
            }

            PEMemoryBlock versionBlock = peReader.GetSectionData(dataRva);
            if (dataSize > versionBlock.Length)
            {
                return null;
            }

            BlobReader versionReader = versionBlock.GetReader(0, dataSize);
            return TryReadFixedProductVersion(ref versionReader);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool TryFindEntryById(
        PEMemoryBlock resourceBlock,
        int directoryOffset,
        int expectedId,
        out ResourceEntry entry)
    {
        entry = default;
        if (!TryReadDirectory(
                resourceBlock,
                directoryOffset,
                out int entryCount,
                out int entriesOffset))
        {
            return false;
        }

        for (int index = 0; index < entryCount; index++)
        {
            if (!TryReadEntry(resourceBlock, entriesOffset, index, out ResourceEntry candidate))
            {
                return false;
            }

            if (!candidate.HasName && candidate.Id == expectedId)
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFirstEntry(
        PEMemoryBlock resourceBlock,
        int directoryOffset,
        bool expectDirectory,
        out ResourceEntry entry)
    {
        entry = default;
        if (!TryReadDirectory(
                resourceBlock,
                directoryOffset,
                out int entryCount,
                out int entriesOffset))
        {
            return false;
        }

        for (int index = 0; index < entryCount; index++)
        {
            if (!TryReadEntry(resourceBlock, entriesOffset, index, out ResourceEntry candidate))
            {
                return false;
            }

            if (candidate.IsDirectory == expectDirectory)
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDirectory(
        PEMemoryBlock resourceBlock,
        int directoryOffset,
        out int entryCount,
        out int entriesOffset)
    {
        entryCount = 0;
        entriesOffset = 0;
        if (!CanRead(resourceBlock, directoryOffset, ResourceDirectoryHeaderSize))
        {
            return false;
        }

        BlobReader reader = resourceBlock.GetReader(
            directoryOffset,
            ResourceDirectoryHeaderSize);
        reader.Offset = 12;
        ushort namedEntryCount = reader.ReadUInt16();
        ushort idEntryCount = reader.ReadUInt16();
        entryCount = namedEntryCount + idEntryCount;
        entriesOffset = directoryOffset + ResourceDirectoryHeaderSize;

        long entriesSize = (long)entryCount * ResourceDirectoryEntrySize;
        return entriesSize <= int.MaxValue
            && CanRead(resourceBlock, entriesOffset, (int)entriesSize);
    }

    private static bool TryReadEntry(
        PEMemoryBlock resourceBlock,
        int entriesOffset,
        int index,
        out ResourceEntry entry)
    {
        entry = default;
        long offset = entriesOffset + ((long)index * ResourceDirectoryEntrySize);
        if (offset > int.MaxValue
            || !CanRead(resourceBlock, (int)offset, ResourceDirectoryEntrySize))
        {
            return false;
        }

        BlobReader reader = resourceBlock.GetReader(
            (int)offset,
            ResourceDirectoryEntrySize);
        uint name = reader.ReadUInt32();
        uint target = reader.ReadUInt32();
        uint relativeOffset = target & ResourceOffsetMask;
        if (relativeOffset > int.MaxValue)
        {
            return false;
        }

        entry = new ResourceEntry(
            HasName: (name & ResourceDirectoryFlag) != 0,
            Id: (int)(name & ResourceOffsetMask),
            IsDirectory: (target & ResourceDirectoryFlag) != 0,
            Offset: (int)relativeOffset);
        return true;
    }

    private static bool TryReadDataEntry(
        PEMemoryBlock resourceBlock,
        int dataEntryOffset,
        out int dataRva,
        out int dataSize)
    {
        dataRva = 0;
        dataSize = 0;
        if (!CanRead(resourceBlock, dataEntryOffset, ResourceDataEntrySize))
        {
            return false;
        }

        BlobReader reader = resourceBlock.GetReader(
            dataEntryOffset,
            ResourceDataEntrySize);
        uint rawDataRva = reader.ReadUInt32();
        uint rawDataSize = reader.ReadUInt32();
        if (rawDataRva > int.MaxValue
            || rawDataSize == 0
            || rawDataSize > MaximumVersionResourceSize)
        {
            return false;
        }

        dataRva = (int)rawDataRva;
        dataSize = (int)rawDataSize;
        return true;
    }

    private static string? TryReadFixedProductVersion(ref BlobReader reader)
    {
        if (reader.Length < 6)
        {
            return null;
        }

        ushort totalLength = reader.ReadUInt16();
        ushort valueLength = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        if (totalLength < 6 || totalLength > reader.Length)
        {
            return null;
        }

        const string expectedKey = "VS_VERSION_INFO";
        foreach (char expectedCharacter in expectedKey)
        {
            if (!CanRead(reader, totalLength, sizeof(ushort))
                || reader.ReadUInt16() != expectedCharacter)
            {
                return null;
            }
        }

        if (!CanRead(reader, totalLength, sizeof(ushort)) || reader.ReadUInt16() != 0)
        {
            return null;
        }

        int alignedOffset = AlignToDword(reader.Offset);
        if (alignedOffset > totalLength
            || valueLength < FixedFileInfoSize
            || totalLength - alignedOffset < FixedFileInfoSize)
        {
            return null;
        }

        reader.Offset = alignedOffset;
        if (reader.ReadUInt32() != FixedFileInfoSignature)
        {
            return null;
        }

        _ = reader.ReadUInt32();
        uint fileVersionMs = reader.ReadUInt32();
        uint fileVersionLs = reader.ReadUInt32();
        uint productVersionMs = reader.ReadUInt32();
        uint productVersionLs = reader.ReadUInt32();

        if (productVersionMs == 0 && productVersionLs == 0)
        {
            productVersionMs = fileVersionMs;
            productVersionLs = fileVersionLs;
        }

        return FormatVersion(productVersionMs, productVersionLs);
    }

    private static bool CanRead(PEMemoryBlock block, int offset, int size)
    {
        return offset >= 0
            && size >= 0
            && offset <= block.Length
            && size <= block.Length - offset;
    }

    private static bool CanRead(BlobReader reader, int limit, int size)
    {
        return size >= 0
            && reader.Offset <= limit
            && size <= limit - reader.Offset;
    }

    private static int AlignToDword(int value)
    {
        return checked((value + 3) & ~3);
    }

    private static string FormatVersion(uint mostSignificant, uint leastSignificant)
    {
        return $"{mostSignificant >> 16}.{mostSignificant & 0xffff}."
            + $"{leastSignificant >> 16}.{leastSignificant & 0xffff}";
    }

    private readonly record struct ResourceEntry(
        bool HasName,
        int Id,
        bool IsDirectory,
        int Offset);
}
