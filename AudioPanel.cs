using static GrpLocalizationEditor.Ui;
using System.Media;

namespace GrpLocalizationEditor;

internal sealed class AudioPanel : UserControl
{
    private readonly ListBox packages = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = T("AudioFiles") };
    private readonly ListView tracks = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        MultiSelect = false, HideSelection = false, AccessibleName = T("PackageRecords") };
    private readonly TextBox filter = new() { Width = 220, PlaceholderText = T("SearchForPackageOrFilename") };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly ToolTip tips = new();
    private readonly Button play;
    private readonly Button wav;
    private readonly Button ogg;
    private readonly Button allWav;
    private readonly Button allOgg;
    private readonly Button repack;
    private AudioLibrary? library;
    private BaoPackage? bank;
    private string? video;
    private string[] linkedBanks = [];
    private SoundPlayer? player;
    private MemoryStream? playback;
    private readonly string? ffmpeg = AudioTools.FindFfmpeg();
    public bool Busy { get; private set; }

    private sealed record FileItem(string Path, string Name)
    {
        public override string ToString() => Name;
    }

    public AudioPanel()
    {
        var choose = Button(T("OpenAudioFolder"), T("ScanSoundBinPcFolderOr"), OpenFolder);
        var guide = Button(T("PackageGuide"), T("ExplainsFileTypesAndRepackagingSteps"), ShowGuide);
        play = Button(T("Play"), T("DecodesAndPlaysTheSelectedRecording"), async () => await Play());
        wav = Button(T("ExtractWav"), T("WritesTheSelectedRecordingToAn"), async () => await Export(true));
        ogg = Button(T("ExtractOgg"), T("SavesTheOriginalVorbisStreamWithout"), async () => await Export(false));
        allWav = Button(T("ExtractAllAsWav"), T("ExtractsSupportedRecordingsFromTheOpen"), async () => await ExportAll(true));
        allOgg = Button(T("ExtractAllAsOgg"), T("ExtractsVorbisRecordsFromTheOpen"), async () => await ExportAll(false));
        repack = Button(T("ReplaceAudioAndRepack"), T("SelectsAWavOrOggAnd"), async () => await Repack());

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 113));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var toolbar = Flow();
        toolbar.Controls.AddRange([choose, filter, guide]);
        layout.Controls.Add(toolbar, 0, 0);

        var browser = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        browser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        browser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        browser.Controls.Add(packages, 0, 0);
        browser.Controls.Add(tracks, 1, 0);
        tracks.Columns.Add(T("Track"), 90);
        tracks.Columns.Add(T("AudioId"), 105);
        tracks.Columns.Add(T("Format"), 105);
        tracks.Columns.Add(T("ChannelsHz"), 110);
        tracks.Columns.Add(T("Duration"), 83);
        tracks.Columns.Add(T("Location"), 105);
        layout.Controls.Add(browser, 0, 1);
        layout.Controls.Add(details, 0, 2);

        var commands = Flow();
        var stop = Button(T("Stop"), T("StopsTheAudioPreview"), StopPlayback);
        commands.Controls.AddRange([play, stop, wav, ogg, allWav, allOgg, repack]);
        layout.Controls.Add(commands, 0, 3);
        layout.Controls.Add(status, 0, 4);
        Controls.Add(layout);

        filter.TextChanged += (_, _) => FilterFiles();
        packages.SelectedIndexChanged += async (_, _) => await LoadSelection();
        tracks.SelectedIndexChanged += (_, _) => Describe();
        tracks.MouseDoubleClick += async (_, e) =>
        {
            if (tracks.HitTest(e.Location).Item?.Selected == true && play.Enabled)
                await Play();
        };
        Describe();
        Say(ffmpeg is null ? T("FfmpegIsMissingExtractTheComplete") : T("OpenAudioFolder2"));
    }

    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };

    private Button Button(string text, string tip, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(5, 0, 5, 0),
            MinimumSize = new Size(0, 31), AccessibleDescription = tip };
        button.Click += (_, _) => action();
        tips.SetToolTip(button, tip);
        return button;
    }

    private async void OpenFolder()
    {
        if (Busy)
            return;
        using var dialog = new FolderBrowserDialog { Description = T("SelectSoundBinPcOrGrp") };
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Phoenix Network", "Ghost Recon Phantoms");
        if (Directory.Exists(installed))
            dialog.SelectedPath = installed;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        await Work(T("SearchingForVoicePacks"), async () =>
        {
            library = await Task.Run(() => new AudioLibrary(dialog.SelectedPath));
            bank = null;
            video = null;
            tracks.Items.Clear();
            FilterFiles();
            Describe();
            Say(T("Found0N0AudioPackagesAnd", library.Files.Length));
        });
    }

    private void FilterFiles()
    {
        var selected = (packages.SelectedItem as FileItem)?.Path;
        packages.BeginUpdate();
        try
        {
            packages.Items.Clear();
            if (library is null)
                return;
            var query = filter.Text.Trim();
            packages.Items.AddRange(library.Files.Select(path => new FileItem(path, library.Relative(path)))
                .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (selected is not null)
            {
                for (int i = 0; i < packages.Items.Count; i++)
                    if (((FileItem)packages.Items[i]!).Path == selected)
                    {
                        packages.SelectedIndex = i;
                        break;
                    }
            }
        }
        finally { packages.EndUpdate(); }
    }

    private async Task LoadSelection()
    {
        if (Busy)
            return;
        StopPlayback();
        bank = null;
        video = null;
        linkedBanks = [];
        tracks.Items.Clear();
        Describe();
        if (packages.SelectedItem is not FileItem file)
            return;
        await Work(T("ReadingPackageIndex"), async () =>
        {
            if (Path.GetExtension(file.Path).Equals(".bik", StringComparison.OrdinalIgnoreCase))
            {
                int count = await Task.Run(() => AudioTools.BinkTracks(file.Path));
                video = file.Path;
                for (int i = 0; i < count; i++)
                    tracks.Items.Add(new ListViewItem([T("Track0", i + 1), "—", T("BinkAudio"), "—", "—", T("Video")]) { Tag = i });
            }
            else
            {
                bank = await Task.Run(() => BaoPackage.Read(file.Path));
                if (Path.GetExtension(file.Path).Equals(".spk", StringComparison.OrdinalIgnoreCase))
                    linkedBanks = await Task.Run(() => library!.LinkedBanks(file.Path));
                tracks.Items.AddRange(bank.Clips.Select(clip => new ListViewItem([
                    $"{clip.Header.Id:X8}", $"{clip.AudioId:X8}", clip.Format, $"{clip.Channels} / {clip.Rate}",
                    T("0F2S", clip.Seconds), clip.Streamed ? T("ExternalPackage") : T("InPackage")
                ]) { Tag = clip }).ToArray());
            }
            if (tracks.Items.Count > 0)
                tracks.Items[0].Selected = true;
            Describe();
            Say(T("01N0AudioTracks", file.Name, tracks.Items.Count));
        });
    }

    private object? Selection => tracks.SelectedItems.Count == 0 ? null : tracks.SelectedItems[0].Tag;

    private void Describe()
    {
        var selection = Selection;
        var clip = selection as BaoPackage.Clip;
        bool supported = clip is { Codec: 4, Prefetched: false };
        bool canWav = ffmpeg is not null && (supported || selection is int);
        play.Enabled = wav.Enabled = !Busy && canWav;
        ogg.Enabled = !Busy && supported;
        bool hasVorbis = bank?.Clips.Any(clip => CanExport(clip, asWav: false)) == true;
        allOgg.Enabled = !Busy && hasVorbis;
        allWav.Enabled = !Busy && ffmpeg is not null && (hasVorbis || (video is not null && tracks.Items.Count > 0));
        repack.Enabled = !Busy && supported && clip is { Looped: false };

        if (clip is not null && bank is not null)
        {
            string location = clip.Streamed
                ? bank.Externals.FirstOrDefault(x => x.Id == clip.AudioId)?.Name ?? T("ExternalLinkIsMissing")
                : Path.GetFileName(bank.Path);
            details.Text = T("0Recording1X8Audio2", DescribeFile(bank.Path), clip.Header.Id, clip.AudioId, location) +
                T("01Ch2N0Hz", clip.Format, clip.Channels, clip.Rate, clip.Seconds) +
                (clip.Looped ? T("LoopedAudio") : "") +
                (supported ? (clip.Looped ? T("Exportable") : T("CanBeExportedAndRepackaged"))
                           : T("ThereIsNoExtractionSupportFor"));
        }
        else if (video is not null)
        {
            details.Text = T("BinkVideoBikEachAudioTrack");
            if (Path.GetFileName(video).Equals("Tutorial.bik", StringComparison.OrdinalIgnoreCase) && selection is int track && track == 1)
                details.Text += T("Channel2EnglishEducationNarrationMovement");
        }
        else if (bank is not null)
            details.Text = T("0ThereIsABaoObject", DescribeFile(bank.Path), bank.Entries.Count, bank.Externals.Count) +
                T("ThisFileHasNoIndividualAudio")
                + (linkedBanks.Length == 0 ? "" :
                    T("AffiliatedBanks0N01", linkedBanks.Length, string.Join(", ", linkedBanks.Take(8))) +
                    (linkedBanks.Length > 8 ? "…" : "") + T("OpenOneOfTheseBanksTo"));
        else
            details.Text = T("OpenAnAudioFolderAndSelect");
    }

    private static string DescribeFile(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var category = name.ToUpperInvariant() switch
        {
            "ABI_ENGLISH_SADIA" => T("ContainsSadiaSEnglishInGame"),
            var n when n.StartsWith("ABI_") => T("TheNameRefersToTheCharacter"),
            var n when n.StartsWith("FST_") => T("VoiceBankSeparatedByClassVerify"),
            var n when n.StartsWith("UI_") => T("TheNameRefersToInterfaceSounds"),
            var n when n.StartsWith("SIN_WEA_") || n.StartsWith("WEA_") => T("TheNameRefersToGunAnd"),
            var n when n.StartsWith("EXP_") => T("TheNameAlludesToExplosionAnd"),
            var n when n.StartsWith("MUS_") => T("TheNameRefersToTheMusical"),
            var n when n.Contains("_AMB") || n.Contains("WIND") || n.Contains("REVERB") => T("TheNameRefersToTheAmbient"),
            _ => T("TheFileNameAloneDoesNot")
        };
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".spk" => T("ExternalAudioPackageSpkSoundBanks"),
            ".lpk" or ".lhpk" => T("LanguageDependentBaoPackage0May", Path.GetExtension(path), category),
            _ => T("BaoVoiceBank01", Path.GetExtension(path), category)
        };
    }

    private async Task Export(bool asWav)
    {
        if (Busy || Selection is not { } selection)
            return;
        string stem = bank is not null ? Path.GetFileNameWithoutExtension(bank.Path) : Path.GetFileNameWithoutExtension(video)!;
        string id = selection is BaoPackage.Clip clip ? $"{clip.Header.Id:X8}" : $"track{(int)selection + 1}";
        string ext = asWav ? "wav" : "ogg";
        using var dialog = new SaveFileDialog { Filter = T("AudioFile01", ext, ext), DefaultExt = ext,
            FileName = $"{stem}_{id}.{ext}", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (library is not null && IsInside(dialog.FileName, library.Root))
        {
            Say(T("SaveOutputToALocationOutside"));
            return;
        }
        await Work(T("ExtractingAudio"), async () =>
        {
            await Task.Run(() => SafeFile.Write(dialog.FileName, ReadAudio(selection, asWav), overwrite: true));
            Say(T("AudioSaved0", dialog.FileName));
        });
    }

    private static bool CanExport(object selection, bool asWav) =>
        selection is BaoPackage.Clip { Codec: 4, Prefetched: false } || (asWav && selection is int);

    private async Task ExportAll(bool asWav)
    {
        if (Busy || library is null || (bank is null && video is null))
            return;
        var selections = tracks.Items.Cast<ListViewItem>().Select(item => item.Tag).OfType<object>().ToArray();
        if (selections.Length == 0)
            return;

        using var dialog = new FolderBrowserDialog { Description = T("SelectParentFolderForBatchAudio") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (IsInside(dialog.SelectedPath, library.Root))
        {
            Say(T("ChooseALocationOutsideTheSource"));
            return;
        }

        string stem = Path.GetFileNameWithoutExtension(bank?.Path ?? video)!;
        string format = asWav ? "wav" : "ogg";
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string destination = Path.Combine(dialog.SelectedPath, $"{stem}_{format}_{suffix}");
        var progress = new Progress<string>(Say);
        await Work(T("ExtractingTracks"), async () =>
        {
            var (written, skipped, failures) = await Task.Run(() => WriteBatch(selections, asWav, destination, progress));
            string result = T("0N0Exported1N0Unsupported", written, skipped, failures);
            Say(result);
            MessageBox.Show(this, $"{result}\r\n\r\n{destination}" +
                (skipped + failures > 0 ? T("DetailsCikarmaRaporuTxt") : ""), T("BatchAudioExport"));
        });
    }

    private (int Written, int Skipped, int Failures) WriteBatch(object[] selections, bool asWav,
        string destination, IProgress<string> progress)
    {
        Directory.CreateDirectory(destination);
        int written = 0, skipped = 0, failures = 0;
        var report = new List<string>();
        string ext = asWav ? "wav" : "ogg";

        for (int i = 0; i < selections.Length; i++)
        {
            var selection = selections[i];
            string id = selection is BaoPackage.Clip clip ? $"{clip.Header.Id:X8}" : $"track{(int)selection + 1}";
            string name = $"{i + 1:D4}_{id}.{ext}";
            progress.Report(T("ProducingSound0N01N0", i + 1, selections.Length));
            if (!CanExport(selection, asWav))
            {
                skipped++;
                report.Add(T("0ThisFormatIsNotSupported", name));
                continue;
            }

            try
            {
                SafeFile.Write(Path.Combine(destination, name), ReadAudio(selection, asWav), overwrite: false);
                written++;
            }
            catch (Exception ex) when (IsAudioError(ex))
            {
                failures++;
                report.Add($"{name} · {ex.Message}");
            }
        }

        if (report.Count > 0)
            File.WriteAllLines(Path.Combine(destination, "cikarma-raporu.txt"), report);
        return (written, skipped, failures);
    }

    private byte[] ReadAudio(object selection, bool asWav)
    {
        if (selection is int index && video is not null)
            return AudioTools.ExtractVideoTrack(RequireFfmpeg(), video, index);
        var bytes = library!.Extract(bank!, (BaoPackage.Clip)selection);
        return asWav ? AudioTools.DecodeWav(RequireFfmpeg(), bytes) : bytes;
    }

    private async Task Play()
    {
        if (Busy || Selection is not { } selection)
            return;
        StopPlayback();
        await Work(T("PreparingAudioPreview"), async () =>
        {
            var bytes = await Task.Run(() => ReadAudio(selection, asWav: true));
            playback = new MemoryStream(bytes);
            player = new SoundPlayer(playback);
            player.Play();
            Say(T("AudioIsPlaying"));
        });
    }

    private async Task Repack()
    {
        if (Busy || Selection is not BaoPackage.Clip clip || library is null || bank is null)
            return;
        using var input = new OpenFileDialog { Filter = T("NewRecordWavOggWavOgg"), CheckFileExists = true };
        if (input.ShowDialog(this) != DialogResult.OK)
            return;
        using var folder = new FolderBrowserDialog { Description = T("SelectTheParentFolderWherePackage") };
        if (folder.ShowDialog(this) != DialogResult.OK)
            return;
        if (IsInside(folder.SelectedPath, library.Root))
        {
            Say(T("ChooseALocationOutsideTheSource2"));
            return;
        }

        await Work(T("CreatingPackageCopies"), async () =>
        {
            string directory = await Task.Run(() =>
            {
                var bytes = Path.GetExtension(input.FileName).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                    ? File.ReadAllBytes(input.FileName)
                    : AudioTools.EncodeVorbis(RequireFfmpeg(), input.FileName, clip.Channels, clip.Rate);
                return library.Repack(bank, clip, bytes, folder.SelectedPath);
            });
            Say(T("PackagesSaved0", directory));
            MessageBox.Show(this, directory, T("PackageCopiesCreated"));
        });
    }

    private static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != "..";
    }

    private string RequireFfmpeg() => ffmpeg ?? throw new IOException(T("FfmpegIsMissingExtractTheComplete"));

    private async Task Work(string message, Func<Task> action)
    {
        Busy = true;
        Enabled = false;
        Say(message);
        try { await action(); }
        catch (Exception ex) when (IsAudioError(ex))
        {
            Say(T("TheAudioOperationCouldNotBe"));
            MessageBox.Show(this, ex.Message, T("AudioPackages"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Busy = false;
            Enabled = true;
            Describe();
        }
    }

    private static bool IsAudioError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidDataException or OverflowException or InvalidOperationException or System.ComponentModel.Win32Exception;

    private void ShowGuide()
    {
        MessageBox.Show(this, T("AudioGuide"), T("PackageGuide"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void StopPlayback()
    {
        player?.Stop();
        player?.Dispose();
        playback?.Dispose();
        player = null;
        playback = null;
    }

    private void Say(string message) => status.Text = message;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPlayback();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
