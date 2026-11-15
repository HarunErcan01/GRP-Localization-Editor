using static GrpLocalizationEditor.Ui;
using System.Buffers.Binary;
using System.Text;

namespace GrpLocalizationEditor;

internal sealed class BaoPackage
{
    private const int IndexStart = 64;
    private const uint IndexMagic = 0x02012801;
    private const uint ObjectMagic = 0x02012802;

    internal sealed record Entry(uint Id, int Index, int Offset, int Size);
    internal sealed record External(uint Id, string Name, int Row, int Offset, int Size);
    internal sealed record Clip(Entry Header, uint AudioId, bool Streamed, int Codec, int Channels,
        int Rate, int Samples, int Size, bool Looped, bool Prefetched)
    {
        public string Format => Codec == 4 ? "Ogg Vorbis" : T("BaoCodec0", Codec);
        public double Seconds => Rate > 0 ? (double)Samples / Rate : 0;
    }

    public string Path { get; }
    public int TableOffset { get; }
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<External> Externals { get; }
    public IReadOnlyList<Clip> Clips { get; }

    private BaoPackage(string path, int tableOffset, List<Entry> entries, List<External> externals, List<Clip> clips)
    {
        Path = path;
        TableOffset = tableOffset;
        Entries = entries;
        Externals = externals;
        Clips = clips;
    }

    public static BaoPackage Read(string path, bool readClips = true)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[IndexStart];
        file.ReadExactly(header);
        if (U32(header, 0) != IndexMagic)
            throw new InvalidDataException(T("ThisFileIsNotInGrp"));

        int indexSize = Number(header, 4);
        int table = Number(header, 8);
        if (indexSize % 8 != 0 || indexSize > 8 * 100_000 ||
            table < IndexStart + indexSize || table > file.Length)
            throw new InvalidDataException(T("BaoPackageDirectoryIsInvalid"));

        var index = new byte[indexSize];
        file.ReadExactly(index);
        var entries = new List<Entry>(indexSize / 8);
        var clips = new List<Clip>();
        Span<byte> objectHeader = stackalloc byte[0xAC];
        int position = IndexStart + indexSize;

        for (int i = 0; i < index.Length; i += 8)
        {
            int size = Number(index, i + 4);
            if (size > table - position)
                throw new InvalidDataException(T("BaoRecordExceedsPackageLimit"));

            var entry = new Entry(U32(index, i), IndexStart + i, position, size);
            entries.Add(entry);
            if (readClips && size >= objectHeader.Length)
            {
                file.Position = position;
                file.ReadExactly(objectHeader);
                if (U32(objectHeader, 0) == ObjectMagic && U32(objectHeader, 0x20) == 0x20000000 &&
                    U32(objectHeader, 0x2C) == 1)
                {
                    int samples = Number(objectHeader, 0x7C);
                    bool looped = (U32(objectHeader, 0x60) & 1) != 0;
                    if (looped)
                        samples = checked(samples + Number(objectHeader, 0x84));
                    clips.Add(new Clip(entry, U32(objectHeader, 0x50), (U32(objectHeader, 0x54) & 1) != 0,
                        Number(objectHeader, 0x8C), Number(objectHeader, 0x6C), Number(objectHeader, 0x74),
                        samples, Number(objectHeader, 0x30), looped, U32(objectHeader, 0x98) != 0));
                }
            }
            position += size;
        }

        var externals = ReadExternals(file, table);
        return new BaoPackage(path, table, entries, externals, clips);
    }

    private static List<External> ReadExternals(Stream file, int table)
    {
        var result = new List<External>();
        if (file.Length - table < 8)
            return result;
        file.Position = table;
        Span<byte> info = stackalloc byte[8];
        file.ReadExactly(info);
        int count = Number(info, 0);
        int namesLength = Number(info, 4);
        if (count == 0)
            return result;
        if (count > 100_000 || namesLength > 4_000_000 || (long)count * 16 + namesLength > file.Length - file.Position)
            throw new InvalidDataException(T("BaoConnectionTableIsInvalid"));
        var names = new byte[namesLength];
        file.ReadExactly(names);
        Span<byte> row = stackalloc byte[16];
        for (int i = 0; i < count; i++)
        {
            int rowOffset = checked((int)file.Position);
            file.ReadExactly(row);
            int start = Number(row, 4);
            if (start >= names.Length)
                throw new InvalidDataException(T("BaoConnectionNameNotFound"));
            int end = Array.IndexOf(names, (byte)0, start);
            if (end < start)
                throw new InvalidDataException(T("BaoConnectionNameIsMissing"));
            result.Add(new External(U32(row, 0), Encoding.ASCII.GetString(names, start, end - start), rowOffset,
                Number(row, 8), Number(row, 12)));
        }
        return result;
    }

    public void WriteWithExternalNames(string destination, Func<External, string> getName)
    {
        var bytes = File.ReadAllBytes(Path);
        if (Externals.Count == 0)
        {
            SafeFile.Write(destination, bytes, overwrite: false);
            return;
        }

        using var names = new MemoryStream();
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new byte[Externals.Count * 16];
        for (int i = 0; i < Externals.Count; i++)
        {
            var external = Externals[i];
            var name = getName(external);
            if (!offsets.TryGetValue(name, out int offset))
            {
                offset = checked((int)names.Length);
                offsets.Add(name, offset);
                names.Write(Encoding.ASCII.GetBytes(name));
                names.WriteByte(0);
            }
            bytes.AsSpan(external.Row, 16).CopyTo(rows.AsSpan(i * 16));
            WriteNumber(rows, i * 16 + 4, offset);
        }

        using var output = new MemoryStream();
        WriteNumber(bytes, TableOffset + 4, checked((int)names.Length));
        output.Write(bytes.AsSpan(0, TableOffset + 8));
        names.WriteTo(output);
        output.Write(rows);
        output.Write(bytes.AsSpan(Externals[^1].Row + 16));
        SafeFile.Write(destination, output.ToArray(), overwrite: false);
    }

    public byte[] ReadAudio(Entry entry)
    {
        using var file = File.OpenRead(Path);
        file.Position = entry.Offset;
        Span<byte> header = stackalloc byte[40];
        if (entry.Size < header.Length)
            throw new InvalidDataException(T("BaoAudioBlockIsMissing"));
        file.ReadExactly(header);
        if (U32(header, 0) != ObjectMagic || U32(header, 4) != 40 ||
            U32(header, 0x20) is not (0x30000000 or 0x50000000))
            throw new InvalidDataException(T("BaoAudioHeaderIsNotSupported"));
        var audio = new byte[entry.Size - header.Length];
        file.ReadExactly(audio);
        return audio;
    }

    public static int Number(ReadOnlySpan<byte> data, int offset) => checked((int)U32(data, offset));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    public static void WriteNumber(Span<byte> data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data[offset..], value);
}
