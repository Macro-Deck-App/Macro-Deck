using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class LicenseItem : UserControl
    {

        string _name, _license, _author, _repository;

        private void LicenseItem_Load(object sender, EventArgs e)
        {
            this.lblName.Text = this._name;
            this.lblAuthor.Text = this._author;

            this.MouseEnter += this.MouseEnterEvent;
            this.MouseLeave += this.MouseLeaveEvent;
        }

        public LicenseItem(string name, string license, string author, string repository)
        {
            this._name = name;
            this._license = license;
            this._author = author;
            this._repository = repository;
            InitializeComponent();
        }

        private void LblLicense_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = this._license,
                UseShellExecute = true
            });
        }

        private void lblProjectPage_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = this._repository,
                UseShellExecute = true
            });
        }

        private void MouseEnterEvent(object sender, EventArgs e)
        {
            this.BackColor = Color.FromArgb(65, 65, 65);
        }

        private void MouseLeaveEvent(object sender, EventArgs e)
        {
            if (!this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)))
            {
                this.BackColor = Color.FromArgb(45, 45, 45);
            }

        }
    }
}
