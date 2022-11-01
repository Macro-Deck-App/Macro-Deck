using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class LicenseItem : UserControl
{

    string _name, _license, _author, _repository;

    private void LicenseItem_Load(object sender, EventArgs e)
    {
        lblName.Text = _name;
        lblAuthor.Text = _author;

        MouseEnter += MouseEnterEvent;
        MouseLeave += MouseLeaveEvent;
    }

    public LicenseItem(string name, string license, string author, string repository)
    {
        _name = name;
        _license = license;
        _author = author;
        _repository = repository;
        InitializeComponent();
    }

    private void LblLicense_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _license,
            UseShellExecute = true
        });
    }

    private void lblProjectPage_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _repository,
            UseShellExecute = true
        });
    }

    private void MouseEnterEvent(object sender, EventArgs e)
    {
        BackColor = Color.FromArgb(65, 65, 65);
    }

    private void MouseLeaveEvent(object sender, EventArgs e)
    {
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            BackColor = Color.FromArgb(45, 45, 45);
        }

    }
}