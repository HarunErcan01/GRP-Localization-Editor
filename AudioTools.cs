using static GrpLocalizationEditor.Ui;
using System.Diagnostics;

namespace GrpLocalizationEditor;

internal static class AudioTools
{
    public static string? FindFfmpeg()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    public static byte[] DecodeWav(string ffmpeg, byte[] input) =>
        WithTemporaryFiles(input, ".ogg", ".wav", (source, output) =>
            Run(ffmpeg, "-i", source, "-map", "0:a:0", "-c:a", "pcm_s16le", output));

    public static byte[] EncodeVorbis(string ffmpeg, string source, int channels, int rate) =>
        WithTemporaryFiles(null, "", ".ogg", (_, output) =>
            Run(ffmpeg, "-i", source, "-map", "0:a:0", "-c:a", "libvorbis", "-q:a", "5",
                "-ac", channels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-ar", rate.ToString(System.Globalization.CultureInfo.InvariantCulture), output));

    public static int BinkTracks(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[44];
        file.ReadExactly(header);
        int count = BaoPackage.Number(header, 0x28);
        if (!header.StartsWith("BIK"u8) || count > 64)
            throw new InvalidDataException(T("ThisBinkVideoTitleIsNot"));
        return count;
    }

    public static byte[] ExtractVideoTrack(string ffmpeg, string source, int index) =>
        WithTemporaryFiles(null, "", ".wav", (_, output) =>
            Run(ffmpeg, "-i", source, "-map", $"0:a:{index}", "-c:a", "pcm_s16le", output));

    private static byte[] WithTemporaryFiles(byte[]? input, string inputExtension, string outputExtension, Action<string, string> convert)
    {
        var directory = Directory.CreateTempSubdirectory("GRPAudio-");
        try
        {
            var source = Path.Combine(directory.FullName, "input" + inputExtension);
            var output = Path.Combine(directory.FullName, "output" + outputExtension);
            if (input is not null)
                File.WriteAllBytes(source, input);
            convert(source, output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" }.Concat(arguments))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException(T("FfmpegFailedToStart"));
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new IOException(T("FfmpegOperationTimedOut"));
        }
        var message = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new IOException(T("FfmpegOperationCouldNotBeCompleted", message.Trim()));
    }
}
