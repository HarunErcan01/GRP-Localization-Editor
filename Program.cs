namespace GrpLocalizationEditor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Ui.Initialize();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
