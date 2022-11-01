using System;
using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Server;
using Timer = System.Timers.Timer;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class NewConnectionDialog : DialogForm
{

    private MacroDeckClient _macroDeckClient;

    private int _denyTimeout = 15;

    private Timer _denyTimer;

    public bool Blocked => checkBlockThisDevice.Checked;


    public NewConnectionDialog(MacroDeckClient macroDeckClient)
    {
        _macroDeckClient = macroDeckClient;
        InitializeComponent();
        (new DropShadow()).ApplyShadows(this);
        btnDeny.BackColor = Color.FromArgb(192, 0, 0);
        lblNewConnectionRequest.Text = LanguageManager.Strings.NewConnectionRequest;
        lblClientId.Text = LanguageManager.Strings.ClientId;
        lblIPAddress.Text = LanguageManager.Strings.IPAddress;
        lblType.Text = LanguageManager.Strings.Type;
        btnAccept.Text = LanguageManager.Strings.Accept;
        btnDeny.Text = $"{LanguageManager.Strings.Deny} ({_denyTimeout})";
        checkBlockThisDevice.Text = LanguageManager.Strings.BlockConnection;
    }

    private void NewConnectionDialog_Load(object sender, EventArgs e)
    {
        CenterToParent();
        TopMost = true;
        clientId.Text = _macroDeckClient?.ClientId;
        ipAddress.Text = _macroDeckClient?.SocketConnection?.ConnectionInfo?.ClientIpAddress;
        type.Text = _macroDeckClient?.DeviceType.ToString();

        _denyTimer = new Timer
        {
            Enabled = true,
            Interval = 1000
        };
        _denyTimer.Elapsed += (sender, e) => {
            _denyTimeout--;
            btnDeny.Text = $"{LanguageManager.Strings.Deny} ({_denyTimeout})";
            if (_denyTimeout <= 0)
            {
                _denyTimer.Stop();
                if (IsHandleCreated && !IsDisposed)
                {
                    Invoke(() => Close());
                }
            }
        };
    }

    private void BtnAccept_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Yes;
        Close();
    }

    private void BtnDeny_Click(object sender, EventArgs e)
    {
        Close();
    }
}