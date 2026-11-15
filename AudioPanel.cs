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
    private readonly Button replaceAudio;
    private readonly Button overwriteBank;
    private readonly Button saveBankAs;
    private readonly Dictionary<string, Dictionary<(bool Streamed, uint Id), AudioLibrary.Replacement>> pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Button importFolder;
    private AudioLibrary? library;
    private BaoPackage? bank;
    private string? video;
    private string[] linkedBanks = [];
    private SoundPlayer? player;
    private MemoryStream? playback;
    private readonly string? ffmpeg = AudioTools.FindFfmpeg();
    private Dictionary<(bool Streamed, uint Id), AudioLibrary.Replacement>? Pending =>
        bank is null ? null : pending.GetValueOrDefault(bank.Path);

    public bool ConfirmDiscard() => pending.Count == 0 || MessageBox.Show(this, T("AudioDiscardPending"),
        T("AudioPackages"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

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
        replaceAudio = Button(T("ReplaceAudio"), T("ReplaceAudioTip"), async () => await ReplaceAudio());
        overwriteBank = Button(T("OverwriteAudioBank"), T("OverwriteAudioBankTip"), async () => await SaveBank(overwrite: true));
        saveBankAs = Button(T("SaveAudioBankAs"), T("SaveAudioBankAsTip"), async () => await SaveBank(overwrite: false));
        importFolder = Button(T("ImportAudioFolder"), T("ImportAudioFolderTip"), async () => await ImportFolder());

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 113));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var toolbar = Flow();
        toolbar.Controls.AddRange([guide, choose]);
        var saveCommands = Flow();
        saveCommands.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        saveCommands.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        saveCommands.WrapContents = false;
        saveCommands.Controls.AddRange([overwriteBank, saveBankAs]);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(toolbar, 0, 0);
        header.Controls.Add(saveCommands, 1, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(filter, 0, 1);

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
        layout.Controls.Add(browser, 0, 2);
        layout.Controls.Add(details, 0, 3);

        var commands = Flow();
        var stop = Button(T("Stop"), T("StopsTheAudioPreview"), StopPlayback);
        commands.Controls.AddRange([play, stop, wav, ogg, allWav, allOgg, replaceAudio, importFolder]);
        layout.Controls.Add(commands, 0, 4);
        layout.Controls.Add(status, 0, 5);
        Controls.Add(layout);

        filter.TextChanged += (_, _) => FilterFiles();
        packages.SelectedIndexChanged += PackageSelectionChanged;
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
            StopPlayback();
            packages.SelectedIndex = -1;
            bank = null;
            video = null;
            linkedBanks = [];
            tracks.Items.Clear();
            FilterFiles();
            Describe();
            Say(T("Found0N0AudioPackagesAnd", library.Files.Length));
        });
    }

    private void FilterFiles()
    {
        var selected = (packages.SelectedItem as FileItem)?.Path;
        packages.SelectedIndexChanged -= PackageSelectionChanged;
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
        finally
        {
            packages.EndUpdate();
            packages.SelectedIndexChanged += PackageSelectionChanged;
        }
        if (selected is not null && packages.SelectedIndex < 0)
            _ = LoadSelection();
    }

    private async void PackageSelectionChanged(object? sender, EventArgs e) => await LoadSelection();

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
            Say(Pending is { } edits ? T("AudioPendingCount", edits.Count) : T("01N0AudioTracks", file.Name, tracks.Items.Count));
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
        importFolder.Enabled = !Busy && bank?.Clips.Any(c => c is { Codec: 4, Looped: false, Prefetched: false }) == true;
        replaceAudio.Enabled = !Busy && supported && clip is { Looped: false };

        overwriteBank.Enabled = !Busy && Pending is { Count: > 0 };
        saveBankAs.Enabled = !Busy && bank is not null && (bank.Clips.Count > 0 || bank.Externals.Count > 0);

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
            if (Pending?.ContainsKey((clip.Streamed, clip.AudioId)) == true)
                details.AppendText("\r\n" + T("AudioPendingTrack"));
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
            FileName = $"{stem}_{id}.{ext}", OverwritePrompt = true, AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        if (!Path.GetExtension(dialog.FileName).Equals("." + ext, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, T("AudioExportExtension", ext), T("AudioPackages"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        var clip = (BaoPackage.Clip)selection;
        var bytes = Pending?.GetValueOrDefault((clip.Streamed, clip.AudioId))?.Audio ?? library!.Extract(bank!, clip);
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

    private async Task ReplaceAudio()
    {
        if (Busy || Selection is not BaoPackage.Clip clip || library is null || bank is null)
            return;
        using var input = new OpenFileDialog { Filter = T("NewRecordWavOggWavOgg"), CheckFileExists = true };
        if (input.ShowDialog(this) != DialogResult.OK)
            return;
        await ApplyImports([new AudioLibrary.Import(clip.Header.Id, input.FileName)]);
    }

    private async Task ImportFolder()
    {
        if (Busy || bank is null)
            return;
        using var dialog = new FolderBrowserDialog { Description = T("ImportAudioFolderTip") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        AudioLibrary.Import[] imports;
        try
        {
            imports = AudioLibrary.MatchFolder(bank, dialog.SelectedPath);
            if (imports.Length == 0)
                throw new InvalidDataException(T("NoMatchingAudioImports"));
        }
        catch (Exception ex) when (IsAudioError(ex))
        {
            MessageBox.Show(this, ex.Message, T("AudioPackages"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ApplyImports(imports);
    }

    private async Task ApplyImports(AudioLibrary.Import[] imports)
    {
        if (library is null || bank is not { } selectedBank)
            return;

        StopPlayback();
        await Work(T("AudioPreparingChanges"), async () =>
        {
            var edits = await Task.Run(() => imports.Select(import =>
            {
                var clip = selectedBank.Clips.Single(item => item.Header.Id == import.HeaderId);
                if (clip is not { Codec: 4, Looped: false, Prefetched: false })
                    throw new InvalidDataException(T("ThisProcessSupportsSeamlessOggVorbis"));
                var audio = Path.GetExtension(import.Path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                    ? File.ReadAllBytes(import.Path)
                    : AudioTools.EncodeVorbis(RequireFfmpeg(), import.Path, clip.Channels, clip.Rate);
                var info = VorbisInfo.Read(audio, verifyChecksum: true);
                if (info.Channels != clip.Channels || info.Rate != clip.Rate)
                    throw new InvalidDataException(T("RepackagingRequiresLoopFreeAudioAnd"));
                return new AudioLibrary.Replacement(clip, audio);
            }).ToArray());

            if (edits.Select(item => (item.Clip.Streamed, item.Clip.AudioId)).Distinct().Count() != edits.Length)
                throw new InvalidDataException(T("SharedAudioImports"));
            if (!pending.TryGetValue(selectedBank.Path, out var queued))
                pending.Add(selectedBank.Path, queued = new());
            foreach (var edit in edits)
                queued[(edit.Clip.Streamed, edit.Clip.AudioId)] = edit;
            Say(T("AudioPendingCount", queued.Count));
        });
    }

    private async Task SaveBank(bool overwrite)
    {
        if (Busy || library is not { } source || bank is not { } selectedBank)
            return;
        var edits = Pending?.Values.ToArray() ?? [];
        string parent = Path.GetDirectoryName(selectedBank.Path)!;
        AudioLibrary.CopyNames? names = null;
        StopPlayback();

        if (overwrite)
        {
            if (edits.Length == 0 || MessageBox.Show(this, T("AudioConfirmOverwrite", edits.Length),
                T("OverwriteAudioBank"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
        }
        else
        {
            try
            {
                var streams = source.Streams(selectedBank);
                names = ChooseCopyNames(selectedBank, streams);
            }
            catch (Exception ex) when (IsAudioError(ex))
            {
                MessageBox.Show(this, ex.Message, T("AudioPackages"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (names is null)
                return;
            using var folder = new FolderBrowserDialog { Description = T("SelectTheParentFolderWherePackage"), SelectedPath = parent };
            if (folder.ShowDialog(this) != DialogResult.OK)
                return;
            parent = folder.SelectedPath;
        }

        bool saved = false;
        string? result = null;
        await Work(T("CreatingPackageCopies"), async () =>
        {
            string savedLocation = await Task.Run(() => source.Repack(selectedBank, edits, parent, names));
            pending.Remove(selectedBank.Path);
            saved = true;
            result = overwrite ? T("AudioAppliedBackup", savedLocation) : T("PackagesSaved0", savedLocation);
            MessageBox.Show(this, result, T("AudioPackages"));
        });
        if (saved && overwrite)
            await LoadSelection();
        if (result is not null)
            Say(result);
    }

    private AudioLibrary.CopyNames? ChooseCopyNames(BaoPackage selectedBank, string[] streams)
    {
        string extension = Path.GetExtension(selectedBank.Path);
        using var dialog = new Form
        {
            Text = T("SaveAudioBankAs"), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = Font
        };
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        var bankName = new TextBox { Width = 365, Text = Path.GetFileName(selectedBank.Path) };
        var streamName = new TextBox
        {
            Width = 365, Enabled = streams.Length > 0, Text = streams.Length > 0 ? Path.GetFileNameWithoutExtension(streams[0]) + ".spk" : ""
        };
        var bankLabel = new Label { Text = T("AudioBankFilename", extension), AutoSize = true, Anchor = AnchorStyles.Left };
        var streamLabel = new Label { Text = T("AudioStreamFilename"), AutoSize = true, Anchor = AnchorStyles.Left };
        bankName.AccessibleName = bankLabel.Text;
        streamName.AccessibleName = streamLabel.Text;
        layout.Controls.Add(bankLabel, 0, 0);
        layout.Controls.Add(bankName, 1, 0);
        layout.Controls.Add(streamLabel, 0, 1);
        layout.Controls.Add(streamName, 1, 1);
        if (streams.Length > 1)
        {
            var note = new Label { Text = T("AudioMultipleStreams"), AutoSize = true };
            layout.Controls.Add(note, 0, 2);
            layout.SetColumnSpan(note, 2);
        }

        var ok = new Button { Text = T("Save"), AutoSize = true };
        var cancel = new Button { Text = T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = Flow();
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.Add(buttons, 1, 3);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        AudioLibrary.CopyNames? result = null;
        ok.Click += (_, _) =>
        {
            string? newBank = PackageName(bankName.Text, extension);
            string? newStream = streams.Length == 0 ? "" : PackageName(streamName.Text, ".spk");
            if (newBank is null || newStream is null || string.Equals(newBank, newStream, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(dialog, T("AudioInvalidPackageNames"), T("SaveAudioBankAs"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result = new(newBank, newStream);
            dialog.DialogResult = DialogResult.OK;
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? result : null;
    }

    private static string? PackageName(string input, string extension)
    {
        string name = input.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Any(c => c < ' ' || c > '~') || name.EndsWith('.') ||
            System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0].TrimEnd(),
                @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return null;
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name += extension;
        return name.Length <= 200 ? name : null;
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
