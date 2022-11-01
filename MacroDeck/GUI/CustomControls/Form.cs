using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class Form : System.Windows.Forms.Form
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;
    [DllImportAttribute("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImportAttribute("user32.dll")]
    public static extern bool ReleaseCapture();

    private const int cGrip = 16;      // Grip size


    public Form()
    {
        InitializeComponent();
        (new DropShadow()).ApplyShadows(this);
        btnHelp.Text = LanguageManager.Strings.Help;
        helpMenuDiscordSupport.Text = LanguageManager.Strings.DiscordSupport;
        helpMenuWiki.Text = LanguageManager.Strings.Wiki;
        helpMenuExportLog.Text = LanguageManager.Strings.ExportLatestLog;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84)
        {  // Trap WM_NCHITTEST
            var pos = new Point(m.LParam.ToInt32());
            pos = PointToClient(pos);
            if (pos.X >= ClientSize.Width - cGrip && pos.Y >= ClientSize.Height - cGrip)
            {
                m.Result = (IntPtr)17; // HTBOTTOMRIGHT
                return;
            }
        }
        base.WndProc(ref m);
    }

    private void BtnClose_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseEventArgs e)
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
    }

    private void BtnHelp_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        helpMenu.Show(Cursor.Position);
    }

    private void HelpMenuDiscordSupport_Click(object sender, EventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("https://discord.gg/yr7TRaXum8")
            {
                UseShellExecute = true,
            }
        };
        p.Start();
    }

    private void HelpMenuWiki_Click(object sender, EventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("https://github.com/SuchByte/Macro-Deck/wiki")
            {
                UseShellExecute = true,
            }
        };
        p.Start();
    }

    private void HelpMenuExportLog_Click(object sender, EventArgs e)
    {
        using (var saveFileDialog = new SaveFileDialog
               {
                   AddExtension = true,
                   Filter = "*.log|*.log",
                   DefaultExt = ".log",
                   FileName = new FileInfo(MacroDeckLogger.CurrentFilename).Name,
               })
        {
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    File.Copy(MacroDeckLogger.CurrentFilename, saveFileDialog.FileName, true);
                    MacroDeckLogger.Info(GetType(), $"Exported latest log to { saveFileDialog.FileName }");
                    using (var msgBox = new MessageBox())
                    {
                        msgBox.ShowDialog(LanguageManager.Strings.ExportLatestLog, string.Format(LanguageManager.Strings.LogSuccessfullyExportedToX, saveFileDialog.FileName), MessageBoxButtons.OK);
                    }
                } catch (Exception ex)
                {
                    MacroDeckLogger.Error(GetType(), "Error while exporting latest log: " + ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }
        }
    }

    private void btnDonate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("https://www.buymeacoffee.com/suchbyte")
            {
                UseShellExecute = true,
            }
        };
        p.Start();
    }
}