using static GrpLocalizationEditor.Ui;
using System.Text;
using System.Text.RegularExpressions;

namespace GrpLocalizationEditor;

internal sealed class OasisDocument
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Lines = new(@"([^\r\n]*)(\r\n|\n|\r|$)", RegexOptions.Compiled);
    private readonly List<Line> lines = [];
    private readonly Dictionary<string, Line> entries = new(StringComparer.Ordinal);
    private bool hasBom;

    public IEnumerable<string> Codes => entries.Keys;
    public int Count => entries.Count;

    private sealed class Line(string prefix, string text, string ending)
    {
        public string Prefix { get; } = prefix;
        public string Text { get; set; } = text;
        public string Ending { get; } = ending;
    }

    public static OasisDocument Read(string path) => Parse(File.ReadAllBytes(path));

    public static OasisDocument Parse(ReadOnlySpan<byte> bytes)
    {
        var document = new OasisDocument { hasBom = bytes.StartsWith(Encoding.UTF8.Preamble) };
        var text = Utf8.GetString(document.hasBom ? bytes[Encoding.UTF8.Preamble.Length..] : bytes);
        if (text.Contains('\0'))
            throw new InvalidDataException(T("TheFileIsNotAValid"));

        foreach (Match match in Lines.Matches(text))
        {
            if (match.Length == 0)
                continue;

            var content = match.Groups[1].Value;
            var ending = match.Groups[2].Value;
            int equals = content.IndexOf('=');
            if (equals > 0 && content.AsSpan(0, equals).IndexOfAnyExceptInRange('0', '9') < 0)
            {
                var code = content[..equals];
                var line = new Line(content[..(equals + 1)], content[(equals + 1)..], ending);
                if (!document.entries.TryAdd(code, line))
                    throw new InvalidDataException(T("ThereIsADuplicateLineCode", code));
                document.lines.Add(line);
            }
            else
            {
                document.lines.Add(new Line("", content, ending));
            }
        }

        if (document.Count == 0)
            throw new InvalidDataException(T("NoOasisLineStartingWithNumeric"));
        return document;
    }

    public bool Contains(string code) => entries.ContainsKey(code);
    public string? GetText(string code) => entries.TryGetValue(code, out var line) ? line.Text : null;

    public void SetText(string code, string text)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
            throw new InvalidDataException(T("UseNForInGameLine"));
        entries[code].Text = text;
    }

    public byte[] ToBytes()
    {
        var output = new StringBuilder();
        if (hasBom)
            output.Append('\uFEFF');
        foreach (var line in lines)
            output.Append(line.Prefix).Append(line.Text).Append(line.Ending);
        return Utf8.GetBytes(output.ToString());
    }
}
