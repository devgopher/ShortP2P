using Microsoft.Extensions.Logging;
using ShortP2P.Auth;

namespace ShortP2P.WinForms;

public sealed class RegisterForm : Form
{
    private readonly AuthService _auth;
    private readonly Button _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };
    private readonly Button _btnOk = new() { Text = "Create account" };
    private readonly ILogger<RegisterForm> _logger;
    private readonly TextBox _nick = new() { PlaceholderText = "Nickname" };
    private readonly TextBox _pass = new() { PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly ILogger<UserAction> _userActions;

    public RegisterForm(AuthService auth, ILogger<RegisterForm> logger, ILogger<UserAction> userActions)
    {
        _auth = auth;
        _logger = logger;
        _userActions = userActions;
        Text = "ShortP2P — Register";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true };
        layout.Controls.Add(_nick, 0, 0);
        layout.Controls.Add(_pass, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(_btnOk);
        buttons.Controls.Add(_btnCancel);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        _nick.Width = 300;
        _pass.Width = 300;

        _btnOk.Click += async (_, _) => await OnRegisterAsync().ConfigureAwait(true);
        CancelButton = _btnCancel;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && DialogResult != DialogResult.OK)
                _userActions.LogInformation("Register: cancelled");
        };
    }

    private async Task OnRegisterAsync()
    {
        var nick = _nick.Text.Trim();
        var pass = _pass.Text ?? "";
        var (ok, err) = await _auth.RegisterAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            _userActions.LogInformation("Register: failed for nickname {Nickname}", nick);
            _logger.LogWarning("Registration failed for {Nickname}: {Reason}", nick, err);
            MessageBox.Show(this, err ?? "Registration failed.", "Register", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _userActions.LogInformation("Register: success for nickname {Nickname}", nick);
        var id = _auth.CurrentUser?.NetworkIdShort ?? "";
        MessageBox.Show(this, $"Your network id:\n{id}", "Account created", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}