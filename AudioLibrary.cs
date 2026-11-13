using static GrpLocalizationEditor.Ui;
namespace GrpLocalizationEditor;

internal sealed class AudioLibrary
{
    internal sealed record Source(BaoPackage Package, BaoPackage.Entry Entry);
    public string Root { get; }
    public string[] Files { get; }

    public AudioLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        Files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".pk" or ".lpk" or ".spk" or ".hpk" or ".lhpk" or ".bik")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Relative(string path) => Path.GetRelativePath(Root, path);

    private IEnumerable<string> BankFiles() => Files.Where(path =>
        Path.GetExtension(path).ToLowerInvariant() is ".pk" or ".lpk" or ".hpk" or ".lhpk");

    public string[] LinkedBanks(string streamPath) => BankFiles()
        .Select(path => BaoPackage.Read(path, readClips: false))
        .Where(bank => bank.Externals.Any(resource => SafeFile.SamePath(Resolve(bank, resource), streamPath)))
        .Select(bank => Relative(bank.Path)).ToArray();

    private string Resolve(BaoPackage bank, BaoPackage.External resource)
    {
        var path = Path.GetFullPath(resource.Name, Path.GetDirectoryName(bank.Path)!);
        if (!Relative(path).Split(Path.DirectorySeparatorChar).First().Equals("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(Relative(path)))
            return path;
        throw new InvalidDataException(T("BaoLinkIsOutsideTheSelected"));
    }

    public Source Locate(BaoPackage bank, BaoPackage.Clip clip)
    {
        if (clip.Codec != 4 || clip.Prefetched)
            throw new InvalidDataException(T("ThisProcessSupportsSeamlessOggVorbis"));
        if (!clip.Streamed)
            return new Source(bank, bank.Entries.SingleOrDefault(entry => entry.Id == clip.AudioId)
                ?? throw new InvalidDataException(T("NoInPacketAudioBlockFound")));

        var resource = bank.Externals.SingleOrDefault(item => item.Id == clip.AudioId)
            ?? throw new InvalidDataException(T("LinkedVoicePackageIsNotSpecified"));
        var external = BaoPackage.Read(Resolve(bank, resource), readClips: false);
        var entry = external.Entries.SingleOrDefault(item =>
            item.Id == clip.AudioId && item.Offset == resource.Offset && item.Size == resource.Size)
            ?? throw new InvalidDataException(T("TheIdPositionOrSizeOf"));
        return new Source(external, entry);
    }

    public byte[] Extract(BaoPackage bank, BaoPackage.Clip clip)
    {
        var source = Locate(bank, clip);
        var audio = source.Package.ReadAudio(source.Entry);
        if (clip.Size != audio.Length)
            throw new InvalidDataException(T("TheSizeOfTheAudioBlock"));
        _ = VorbisInfo.Read(audio);
        return audio;
    }

    public string Repack(BaoPackage bank, BaoPackage.Clip clip, byte[] audio, string outputParent)
    {
        var info = VorbisInfo.Read(audio, verifyChecksum: true);
        if (clip.Looped || info.Channels != clip.Channels || info.Rate != clip.Rate)
            throw new InvalidDataException(T("RepackagingRequiresLoopFreeAudioAnd"));

        // Reload indexes so the output is based on a coherent snapshot of the source files.
        bank = BaoPackage.Read(bank.Path);
        if (!bank.Clips.Contains(clip))
            throw new InvalidDataException(T("TheSourceBankHasChangedReopen"));
        var source = Locate(bank, clip);
        var original = source.Package.ReadAudio(source.Entry);
        if (clip.Size != original.Length || VorbisInfo.Read(original).Samples != clip.Samples)
            throw new InvalidDataException(T("OriginalVoiceAndBaoInformationAre"));

        int delta = audio.Length - original.Length;
        var output = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (clip.Streamed)
        {
            foreach (string path in BankFiles())
            {
                var linkedBank = BaoPackage.Read(path, readClips: false);
                var resources = linkedBank.Externals.Where(x => SafeFile.SamePath(Resolve(linkedBank, x), source.Package.Path)).ToArray();
                if (resources.Length == 0)
                    continue;
                var bytes = File.ReadAllBytes(path);
                foreach (var resource in resources)
                {
                    if (resource.Offset > source.Entry.Offset)
                        BaoPackage.WriteNumber(bytes, resource.Row + 8, checked(resource.Offset + delta));
                    else if (resource.Offset == source.Entry.Offset)
                    {
                        if (resource.Id != clip.AudioId || resource.Size != source.Entry.Size)
                            throw new InvalidDataException(T("ThereAreMultipleIncompatibleConnectionsTo"));
                        BaoPackage.WriteNumber(bytes, resource.Row + 12, checked(resource.Size + delta));
                    }
                }

                var ids = resources.Where(x => x.Offset == source.Entry.Offset).Select(x => x.Id).ToHashSet();
                if (ids.Count > 0)
                {
                    linkedBank = BaoPackage.Read(path);
                    foreach (var reference in linkedBank.Clips.Where(x => x.Streamed && ids.Contains(x.AudioId)))
                        UpdateHeader(bytes, reference, original.Length, clip.Samples, audio.Length, info.Samples);
                }
                output.Add(path, bytes);
            }
            if (!output.ContainsKey(bank.Path))
                throw new InvalidDataException(T("TheReferringBankWasNotFound"));
        }

        var payloadPackage = File.ReadAllBytes(source.Package.Path);
        if (!clip.Streamed)
        {
            foreach (var reference in bank.Clips.Where(x => !x.Streamed && x.AudioId == clip.AudioId))
                UpdateHeader(payloadPackage, reference, original.Length, clip.Samples, audio.Length, info.Samples);
        }
        int payloadOffset = source.Entry.Offset + 40;
        BaoPackage.WriteNumber(payloadPackage, source.Entry.Index + 4, checked(source.Entry.Size + delta));
        BaoPackage.WriteNumber(payloadPackage, 8, checked(source.Package.TableOffset + delta));
        var rebuilt = new byte[checked(payloadPackage.Length + delta)];
        payloadPackage.AsSpan(0, payloadOffset).CopyTo(rebuilt);
        audio.CopyTo(rebuilt, payloadOffset);
        payloadPackage.AsSpan(payloadOffset + original.Length).CopyTo(rebuilt.AsSpan(payloadOffset + audio.Length));
        output.Add(source.Package.Path, rebuilt);

        string label = $"GRP_Ses_{clip.Header.Id:X8}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}";
        var destination = Path.Combine(outputParent, label);
        var staging = destination + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (path, bytes) in output)
            {
                var file = Path.Combine(staging, Relative(path));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                SafeFile.Write(file, bytes, overwrite: false);
            }
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static void UpdateHeader(byte[] bytes, BaoPackage.Clip clip, int oldSize, int oldSamples, int newSize, int newSamples)
    {
        if (clip.Codec != 4 || clip.Looped || clip.Prefetched || clip.Size != oldSize || clip.Samples != oldSamples)
            throw new InvalidDataException(T("ADifferentRecordingFormatWasFound"));
        int offset = clip.Header.Offset;
        BaoPackage.WriteNumber(bytes, offset + 0x30, newSize);
        BaoPackage.WriteNumber(bytes, offset + 0x7C, newSamples);
        if (BaoPackage.Number(bytes, offset + 0x80) == oldSize)
            BaoPackage.WriteNumber(bytes, offset + 0x80, newSize);
    }
}
