namespace ShortP2P.WinForms;

internal sealed class MessageViewForm : Form
{
    public MessageViewForm(string title, string messageText)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 420;

        var viewer = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = true,
            Text = messageText,
        };

        Controls.Add(viewer);
    }
}
