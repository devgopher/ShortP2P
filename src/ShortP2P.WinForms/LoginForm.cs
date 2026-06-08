using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortP2P.Auth;

namespace ShortP2P.WinForms;

public sealed class LoginForm : Form
{
    private readonly AuthService _auth;
    private readonly Button _btnCancel = new() { Text = "Exit", DialogResult = DialogResult.Cancel };
    private readonly Button _btnLogin = new() { Text = "Login", DialogResult = DialogResult.None };
    private readonly Button _btnRegister = new() { Text = "Register" };
    private readonly ILogger<LoginForm> _logger;
    private readonly TextBox _nick = new() { PlaceholderText = "Nickname" };
    private readonly TextBox _pass = new() { PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly IServiceProvider _services;
    private readonly ILogger<UserAction> _userActions;

    public LoginForm(AuthService auth, IServiceProvider services, ILogger<LoginForm> logger,
        ILogger<UserAction> userActions)
    {
        _auth = auth;
        _services = services;
        _logger = logger;
        _userActions = userActions;
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
            Dock = DockStyle.Fill
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_nick, 0, 0);
        layout.Controls.Add(_pass, 0, 1);
        var buttons = new FlowLayoutPanel
            { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
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
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.Cancel)
                _userActions.LogInformation("Login: exit");
        };
    }

    private async Task TryRestoreAsync()
    {
        if (await _auth.TryRestoreSessionAsync().ConfigureAwait(true) && _auth.CurrentUser != null)
        {
            _userActions.LogInformation("Login: session restored for {Nickname}", _auth.CurrentUser.Nickname);
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
            _userActions.LogInformation("Login: failed for nickname {Nickname}", nick);
            _logger.LogWarning("Login failed for {Nickname}: {Reason}", nick, err);
            MessageBox.Show(this, err ?? "Login failed.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _userActions.LogInformation("Login: success for nickname {Nickname}", nick);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnRegisterClicked(object? sender, EventArgs e)
    {
        _userActions.LogInformation("Login: open register");
        using var reg = _services.GetRequiredService<RegisterForm>();
        if (reg.ShowDialog(this) != DialogResult.OK)
            return;
        DialogResult = DialogResult.OK;
        Close();
    }
}