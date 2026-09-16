using Microsoft.Extensions.Logging;
using ShortP2P.Auth;
using ShortP2P.Auth.Data;

namespace ShortP2P.WinForms;

/// <summary>Редактирование своего Avatar и AboutMe (только локально).</summary>
public sealed class ProfileForm : Form
{
    private readonly AuthService _auth;
    private readonly ILogger<ProfileForm> _logger;
    private readonly PictureBox _avatarPreview = new()
    {
        Width = 96,
        Height = 96,
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly TextBox _aboutMe = new()
    {
        Multiline = true,
        Width = 420,
        Height = 100,
        MaxLength = PeerProfileLimits.MaxAboutMeChars,
        ScrollBars = ScrollBars.Vertical
    };

    private readonly Label _aboutCounter = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _loadAvatar = new() { Text = "Выбрать аватар…", AutoSize = true };
    private readonly Button _clearAvatar = new() { Text = "Убрать аватар", AutoSize = true };
    private readonly Button _save = new() { Text = "Сохранить", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };

    private byte[]? _avatarBytes;

    public ProfileForm(AuthService auth, ILogger<ProfileForm> logger)
    {
        _auth = auth;
        _logger = logger;
        Text = "Мой профиль";
        StartPosition = FormStartPosition.CenterParent;
        Width = 520;
        Height = 360;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var avatarRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        avatarRow.Controls.Add(_avatarPreview);
        var avatarBtns = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 0, 0, 0)
        };
        avatarBtns.Controls.Add(_loadAvatar);
        avatarBtns.Controls.Add(_clearAvatar);
        avatarRow.Controls.Add(avatarBtns);

        var aboutLabel = new Label
        {
            AutoSize = true,
            Text = $"О себе (до {PeerProfileLimits.MaxAboutMeChars} символов):"
        };
        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(460, 0),
            Text =
                $"Аватар — изображение до {PeerProfileLimits.MaxAvatarBytes / 1024} КБ. " +
                "Данные хранятся только локально и отдаются пирам при скане сети."
        };

        var bottom = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        bottom.Controls.Add(_cancel);
        bottom.Controls.Add(_save);

        root.Controls.Add(avatarRow, 0, 0);
        root.Controls.Add(aboutLabel, 0, 1);
        root.Controls.Add(_aboutMe, 0, 2);
        root.Controls.Add(_aboutCounter, 0, 3);
        root.Controls.Add(hint, 0, 4);
        root.Controls.Add(bottom, 0, 5);
        Controls.Add(root);

        _loadAvatar.Click += (_, _) => OnLoadAvatar();
        _clearAvatar.Click += (_, _) =>
        {
            _avatarBytes = null;
            ApplyAvatarPreview(null);
        };
        _aboutMe.TextChanged += (_, _) => UpdateAboutCounter();
        _save.Click += async (_, _) => await OnSaveAsync().ConfigureAwait(true);
        AcceptButton = _save;
        CancelButton = _cancel;

        Shown += (_, _) => LoadCurrentProfileBestEffort();
    }

    private void LoadCurrentProfileBestEffort()
    {
        try
        {
            var user = _auth.CurrentUser;
            if (user == null)
                return;
            _aboutMe.Text = user.AboutMe ?? "";
            _avatarBytes = user.Avatar;
            ApplyAvatarPreview(_avatarBytes);
            UpdateAboutCounter();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load own profile into UI (best-effort); continuing with empty fields");
            _aboutMe.Text = "";
            _avatarBytes = null;
            ApplyAvatarPreview(null);
            UpdateAboutCounter();
        }
    }

    private void UpdateAboutCounter()
    {
        _aboutCounter.Text = $"{_aboutMe.Text.Length} / {PeerProfileLimits.MaxAboutMeChars}";
    }

    private void OnLoadAvatar()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Выбор аватара",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > PeerProfileLimits.MaxAvatarBytes)
            {
                MessageBox.Show(this,
                    $"Файл больше {PeerProfileLimits.MaxAvatarBytes / 1024} КБ ({bytes.Length} байт).",
                    "Аватар", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Image.FromStream keeps the stream open; clone into a Bitmap so we can dispose the stream.
            using (var ms = new MemoryStream(bytes, false))
            using (var img = Image.FromStream(ms))
            {
                _ = img.Width;
            }

            _avatarBytes = bytes;
            ApplyAvatarPreview(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load avatar file (best-effort)");
            MessageBox.Show(this, "Не удалось загрузить изображение.", "Аватар", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ApplyAvatarPreview(byte[]? bytes)
    {
        var previous = _avatarPreview.Image;
        _avatarPreview.Image = null;
        previous?.Dispose();
        if (bytes == null || bytes.Length == 0)
            return;
        try
        {
            using var ms = new MemoryStream(bytes, false);
            using var img = Image.FromStream(ms);
            _avatarPreview.Image = new Bitmap(img);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preview avatar (best-effort)");
        }
    }

    private async Task OnSaveAsync()
    {
        try
        {
            var (ok, error) = await _auth.UpdateProfileAsync(_aboutMe.Text, _avatarBytes).ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(this, error ?? "Ошибка сохранения", "Профиль", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save own profile");
            MessageBox.Show(this, "Не удалось сохранить профиль.", "Профиль", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var img = _avatarPreview.Image;
            _avatarPreview.Image = null;
            img?.Dispose();
        }

        base.Dispose(disposing);
    }
}
