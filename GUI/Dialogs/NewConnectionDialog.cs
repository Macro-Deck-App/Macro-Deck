using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class NewConnectionDialog : DialogForm
    {

        private MacroDeckClient _macroDeckClient;

        private int _denyTimeout = 15;

        private System.Timers.Timer _denyTimer;

        public bool Blocked
        {
            get => this.checkBlockThisDevice.Checked;
        }


        public NewConnectionDialog(MacroDeckClient macroDeckClient)
        {
            this._macroDeckClient = macroDeckClient;
            InitializeComponent();
            (new DropShadow()).ApplyShadows(this);
            this.btnDeny.BackColor = Color.FromArgb(192, 0, 0);
            this.lblNewConnectionRequest.Text = LanguageManager.Strings.NewConnectionRequest;
            this.lblClientId.Text = LanguageManager.Strings.ClientId;
            this.lblIPAddress.Text = LanguageManager.Strings.IPAddress;
            this.lblType.Text = LanguageManager.Strings.Type;
            this.btnAccept.Text = LanguageManager.Strings.Accept;
            this.btnDeny.Text = $"{LanguageManager.Strings.Deny} ({this._denyTimeout})";
            this.checkBlockThisDevice.Text = LanguageManager.Strings.BlockConnection;
        }

        private void NewConnectionDialog_Load(object sender, EventArgs e)
        {
            this.CenterToParent();
            this.TopMost = true;
            this.clientId.Text = this._macroDeckClient?.ClientId;
            this.ipAddress.Text = this._macroDeckClient?.SocketConnection?.ConnectionInfo?.ClientIpAddress;
            this.type.Text = this._macroDeckClient?.DeviceType.ToString();

            this._denyTimer = new System.Timers.Timer()
            {
                Enabled = true,
                Interval = 1000
            };
            this._denyTimer.Elapsed += (sender, e) => {
                this._denyTimeout--;
                this.btnDeny.Text = $"{LanguageManager.Strings.Deny} ({this._denyTimeout})";
                if (this._denyTimeout <= 0)
                {
                    this._denyTimer.Stop();
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.Invoke(new Action(() => this.Close()));
                    }
                }
            };
        }

        private void BtnAccept_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Yes;
            this.Close();
        }

        private void BtnDeny_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
