using static GrpLocalizationEditor.Ui;
namespace GrpLocalizationEditor;

internal static class EmbeddedOasis
{
    public static byte[] Read(string name)
    {
        using var stream = typeof(EmbeddedOasis).Assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException(T("EmbeddedOasisResourceNotFound0", name));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
