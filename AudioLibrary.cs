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
        Files = EnumerateSourceFiles(Root)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".pk" or ".lpk" or ".spk" or ".hpk" or ".lhpk" or ".bik")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child).StartsWith("GRP_Audio_", StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                continue;
            foreach (var file in EnumerateSourceFiles(child))
                yield return file;
        }
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

    private Dictionary<string, byte[]> BuildReplacement(BaoPackage bank, BaoPackage.Clip clip, byte[] audio)
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
            foreach (string path in BankFiles().Append(bank.Path).Distinct(StringComparer.OrdinalIgnoreCase))
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

        return output;
    }

    internal sealed record Import(uint HeaderId, string Path);

    public static Import[] MatchFolder(BaoPackage bank, string folder)
    {
        var matches = new Dictionary<uint, string>();
        var ids = bank.Clips.Select(clip => clip.Header.Id).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".wav" or ".ogg"))
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var suffix = name[(name.LastIndexOf('_') + 1)..];
            if (suffix.Length != 8 || !uint.TryParse(suffix, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint id) || !ids.Contains(id))
                continue;
            if (!matches.TryAdd(id, path))
                throw new InvalidDataException(T("DuplicateAudioImport", suffix));
        }

        return matches.Select(match => new Import(match.Key, match.Value)).ToArray();
    }

    internal sealed record Replacement(BaoPackage.Clip Clip, byte[] Audio);
    internal sealed record CopyNames(string Bank, string Stream);

    public string[] Streams(BaoPackage bank) => bank.Externals
        .Select(resource => Resolve(bank, resource)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public string Repack(BaoPackage bank, IReadOnlyCollection<Replacement> replacements,
        string outputParent, CopyNames? names)
    {
        var currentBank = BaoPackage.Read(bank.Path);
        if (!currentBank.Entries.SequenceEqual(bank.Entries) || !currentBank.Clips.SequenceEqual(bank.Clips) ||
            !currentBank.Externals.SequenceEqual(bank.Externals))
            throw new InvalidDataException(T("TheSourceBankHasChangedReopen"));

        bool overwrite = names is null;
        var streams = overwrite
            ? replacements.Where(item => item.Clip.Streamed)
                .Select(item => Locate(bank, item.Clip).Package.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Streams(bank);
        var destination = Path.Combine(outputParent, $"GRP_Audio_{Guid.NewGuid():N}");
        var workspace = destination + ".tmp";
        var staging = Path.Combine(workspace, "work");
        Directory.CreateDirectory(staging);
        try
        {
            var bankSources = overwrite && streams.Length > 0 ? BankFiles().Append(bank.Path) : [bank.Path];
            var originals = Path.Combine(workspace, "originals");
            foreach (var path in bankSources.Concat(streams).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var copy = Path.Combine(staging, Relative(path));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(path, copy);
                if (overwrite)
                {
                    var original = Path.Combine(originals, Relative(path));
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Copy(copy, original);
                }
            }

            var working = new AudioLibrary(staging);
            var workingBankPath = Path.Combine(staging, Relative(bank.Path));
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var replacement in replacements)
            {
                var current = BaoPackage.Read(workingBankPath);
                var clip = current.Clips.Single(candidate => candidate.Header.Id == replacement.Clip.Header.Id);
                foreach (var (path, bytes) in working.BuildReplacement(current, clip, replacement.Audio))
                {
                    SafeFile.Write(path, bytes, overwrite: true);
                    changed.Add(working.Relative(path));
                }
            }

            if (names is not null)
            {
                var result = Path.Combine(workspace, "result");
                Directory.CreateDirectory(result);
                var streamNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < streams.Length; i++)
                {
                    string name = i == 0 ? names.Stream :
                        $"{Path.GetFileNameWithoutExtension(names.Stream)}_{i + 1}{Path.GetExtension(names.Stream)}";
                    streamNames.Add(Path.Combine(staging, Relative(streams[i])), name);
                    File.Copy(Path.Combine(staging, Relative(streams[i])), Path.Combine(result, name));
                }

                var copy = BaoPackage.Read(workingBankPath);
                copy.WriteWithExternalNames(Path.Combine(result, names.Bank),
                    resource => streamNames[working.Resolve(copy, resource)]);
                Directory.Move(result, destination);
                return destination;
            }

            return OverwritePackages(changed, originals, staging);
        }
        finally
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }

    private string OverwritePackages(HashSet<string> changed, string originals, string staging)
    {
        int version = 0;
        foreach (var relative in changed)
        {
            string path = Path.Combine(Root, relative);
            string prefix = Path.GetFileName(path) + ".bak";
            foreach (var file in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, prefix + "*"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int existing))
                    version = Math.Max(version, existing);
            }
        }

        string suffix = ".bak" + checked(version + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var backups = new List<string>();
        try
        {
            foreach (var relative in changed)
            {
                string path = Path.Combine(Root, relative);
                var original = File.ReadAllBytes(Path.Combine(originals, relative));
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                    throw new IOException(T("TheSourceBankHasChangedReopen"));
                SafeFile.Write(path + suffix, original, overwrite: false);
                backups.Add(path + suffix);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var backup in backups)
            {
                try { File.Delete(backup); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }

        var applied = new List<string>();
        try
        {
            foreach (var relative in changed)
            {
                string path = Path.Combine(Root, relative);
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(path + suffix)))
                    throw new IOException(T("TheSourceBankHasChangedReopen"));
                SafeFile.Write(path, File.ReadAllBytes(Path.Combine(staging, relative)), overwrite: true);
                applied.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var path in applied)
            {
                try { SafeFile.Write(path, File.ReadAllBytes(path + suffix), overwrite: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            throw new IOException($"{ex.Message}\n\n{T("AudioRestoreBackup", suffix)}", ex);
        }
        return suffix;
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
