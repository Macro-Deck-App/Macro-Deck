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
            (new DropShadow()).ApplyShadows(this);
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
        

        private void Btn_close_Click(object sender, EventArgs e)
        {
            this.Close();
        }


        private void DialogForm_Load(object sender, EventArgs e)
        {
            this.btnClose.Location = new Point(this.Width - this.btnClose.Width - 2, 2);
            CenterToScreen();
            CenterToParent();
        }

        private void DialogForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (this.PointToClient(Cursor.Position).Y <= 25)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

    }
}
