using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly TextBox _nick = new() { PlaceholderText = "Nickname" };
    private readonly TextBox _pass = new() { PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly Button _btnLogin = new() { Text = "Login", DialogResult = DialogResult.None };
    private readonly Button _btnRegister = new() { Text = "Register" };
    private readonly Button _btnCancel = new() { Text = "Exit", DialogResult = DialogResult.Cancel };

    public LoginForm(AuthService auth)
    {
        _auth = auth;
        Text = "ShortP2P — Login";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AcceptButton = _btnLogin;
        CancelButton = _btnCancel;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            Dock = DockStyle.Fill,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_nick, 0, 0);
        layout.Controls.Add(_pass, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(_btnLogin);
        buttons.Controls.Add(_btnRegister);
        buttons.Controls.Add(_btnCancel);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

        _nick.Width = 320;
        _pass.Width = 320;

        _btnLogin.Click += async (_, _) => await OnLoginAsync().ConfigureAwait(true);
        _btnRegister.Click += OnRegisterClicked;
        Load += async (_, _) => await TryRestoreAsync().ConfigureAwait(true);
    }

    private async Task TryRestoreAsync()
    {
        if (await _auth.TryRestoreSessionAsync().ConfigureAwait(true) && _auth.CurrentUser != null)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async Task OnLoginAsync()
    {
        var nick = _nick.Text.Trim();
        var pass = _pass.Text ?? "";
        var (ok, err) = await _auth.LoginAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            MessageBox.Show(this, err ?? "Login failed.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnRegisterClicked(object? sender, EventArgs e)
    {
        using var reg = new RegisterForm(_auth);
        if (reg.ShowDialog(this) != DialogResult.OK)
            return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
