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
    public partial class FeedbackDialog : DialogForm
    {
        public FeedbackDialog()
        {
            InitializeComponent();
        }

        private void BtnSendFeedback_Click(object sender, EventArgs e)
        {
            if (feedback.Text.Length > 10)
            {
                try
                {
                    new WebClient().DownloadString("https://macrodeck.org/services/feedback/feedback.php?feedback=" + "Version: " + MacroDeck.VersionString + Environment.NewLine + this.feedback.Text + (this.email.Text.Length > 0 ? "&email=" + email.Text : ""));
                }
                catch (Exception ex) {}
            }
            this.Close();
        }

        private void FeedbackDialog_Load(object sender, EventArgs e)
        {
            
        }
    }
}
