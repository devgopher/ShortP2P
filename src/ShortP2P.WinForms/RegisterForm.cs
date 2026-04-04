using ShortP2P.Client.Services;

namespace ShortP2P.WinForms;

public sealed class RegisterForm : Form
{
    private readonly AuthService _auth;
    private readonly TextBox _nick = new() { PlaceholderText = "Nickname" };
    private readonly TextBox _pass = new() { PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly Button _btnOk = new() { Text = "Create account" };
    private readonly Button _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public RegisterForm(AuthService auth)
    {
        _auth = auth;
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
    }

    private async Task OnRegisterAsync()
    {
        var nick = _nick.Text.Trim();
        var pass = _pass.Text ?? "";
        var (ok, err) = await _auth.RegisterAsync(nick, pass).ConfigureAwait(true);
        if (!ok)
        {
            MessageBox.Show(this, err ?? "Registration failed.", "Register", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var id = _auth.CurrentUser?.NetworkIdShort ?? "";
        MessageBox.Show(this, $"Your network id:\n{id}", "Account created", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
