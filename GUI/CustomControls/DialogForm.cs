using SuchByte.MacroDeck.GUI.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class DialogForm : System.Windows.Forms.Form
    {

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();

        public DialogForm()
        {
            InitializeComponent();
            this.ResizeEnd += OnResizeEnd;
        }

        private void OnResizeEnd(object sender, EventArgs e)
        {
            this.btnClose.Location = new Point(this.Width - this.btnClose.Width - 2, 2);
        }

        public void SetCloseIconVisible(bool visible)
        {
            this.btnClose.Visible = visible;
        }


        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        private void Btn_close_Click(object sender, EventArgs e)
        {
            this.Close();
        }


        private void DialogForm_Load(object sender, EventArgs e)
        {
            /*this.Opacity = 0;
            open_animation_timer.Start();*/
            this.btnClose.Location = new Point(this.Width - this.btnClose.Width - 2, 2);
        }

        private void open_animation_timer_Tick(object sender, EventArgs e)
        {
            if (this.Opacity < 1)
            {
                this.Opacity += 0.1;
            }
            if (this.Opacity >= 1)
            {
                open_animation_timer.Stop();
            }
        }

        
        private void DialogForm_Paint(object sender, PaintEventArgs e)
        {
            Pen pen = new Pen(Color.FromArgb(0, 123, 255), 1);
            Rectangle rect = new Rectangle(0, 0, this.Width - 2, this.Height - 2);
            e.Graphics.DrawRectangle(pen, rect);
        }

        private void BtnFeedback_Click(object sender, EventArgs e)
        {
            using (var feedbackDialog = new FeedbackDialog())
            {
                feedbackDialog.ShowDialog();
            }
        }

    }
}
