# GRP Localization Editor

[Türkçe](README.md)

A Windows localization editor for Ghost Recon Online and Ghost Recon Phantoms. Edit Oasis text side by side, convert between INF and INF.ENC, and extract or repack supported BAO audio.

## Run from source

On Windows with the .NET 10 SDK, run `dotnet build -c Release` and `dotnet run` from the project directory. The build embeds the English game reference and the Oasis key from `Assets`. To use the WAV and Bink features, add FFmpeg as described below.

- `Assets/Oasis_English.inf` is the readable UTF-8 Oasis file used for the built-in English reference.
- `Assets/Oasis.key` contains the 32-byte ASCII Oasis key. Keep the file free of a BOM and a trailing newline.
- `Tools/ffmpeg/ffmpeg.exe` is used for WAV extraction, playback previews, Bink audio extraction, and Vorbis encoding. Use a Windows build with libvorbis support.

## Translation editor

The editor starts empty. Click **Load original English** to open the built-in GRP text or **Open reference INF** to choose another language file. The left column displays the read-only reference. The right column starts with an editable copy of the same text. The row numbers are for display only.

1. Double-click a target cell or press **F2** on the selected row to start editing. For longer strings, you can also use the text box at the bottom right. **Enter** moves to the next row in the same editing area and selects its entire text.
2. The first time you click **Save**, choose the target file. Subsequent saves update that file. Use **Save as** to switch to another target file.

Both columns scroll together in one table, and rows are matched by their numeric IDs. The full file is available in one scrollable list, including when you search or move between rows with Enter.

Saving preserves the target file's IDs, order, line endings, and UTF-8 BOM setting. IDs present in just one file still appear in the table. Rows without a target entry cannot be edited. Use `\n` for an in-game line break and preserve placeholders such as `%s` and `{0}`.

| Button or shortcut | What it does |
| --- | --- |
| Open reference INF | Loads the selected INF into both columns. |
| Load original English | Loads the built-in GRP text into both columns. |
| Save · Ctrl+S | Saves to the current target path. Prompts for a path on the first save. |
| Save as · Ctrl+Shift+S | Saves the target to a location you choose. |
| Find · Ctrl+F | Searches for an ID or text. Press Enter in the search box to select a result. |
| Previous | Moves to the previous editable row. |
| F2 or double-click a target cell | Starts editing the cell in place. |
| Next · Enter | Moves to the next target row and selects its entire text. |
| Esc | Closes in-place editing while keeping your changes. |
| Shortcuts and help · F1 | Explains the buttons and keyboard shortcuts. |

## File conversion

| Button | What it does |
| --- | --- |
| Decrypt | Converts an `.inf.enc` file to a readable UTF-8 `.inf` file. |
| Encrypt | Converts an edited `.inf` file to an Oasis `.inf.enc` file. |

Encrypted output ends in `.inf.enc` and decrypted output ends in `.inf`. The editor removes duplicate extensions from the output name. Choose separate paths for input and output. Conversion uses Twofish in CFB1 mode and the built-in Oasis key.

## Audio packages

1. Click **Open audio folder** and choose the game's installation folder. If you only need BAO audio, you can select the `Sound_Bin_PC` folder directly.
2. Select a package. The list on the right shows each track's entry ID, audio ID, format, channel count, sample rate, duration, and location.
3. Double-click a track or click **Play** to preview it. Use **Extract WAV** to make an editable copy or **Extract Ogg** to save the original Vorbis stream. **Extract All as WAV** and **Extract All as OGG** write supported tracks from the selected bank to individual files. The editor reports skipped tracks and extraction errors.
4. Use **Replace audio** to choose a WAV or Ogg Vorbis file, or **Import audio folder** to prepare multiple replacements at once. You can preview changes before repacking. The editor converts WAV input to the original channel count and sample rate. Ogg input must already match those settings.
5. **Overwrite current audio bank** backs up each original file beside it using numbered suffixes such as `.bak1` and `.bak2`, then writes the pending changes to the affected packages. All backups from one save share the same number. **Save audio bank as** lets you name the bank and SPK files and saves the copies in a new subfolder of your chosen location.

| File type | Contents |
| --- | --- |
| `.pk`, `.hpk` | BAO audio banks. Audio can be stored in the bank or referenced in another package. |
| `.spk` | BAO audio data. See the details pane for associated banks. |
| `.lpk`, `.lhpk` | Banks or external audio references in language folders. |
| `.bik` | Bink video. The editor can extract the audio tracks as WAV files. |

For example, `ABI_english_sadia.pk` links to an external `.spk` package. `Fst_Recon.pk` contains audio directly. `UI_InGame_PC.pk` is a good starting point for in-game interface sounds. The second audio track of `Tutorial.bik` contains English tutorial narration.

Repacking supports single-part, non-looping Vorbis tracks in BAO 28.1.2 format. **Import audio folder** matches tracks in the selected bank by the eight-digit hexadecimal ID at the end of each filename. Keep the exported filenames and use one WAV or Ogg file per ID.

Close the game before updating its packages. You can save audio output in a subfolder of the game directory. Test repacked audio in the game.

The ready-to-run package includes FFmpeg in `Tools/ffmpeg` for WAV conversion, audio previews, and Bink audio extraction. When building from source, place your FFmpeg distribution's `ffmpeg.exe`, license, and distribution notes in the same folder inside the project.

## Build and publish

From the project directory on Windows with the .NET 10 SDK, run:

```powershell
dotnet build -c Release
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

`MainForm.cs` defines the user interface, `OasisDocument.cs` handles the text format, `OasisCipher.cs` handles encryption, and `SafeFile.cs` writes files. The Twofish implementation comes from BouncyCastle.Cryptography. The build embeds the English reference and Oasis key from `Assets`.

## Interface languages

Use the menu in the upper right to change the interface language. **System language** uses your Windows display language. For example, `de-AT` uses German if available. If no supported language matches, the editor uses English. The editor remembers any language you select manually. This setting controls only the editor's interface language.

To add a translation, copy `Languages/en.json` in the ready-to-run package to a new JSON file in the same folder:

1. Set `Code` to the language code and `Name` to the name that should appear in the menu, such as `nl` and `Nederlands`.
2. Translate the values in `Strings`. Preserve the keys, placeholders such as `{0}` and `{1:N0}`, and the `|` separators in file filters.
3. Save the file as UTF-8 and reopen the editor. Missing strings fall back to English. Rebuilding is unnecessary.

## Author

Harun Ercan · [GitHub](https://github.com/HarunErcan01)

## Technologies and references

- C# and Windows Forms provide the desktop interface. [.NET Windows Forms](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/)
- Bouncy Castle provides the Twofish implementation used for Oasis conversion. [Bouncy Castle for C#](https://github.com/bcgit/bc-csharp) · [NuGet package](https://www.nuget.org/packages/BouncyCastle.Cryptography)
- FFmpeg and libvorbis handle WAV and Ogg conversion and decode Bink audio. [FFmpeg](https://ffmpeg.org/) · [Xiph.Org Vorbis](https://xiph.org/vorbis/) · [Gyan Windows builds](https://www.gyan.dev/ffmpeg/builds/)
- The vgmstream source code was used to study Ubisoft's BAO format and independently verify the repacked audio. [vgmstream](https://github.com/vgmstream/vgmstream) · [Ubisoft BAO reader](https://github.com/vgmstream/vgmstream/blob/master/src/meta/ubi_bao.c)
- zeroKilo's GROBackendWV wiki notes on language files provided a starting point for the Oasis format investigation. [Useful binary edits](https://github.com/zeroKilo/GROBackendWV/wiki/Useful-binary-edits)
- Google Translate helped draft the interface translations. You can edit them in the JSON language files. [Google Translate](https://translate.google.com/)
