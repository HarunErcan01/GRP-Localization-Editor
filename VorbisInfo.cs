using static GrpLocalizationEditor.Ui;
using System.Buffers.Binary;

namespace GrpLocalizationEditor;

internal sealed record VorbisInfo(int Channels, int Rate, int Samples)
{
    public static VorbisInfo Read(byte[] data, bool verifyChecksum = false)
    {
        int channels = 0, rate = 0, samples = 0, cursor = 0;
        uint serial = 0;
        bool ended = false;
        while (cursor < data.Length)
        {
            var page = data.AsSpan(cursor);
            if (page.Length < 27 || !page.StartsWith("OggS\0"u8) || ended)
                throw new InvalidDataException(T("ASingleStreamOggVorbisFile"));

            int segments = page[26];
            int headerSize = 27 + segments;
            if (page.Length < headerSize)
                throw new InvalidDataException(T("OggPageTitleIsMissing"));
            int length = headerSize;
            for (int i = 27; i < headerSize; i++)
                length += page[i];
            if (page.Length < length)
                throw new InvalidDataException(T("OggAudioDataIsIncomplete"));

            if (verifyChecksum && Checksum(page[..length]) != BinaryPrimitives.ReadUInt32LittleEndian(page[22..]))
                throw new InvalidDataException(T("TheChecksumOfTheOggAudio"));

            uint pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page[14..]);
            if (cursor == 0)
            {
                var packet = page[headerSize..length];
                if (segments == 0 || page[27] < 30 || !packet.StartsWith("\u0001vorbis"u8) || (page[5] & 2) == 0)
                    throw new InvalidDataException(T("NoVorbisAudioFoundInOgg"));
                serial = pageSerial;
                channels = packet[11];
                rate = BaoPackage.Number(packet, 12);
            }
            if (pageSerial != serial)
                throw new InvalidDataException(T("MultipleOggStreamsAreNotSupported"));

            ended = (page[5] & 4) != 0;
            if (ended)
                samples = checked((int)BinaryPrimitives.ReadInt64LittleEndian(page[6..]));
            cursor += length;
        }

        if (!ended || channels == 0 || rate == 0 || samples <= 0)
            throw new InvalidDataException(T("OggVorbisMetadataIsIncomplete"));
        return new VorbisInfo(channels, rate, samples);
    }

    private static uint Checksum(ReadOnlySpan<byte> page)
    {
        uint value = 0;
        for (int i = 0; i < page.Length; i++)
        {
            if (i is < 22 or > 25)
                value ^= (uint)page[i] << 24;
            for (int bit = 0; bit < 8; bit++)
                value = (value << 1) ^ ((value & 0x80000000) == 0 ? 0u : 0x04C11DB7u);
        }
        return value;
    }

}
