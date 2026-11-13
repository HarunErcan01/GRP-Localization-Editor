namespace GrpLocalizationEditor;

internal sealed class TranslationListView : ListView
{
    public event EventHandler? ViewportChanged;
    public event EventHandler<int>? WheelScrolled;

    protected override void WndProc(ref Message message)
    {
        const int horizontalScroll = 0x0114;
        const int verticalScroll = 0x0115;
        const int mouseWheel = 0x020A;
        const int horizontalMouseWheel = 0x020E;

        if (message.Msg == mouseWheel && WheelScrolled is not null)
        {
            WheelScrolled.Invoke(this, unchecked((short)(message.WParam.ToInt64() >> 16)));
            return;
        }

        base.WndProc(ref message);
        if (message.Msg is horizontalScroll or verticalScroll or mouseWheel or horizontalMouseWheel)
            ViewportChanged?.Invoke(this, EventArgs.Empty);
    }
}
