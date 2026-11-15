using static GrpLocalizationEditor.Ui;
using System.Drawing;

namespace GrpLocalizationEditor;

internal sealed class MainForm : Form
{
    private const string AppName = "GRP Localization Editor";
    private static readonly string InfFilter = T("InfDialogFilter");
    private static readonly string EncFilter = T("EncryptedDialogFilter");
    private static readonly Color Ink = Color.FromArgb(19, 38, 56);
    private static readonly Color Muted = Color.FromArgb(75, 95, 113);
    private static readonly Color Canvas = Color.FromArgb(240, 245, 249);
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage editorTab = new(T("TranslationEditor")) { BackColor = Canvas };
    private readonly TabPage audioTab = new(T("AudioPackages")) { BackColor = Canvas };
    private readonly TabPage converterTab = new(T("FileConversion")) { BackColor = Canvas };
    private readonly TranslationListView grid = new();
    private readonly VScrollBar translationScroll = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TextBox englishText = EditorBox(readOnly: true);
    private readonly TextBox inlineText = new()
    {
        Visible = false, AutoSize = false, MaxLength = 0,
        AccessibleName = T("InlineTargetText")
    };
    private readonly TextBox targetText = EditorBox(readOnly: false);
    private readonly TextBox search = new() { Width = 190, PlaceholderText = T("SearchIdOrText") };
    private readonly Label referenceLabel = new() { AutoSize = true };
    private readonly Label targetLabel = new() { AutoSize = true };
    private readonly Label rowLabel = new() { AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolTip tips = new();
    private readonly Button saveButton;
    private readonly Button saveAsButton;
    private readonly Button openEnglishButton;
    private readonly Button openReferenceButton;
    private readonly Button previousButton;
    private readonly Button nextButton;
    private readonly Button findButton;

    private OasisDocument? reference;
    private string? referencePath;
    private OasisDocument? target;
    private string? targetPath;
    private string[] rowCodes = [];
    private int viewportStart;
    private int viewportRows = 16;
    private string? selectedCode;
    private bool dirty;
    private bool syncing;
    private readonly AudioPanel audio;
    private bool converting;
    private bool restarting;
    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    public MainForm()
    {
        Text = AppName;
        Font = new Font("Segoe UI", 10);
        ForeColor = Ink;
        BackColor = Canvas;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(890, 650);
        ClientSize = new Size(1170, 770);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = Table(1, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var header = Table(4, 1);
        header.BackColor = Ink;
        header.Padding = new Padding(20, 0, 20, 0);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int column = 1; column < header.ColumnCount; column++)
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var name = new Label
        {
            Text = AppName, AutoSize = true, ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 20), Anchor = AnchorStyles.Left
        };
        var help = Button(T("ShortcutsAndHelp"), T("ExplainsKeyboardShortcutsAndFileFormats"), ShowHelp);
        help.Anchor = AnchorStyles.None;
        header.Controls.Add(name, 0, 0);
        var language = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, DropDownWidth = 185,
            AccessibleName = T("InterfaceLanguage"), Anchor = AnchorStyles.None, Margin = new Padding(6, 0, 8, 0)
        };
        language.Items.Add(new LanguageChoice("", T("SystemLanguage")));
        foreach (var item in Ui.Languages)
            language.Items.Add(new LanguageChoice(item.Code, item.Name));
        language.SelectedIndex = Enumerable.Range(0, language.Items.Count)
            .First(index => language.Items[index] is LanguageChoice item && item.Code == Ui.Preference);
        language.SelectionChangeCommitted += (_, _) => ChangeLanguage(language);
        tips.SetToolTip(language, T("InterfaceLanguage"));
        var about = Button(T("About"), T("OpenGithubProfile"), ShowAbout);
        about.Anchor = AnchorStyles.None;
        header.Controls.Add(language, 1, 0);
        header.Controls.Add(help, 2, 0);
        header.Controls.Add(about, 3, 0);

        tabs.TabPages.AddRange([editorTab, converterTab, audioTab]);
        tabs.Margin = new Padding(14, 12, 14, 0);
        status.Padding = new Padding(18, 0, 18, 0);
        status.ForeColor = Muted;
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(status, 0, 2);
        Controls.Add(layout);

        openEnglishButton = Button(T("LoadOriginalEnglish"), T("LoadsTheBuiltInEnglishGrp"), OpenEnglish);
        openReferenceButton = Button(T("OpenReferenceInf"), T("LoadsAnotherLanguageSInfFile"), OpenReference);
        saveButton = Button(T("Save"), T("SavesTheTargetFileCtrlS"), () => Save(false), primary: true);
        saveAsButton = Button(T("SaveAs"), T("WritesTheTargetToTheLocation"), () => Save(true));
        previousButton = Button(T("Previous"), T("MovesToThePreviousEditableLine"), () => MoveRow(-1));
        nextButton = Button(T("Next"), T("MovesToTheNextEditableLine"), () => MoveRow(1));
        findButton = Button(T("Find"), T("SearchesForCodeOrTextAfter"), FindNext);

        BuildEditor();
        BuildConverter();
        audio = new AudioPanel { Dock = DockStyle.Fill };
        audioTab.Controls.Add(audio);
        FormClosing += (_, e) => e.Cancel = !restarting && (converting || audio.Busy || !ConfirmDiscard() || !audio.ConfirmDiscard());
        PopulateRows();
        Shown += (_, _) => BeginInvoke(() =>
        {
            ResizeViewport();
            if (rowCodes.Length > 0 && SelectedRowIndex < 0)
                SelectRow(0);
        });
    }

    private void ChangeLanguage(ComboBox picker)
    {
        if (picker.SelectedItem is not LanguageChoice choice || choice.Code == Ui.Preference)
            return;
        int previous = Enumerable.Range(0, picker.Items.Count)
            .First(index => picker.Items[index] is LanguageChoice item && item.Code == Ui.Preference);
        if (converting || audio.Busy)
        {
            picker.SelectedIndex = previous;
            return;
        }

        bool restart = Ui.Resolve(System.Globalization.CultureInfo.CurrentUICulture.Name, choice.Code).Code != Ui.ActiveCode;
        if (restart && (MessageBox.Show(this, T("TheAppWillRestartToChange"),
            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes || !ConfirmDiscard() || !audio.ConfirmDiscard()))
        {
            picker.SelectedIndex = previous;
            return;
        }
        try { Ui.SavePreference(choice.Code); }
        catch (Exception ex) when (IsFileError(ex))
        {
            picker.SelectedIndex = previous;
            Say(T("CouldNotSaveTheLanguageSetting"));
            ShowError(ex);
            return;
        }

        if (restart)
        {
            restarting = true;
            Application.Restart();
        }
    }

    private void ShowAbout()
    {
        const string profile = "https://github.com/HarunErcan01";
        using var dialog = new Form
        {
            Text = T("About"), StartPosition = FormStartPosition.CenterParent, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false
        };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18)
        };
        content.Controls.Add(new Label { Text = AppName, AutoSize = true });
        content.Controls.Add(new Label { Text = T("CreatedBy") + " · Harun Ercan", AutoSize = true });
        var link = new LinkLabel { Text = profile, AutoSize = true, AccessibleName = T("OpenGithubProfile") };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(profile) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ShowError(ex); }
        };
        content.Controls.Add(link);
        dialog.Controls.Add(content);
        dialog.ShowDialog(this);
    }

    private void BuildEditor()
    {
        var layout = Table(1, 6);
        layout.Padding = new Padding(14, 10, 14, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorTab.Controls.Add(layout);

        var tools = Flow();
        tools.Controls.AddRange([openEnglishButton, openReferenceButton, saveButton, saveAsButton, search, findButton]);
        search.Margin = new Padding(14, 5, 4, 4);
        layout.Controls.Add(tools, 0, 0);

        var files = Flow();
        files.Controls.AddRange([referenceLabel, targetLabel]);
        referenceLabel.Margin = new Padding(3, 7, 18, 8);
        targetLabel.Margin = new Padding(3, 7, 3, 8);
        layout.Controls.Add(files, 0, 1);

        grid.Dock = DockStyle.Fill;
        grid.BackColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.View = View.Details;
        grid.VirtualMode = true;
        grid.Scrollable = false;
        grid.FullRowSelect = true;
        grid.HideSelection = false;
        grid.MultiSelect = false;
        grid.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        grid.Columns.Add(T("No"), 65);
        grid.Columns.Add(T("Id"), 88);
        grid.Columns.Add(T("ReferenceReadOnly"), 370);
        grid.Columns.Add(T("TargetLanguage"), 370);
        grid.Resize += (_, _) =>
        {
            int available = Math.Max(300, grid.ClientSize.Width - grid.Columns[0].Width - grid.Columns[1].Width - 2);
            grid.Columns[2].Width = available / 2;
            grid.Columns[3].Width = available - grid.Columns[2].Width;
            ResizeViewport();
        };
        grid.RetrieveVirtualItem += (_, e) =>
        {
            int index = viewportStart + e.ItemIndex;
            if ((uint)index >= (uint)rowCodes.Length)
            {
                e.Item = new ListViewItem(["", "", "", ""]);
                return;
            }
            var code = rowCodes[index];
            var item = new ListViewItem([(index + 1).ToString("N0"), code,
                reference?.GetText(code) ?? "", target?.GetText(code) ?? ""])
            {
                UseItemStyleForSubItems = false,
                BackColor = Color.FromArgb(227, 235, 242),
                ForeColor = Muted
            };
            item.SubItems[1].BackColor = item.SubItems[2].BackColor = Color.FromArgb(203, 216, 229);
            item.SubItems[1].ForeColor = item.SubItems[2].ForeColor = Ink;
            item.SubItems[3].BackColor = index % 2 == 0 ? Color.White : Color.FromArgb(246, 249, 252);
            e.Item = item;
        };
        grid.SelectedIndexChanged += (_, _) =>
        {
            inlineText.Hide();
            LoadSelectedRow();
        };
        grid.MouseDoubleClick += (_, e) =>
        {
            var hit = grid.HitTest(e.Location);
            if (hit.Item is not null && hit.SubItem is not null && hit.Item.SubItems.IndexOf(hit.SubItem) == 3)
                StartInlineEdit();
        };
        grid.Controls.Add(inlineText);
        grid.ViewportChanged += (_, _) => RepositionInlineEditor();
        grid.ColumnWidthChanged += (_, _) => RepositionInlineEditor();
        grid.Resize += (_, _) => RepositionInlineEditor();
        inlineText.TextChanged += (_, _) => ApplyTargetText(inlineText);
        inlineText.KeyDown += TargetKeyDown;
        inlineText.LostFocus += (_, _) => inlineText.Hide();
        var listArea = Table(2, 1);
        listArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SystemInformation.VerticalScrollBarWidth));
        listArea.Controls.Add(grid, 0, 0);
        listArea.Controls.Add(translationScroll, 1, 0);
        translationScroll.ValueChanged += (_, _) =>
        {
            if (!syncing)
                ScrollTo(translationScroll.Value);
        };
        grid.WheelScrolled += (_, delta) =>
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines != 0)
                ScrollTo(viewportStart - delta * (lines < 0 ? viewportRows : lines) / 120);
        };
        layout.Controls.Add(listArea, 0, 2);

        rowLabel.Margin = new Padding(3, 9, 3, 4);
        layout.Controls.Add(rowLabel, 0, 3);
        var detail = Table(2, 1);
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detail.Controls.Add(TextPanel(T("ReferenceTextReadOnly"), englishText), 0, 0);
        detail.Controls.Add(TextPanel(T("TargetTextEditHere"), targetText), 1, 0);
        targetText.TextChanged += (_, _) => ApplyTargetText(targetText);
        targetText.KeyDown += TargetKeyDown;
        layout.Controls.Add(detail, 0, 4);

        var navigation = Flow();
        navigation.Controls.AddRange([previousButton, nextButton, new Label
        {
            Text = T("DoubleClickOrPressF2To"),
            AutoSize = true, ForeColor = Muted, Margin = new Padding(12, 7, 0, 0)
        }]);
        layout.Controls.Add(navigation, 0, 5);
    }

    private void BuildConverter()
    {
        var content = Table(1, 2);
        content.Padding = new Padding(24, 20, 24, 16);
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var description = new Label
        {
            AutoSize = true, Text = T("ConvertOasisLanguageFilesToReadable"),
            Font = new Font("Segoe UI Semibold", 13), Margin = new Padding(3, 4, 3, 12)
        };
        content.Controls.Add(description, 0, 0);

        var conversions = Flow();
        var decode = Button(T("DecryptInfEncInf"), T("OpensTheEncryptedFileAndSaves"), primary: true);
        var encode = Button(T("EncryptInfInfEnc"), T("ConvertsReadableTextIntoTheEncrypted"));
        decode.Click += async (_, _) => await ConvertFile(true, decode, encode);
        encode.Click += async (_, _) => await ConvertFile(false, decode, encode);
        conversions.Controls.AddRange([decode, encode]);
        content.Controls.Add(conversions, 0, 1);
        converterTab.Controls.Add(content);
    }

    private async Task ConvertFile(bool decrypt, params Button[] buttons)
    {
        if (converting)
            return;
        var input = ChooseInput(decrypt ? EncFilter : InfFilter);
        if (input is null)
            return;
        var output = ChooseConversionOutput(input, decrypt);
        if (output is null)
            return;

        converting = true;
        foreach (var button in buttons)
            button.Enabled = false;
        UseWaitCursor = true;
        Say(T("ConvertingFile"));
        try
        {
            await Task.Run(() =>
            {
                var data = OasisCipher.Convert(File.ReadAllBytes(input), decrypt);
                SafeFile.Write(output.Value.Path, data, overwrite: output.Value.Overwrite);
            });
            Say(T("ConversionCompleted0", output.Value.Path));
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ShowError(ex);
        }
        finally
        {
            converting = false;
            UseWaitCursor = false;
            foreach (var button in buttons)
                button.Enabled = true;
        }
    }

    private void OpenEnglish() => Run(() => LoadReference(EmbeddedOasis.Read("Oasis.English"), null));

    private void OpenReference()
    {
        var path = ChooseInput(InfFilter);
        if (path is not null)
            Run(() => LoadReference(File.ReadAllBytes(path), path));
    }

    private void LoadReference(byte[] bytes, string? path)
    {
        var loaded = OasisDocument.Parse(bytes);
        if (!ConfirmDiscard())
            return;

        reference = loaded;
        target = OasisDocument.Parse(bytes);
        referencePath = path;
        targetPath = null;
        dirty = false;
        PopulateRows();
    }

    private void PopulateRows()
    {
        syncing = true;
        try
        {
            grid.SelectedIndices.Clear();
            rowCodes = reference?.Codes.ToArray() ?? [];
            UpdateViewport(0);
            if (grid.IsHandleCreated && rowCodes.Length > 0)
                SelectRow(0);
        }
        finally
        {
            syncing = false;
        }
        LoadSelectedRow();
        UpdateState();

        Say(rowCodes.Length == 0 ? T("ToGetStartedUploadTheOriginal")
            : T("Loaded0N0Rows", rowCodes.Length));
    }

    private void LoadSelectedRow()
    {
        if (syncing)
            return;
        syncing = true;
        try
        {
            int index = SelectedRowIndex;
            selectedCode = (uint)index < (uint)rowCodes.Length ? rowCodes[index] : null;
            var original = selectedCode is null ? null : reference?.GetText(selectedCode);
            var translation = selectedCode is null ? null : target?.GetText(selectedCode);
            englishText.Text = original ?? "";
            targetText.Text = translation ?? "";
            targetText.Enabled = translation is not null;
            rowLabel.Text = selectedCode is null ? T("NoRowSelected") : T("Code0Line1N02", selectedCode, index + 1, rowCodes.Length)
                + (translation is null ? T("MissingFromTheTargetFile") : "");
        }
        finally
        {
            syncing = false;
        }
    }

    private void ApplyTargetText(TextBox editor)
    {
        if (syncing || selectedCode is not { } code || target is not { } document)
            return;
        var value = editor.Text.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n").Replace("\0", "");
        syncing = true;
        try
        {
            if (value != editor.Text)
            {
                int position = editor.SelectionStart;
                editor.Text = value;
                editor.SelectionStart = Math.Min(position, value.Length);
                Say(T("PastedLineBreaksHaveBeenConverted"));
            }

            var otherEditor = editor == inlineText ? targetText : inlineText;
            if (otherEditor.Text != value)
                otherEditor.Text = value;
        }
        finally
        {
            syncing = false;
        }
        if (value == document.GetText(code))
            return;
        document.SetText(code, value);
        int index = SelectedRowIndex;
        if (index >= 0)
            grid.RedrawItems(index - viewportStart, index - viewportStart, invalidateOnly: true);
        if (!dirty)
        {
            dirty = true;
            UpdateState();
            Say(T("ThereAreUnsavedChanges"));
        }
    }

    private bool Save(bool saveAs)
    {
        if (target is null)
            return false;
        var path = targetPath;
        if (saveAs || path is null)
        {
            string name = referencePath is null ? "GRP_Oasis" : Path.GetFileNameWithoutExtension(referencePath);
            path = ChooseOutput(InfFilter, "inf", path is null ? name + "_Hedef.inf" : Path.GetFileName(path),
                path ?? referencePath);
            if (path is null)
                return false;
        }

        return Run(() =>
        {
            if (referencePath is not null && SafeFile.SamePath(path, referencePath))
                throw new IOException(T("SaveTheTargetInADifferent"));
            SafeFile.Write(path, target.ToBytes(), overwrite: true);
            targetPath = path;
            dirty = false;
            UpdateState();
            Say(T("TargetFileSaved0", path));
        });
    }

    private bool ConfirmDiscard()
    {
        if (!dirty)
            return true;
        var result = MessageBox.Show(this, T("SaveChangesToTargetFile"), AppName,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || (result == DialogResult.Yes && Save(false));
    }

    private void TargetKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyData != Keys.Enter)
            return;
        e.SuppressKeyPress = true;
        MoveRow(1, sender == inlineText);
    }

    private void MoveRow(int direction, bool inline = false)
    {
        int current = SelectedRowIndex;
        if (current < 0)
            return;
        for (int index = current + direction; index >= 0 && index < rowCodes.Length; index += direction)
        {
            if (target?.Contains(rowCodes[index]) != true)
                continue;
            SelectRow(index);
            if (inline)
                StartInlineEdit();
            else
                FocusTarget();
            return;
        }
        Say(direction > 0 ? T("YouReOnTheLastEditable") : T("YouReOnTheFirstEditable"));
    }

    private int SelectedRowIndex => grid.SelectedIndices.Count == 0 ? -1 : viewportStart + grid.SelectedIndices[0];

    private void ResizeViewport()
    {
        int rowHeight = grid.IsHandleCreated && grid.VirtualListSize > 0
            ? grid.GetItemRect(0).Height : grid.Font.Height + 4;
        int headerHeight = grid.IsHandleCreated && grid.VirtualListSize > 0
            ? grid.GetItemRect(0).Top : grid.Font.Height + 10;
        int count = Math.Max(1, (grid.ClientSize.Height - Math.Max(0, headerHeight)) / Math.Max(1, rowHeight));
        if (count == viewportRows)
            return;
        viewportRows = count;
        ScrollTo(viewportStart);
    }

    private void UpdateViewport(int start)
    {
        viewportStart = Math.Clamp(start, 0, Math.Max(0, rowCodes.Length - viewportRows));
        grid.VirtualListSize = Math.Min(viewportRows, rowCodes.Length - viewportStart);
        translationScroll.Maximum = Math.Max(0, rowCodes.Length - 1);
        translationScroll.LargeChange = viewportRows;
        translationScroll.SmallChange = 1;
        translationScroll.Value = viewportStart;
        translationScroll.Enabled = rowCodes.Length > viewportRows;
        grid.Invalidate();
    }

    private void ScrollTo(int start)
    {
        if (syncing || rowCodes.Length == 0)
            return;
        int selection = SelectedRowIndex;
        syncing = true;
        try
        {
            grid.SelectedIndices.Clear();
            UpdateViewport(start);
            int index = selection >= viewportStart && selection < viewportStart + grid.VirtualListSize
                ? selection : viewportStart;
            SetVisibleSelection(index);
        }
        finally
        {
            syncing = false;
        }
        LoadSelectedRow();
    }

    private void SelectRow(int index)
    {
        bool restoreSync = syncing;
        syncing = true;
        try
        {
            grid.SelectedIndices.Clear();
            if (index < viewportStart || index >= viewportStart + grid.VirtualListSize)
                UpdateViewport(index < viewportStart ? index : index - viewportRows + 1);
            SetVisibleSelection(index);
        }
        finally
        {
            syncing = restoreSync;
        }
        LoadSelectedRow();
    }

    private void SetVisibleSelection(int index)
    {
        int itemIndex = index - viewportStart;
        grid.SelectedIndices.Add(itemIndex);
        grid.FocusedItem = grid.Items[itemIndex];
    }

    private void FocusTarget()
    {
        targetText.Focus();
        targetText.SelectAll();
    }

    private void StartInlineEdit()
    {
        if (!targetText.Enabled || SelectedRowIndex < 0)
            return;

        syncing = true;
        try
        {
            inlineText.Text = targetText.Text;
        }
        finally
        {
            syncing = false;
        }

        if (!PositionInlineEditor())
        {
            FocusTarget();
            return;
        }

        inlineText.Show();
        inlineText.BringToFront();
        inlineText.Focus();
        inlineText.SelectAll();
    }

    private void RepositionInlineEditor()
    {
        if (inlineText.Visible)
            PositionInlineEditor();
    }

    private bool PositionInlineEditor()
    {
        int index = grid.SelectedIndices.Count == 0 ? -1 : grid.SelectedIndices[0];
        if (index < 0)
        {
            inlineText.Hide();
            return false;
        }

        var cell = grid.Items[index].SubItems[3].Bounds;
        if (cell.Width <= 0 || cell.Height <= 0 || cell.Bottom > grid.ClientSize.Height || cell.Right <= 0)
        {
            inlineText.Hide();
            return false;
        }

        inlineText.Bounds = Rectangle.Intersect(cell, grid.ClientRectangle);
        return inlineText.Width > 0;
    }

    private void FindNext()
    {
        string query = search.Text.Trim();
        if (query.Length == 0 || rowCodes.Length == 0)
            return;
        int current = SelectedRowIndex;
        int exactCode = query.All(char.IsAsciiDigit) ? Array.IndexOf(rowCodes, query) : -1;
        if (exactCode >= 0)
        {
            SelectRow(exactCode);
            return;
        }
        for (int offset = 1; offset <= rowCodes.Length; offset++)
        {
            int index = (current + offset) % rowCodes.Length;
            var code = rowCodes[index];
            const StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;
            if (!code.Contains(query, comparison) &&
                reference?.GetText(code)?.Contains(query, comparison) != true &&
                target?.GetText(code)?.Contains(query, comparison) != true)
                continue;
            SelectRow(index);
            return;
        }
        Say(T("TheRequestedCodeOrTextWas"));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (tabs.SelectedTab == editorTab)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S:
                    Save(false);
                    return true;
                case Keys.Control | Keys.Shift | Keys.S:
                    Save(true);
                    return true;
                case Keys.Control | Keys.F:
                    search.Focus();
                    search.SelectAll();
                    return true;
                case Keys.Enter when search.Focused:
                    FindNext();
                    return true;
                case Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End when grid.Focused:
                    int current = Math.Max(0, SelectedRowIndex);
                    int index = keyData switch
                    {
                        Keys.Up => current - 1,
                        Keys.Down => current + 1,
                        Keys.PageUp => current - viewportRows,
                        Keys.PageDown => current + viewportRows,
                        Keys.Home => 0,
                        _ => rowCodes.Length - 1
                    };
                    if (rowCodes.Length > 0)
                        SelectRow(Math.Clamp(index, 0, rowCodes.Length - 1));
                    return true;
                case Keys.F2 when grid.ContainsFocus || targetText.Focused:
                    StartInlineEdit();
                    return true;
                case Keys.Escape when inlineText.Focused:
                    inlineText.Hide();
                    grid.Focus();
                    return true;
                case Keys.Enter when targetText.Focused || grid.ContainsFocus:
                    MoveRow(1, inlineText.Focused || grid.Focused);
                    return true;
            }
        }
        if (keyData == Keys.F1)
        {
            ShowHelp();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateState()
    {
        Text = AppName + (dirty ? " *" : "");
        referenceLabel.Text = T("Reference") + (reference is null ? T("NotLoaded") :
            referencePath is null ? T("EmbeddedGrpEnglish") : Path.GetFileName(referencePath));
        targetLabel.Text = T("Target") + (target is null ? T("NotLoaded") :
            targetPath is null ? T("NotSavedYet") : Path.GetFileName(targetPath))
            + (dirty ? " *" : "");
        findButton.Enabled = search.Enabled = rowCodes.Length > 0;
        saveButton.Enabled = saveAsButton.Enabled = previousButton.Enabled = nextButton.Enabled = target is not null;
    }

    private void ShowHelp()
    {
        string text = T("EditorHelp");
        MessageBox.Show(this, text, T("ShortcutsAndHelp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string? ChooseInput(string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    internal static string ConversionFileName(string name, bool decrypt)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        while (name.EndsWith(".enc", comparison) || name.EndsWith(".inf", comparison))
            name = name[..^4];
        return name + (decrypt ? ".inf" : ".inf.enc");
    }

    private (string Path, bool Overwrite)? ChooseConversionOutput(string input, bool decrypt)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = decrypt ? InfFilter : EncFilter,
            AddExtension = false,
            OverwritePrompt = false,
            FileName = ConversionFileName(Path.GetFileName(input), decrypt),
            InitialDirectory = Path.GetDirectoryName(input)
        };
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var output = Path.Combine(Path.GetDirectoryName(dialog.FileName)!,
                ConversionFileName(Path.GetFileName(dialog.FileName), decrypt));
            dialog.FileName = Path.GetFileName(output);
            if (SafeFile.SamePath(input, output))
            {
                ShowError(new IOException(T("SelectDifferentFilesForInputAnd")));
                continue;
            }
            if ((targetPath is not null && SafeFile.SamePath(output, targetPath)) ||
                (referencePath is not null && SafeFile.SamePath(output, referencePath)))
            {
                ShowError(new IOException(T("SaveTheOutputInADifferent")));
                continue;
            }
            bool exists = File.Exists(output);
            if (!exists || MessageBox.Show(this, T("ConfirmConversionOverwrite", output),
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                return (output, exists);
        }
        return null;
    }

    private string? ChooseOutput(string filter, string extension, string suggested, string? beside)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = filter, DefaultExt = extension, AddExtension = true, OverwritePrompt = true,
            SupportMultiDottedExtensions = true,
            FileName = suggested, InitialDirectory = beside is null ? "" : Path.GetDirectoryName(beside)
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ShowError(ex);
            return false;
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException;

    private void ShowError(Exception ex)
    {
        Say(T("TheOperationCouldNotBeCompleted"));
        MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Say(string message) => status.Text = message;

    private Button Button(string text, string help, Action? action = null, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(80, 33), Margin = new Padding(2, 2, 5, 4),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(16, 89, 137) : Color.White,
            ForeColor = primary ? Color.White : Ink, Padding = new Padding(7, 0, 7, 0),
            AccessibleDescription = help
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(156, 175, 190);
        if (action is not null)
            button.Click += (_, _) => action();
        tips.SetToolTip(button, help);
        return button;
    }

    private static TextBox EditorBox(bool readOnly) => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        WordWrap = true, ReadOnly = readOnly, BackColor = Color.White, MaxLength = 0,
        AccessibleName = readOnly ? T("ReferenceText") : T("EditableTargetText")
    };

    private static GroupBox TextPanel(string title, TextBox editor)
    {
        var panel = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(7) };
        panel.Controls.Add(editor);
        return panel;
    }

    private static TableLayoutPanel Table(int columns, int rows) =>
        new() { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty };

    private static FlowLayoutPanel Flow() =>
        new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
}

