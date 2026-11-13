using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GrpLocalizationEditor;

internal static class Ui
{
    internal sealed record Language(string Code, string Name, Dictionary<string, string> Strings);
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Languages");
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "GRP Localization Editor", "language.txt");
    private static readonly Regex CultureCode = new(@"\A[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*\z");
    private static readonly Language English = ReadEnglish();
    public static IReadOnlyList<Language> Languages { get; } = ReadLanguages();
    private static Language active = English;
    public static string Preference { get; private set; } = "";
    public static string ActiveCode => active.Code;

    public static void Initialize()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Preference = File.ReadAllText(SettingsPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Preference = Languages.FirstOrDefault(language =>
            language.Code.Equals(Preference, StringComparison.OrdinalIgnoreCase))?.Code ?? "";
        active = Resolve(CultureInfo.CurrentUICulture.Name, Preference);
    }

    internal static Language Resolve(string system, string preference)
    {
        var requested = string.IsNullOrEmpty(preference) ? system : preference;
        while (requested.Length > 0)
        {
            var language = Languages.FirstOrDefault(item => item.Code.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (language is not null)
                return language;
            int split = requested.LastIndexOf('-');
            requested = split < 0 ? "" : requested[..split];
        }
        return Languages.First(item => item.Code == "en");
    }

    public static void SavePreference(string code)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        SafeFile.Write(SettingsPath, System.Text.Encoding.UTF8.GetBytes(code), overwrite: true);
        Preference = code;
    }

    public static string T(string key, params object?[] args)
    {
        var fallback = English.Strings.GetValueOrDefault(key, key);
        var value = active.Strings.GetValueOrDefault(key);
        if (string.IsNullOrWhiteSpace(value))
            value = fallback;
        if (args.Length == 0)
            return value;
        try { return string.Format(CultureInfo.CurrentCulture, value, args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, fallback, args); }
    }

    private static Language ReadEnglish()
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("UI.English")!;
        return JsonSerializer.Deserialize<Language>(stream)!;
    }

    private static IReadOnlyList<Language> ReadLanguages()
    {
        var result = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase) { ["en"] = English };
        if (!Directory.Exists(DirectoryPath))
            return [English];
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            try
            {
                var language = JsonSerializer.Deserialize<Language>(File.ReadAllText(path));
                if (language is not null && language.Code is not null && CultureCode.IsMatch(language.Code) &&
                    !string.IsNullOrWhiteSpace(language.Name) && language.Strings is not null)
                    result[language.Code] = language;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
