using Microsoft.Win32;
using SuchByte.MacroDeck.GUI.CustomControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class CrashReportDialog : DialogForm
    {
        private int _secondsAutoClose = 15;

        public CrashReportDialog(string crashReport)
        {
            InitializeComponent();
            this.SetCloseIconVisible(false);
            this.crashReport.Text = "Macro Deck version: " + MacroDeck.VersionString + Environment.NewLine +
                                "Windows version: " + Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", "").ToString() + " " + (Environment.Is64BitOperatingSystem ? "64 bit" : "32 bit") + Environment.NewLine +
                                Environment.NewLine + 
                                crashReport;
            this.autoContinueTimer.Tick += AutoContinueTimerTick;
            this.crashReport.MouseClick += delegate
            {
                this.CancelAutoContinueTimer();
            };
            this.comment.MouseClick += delegate
            {
                this.CancelAutoContinueTimer();
            };
        }

        private void BtnSendCrashReport_Click(object sender, EventArgs e)
        {
            string crashMessage = this.crashReport.Text + Environment.NewLine + Environment.NewLine + "User comment: " + Environment.NewLine + comment.Text;
            try
            {
                new WebClient().DownloadString("https://macrodeck.org/services/crashreport/report2.php?error_msg=" + crashMessage);
            }
            catch { }
            this.Close();
        }


        private void BtnContinue_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void AutoContinueTimerTick(object sender, EventArgs e)
        {
            if (this._secondsAutoClose >= 1)
            {
                this._secondsAutoClose--;
                this.btnContinue.Text = String.Format("Just continue ({0})", this._secondsAutoClose);
            } else
            {
                this.Close();
            }
        }

        private void CancelAutoContinueTimer()
        {
            this.autoContinueTimer.Enabled = false;
            this.btnContinue.Text = "Just continue";
        }


    }
}
