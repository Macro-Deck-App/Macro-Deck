using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SuchByte.MacroDeck.GUI.Dialogs;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class Form : System.Windows.Forms.Form
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();

        public Form()
        {
            InitializeComponent();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Pen pen = new Pen(Color.FromArgb(0, 123, 255), 1);
            Rectangle rect = new Rectangle(0, 0, this.Width - 2, this.Height - 2);
            e.Graphics.DrawRectangle(pen, rect);
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        private void LblFeedback_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var feedbackDialog = new FeedbackDialog())
            {
                feedbackDialog.ShowDialog();
            }
        }
    }
}
