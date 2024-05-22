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
    public event EventHandler FormWindowStateChanged;

    public Form()
    {
        InitializeComponent();
    }

    protected override void WndProc(ref Message m)
    {
        var originalState = this.WindowState;
        base.WndProc(ref m);
        if (this.WindowState == originalState)
        {
            return;
        }
        
        FormWindowStateChanged?.Invoke(this, EventArgs.Empty);
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

    private void HelpMenuExportLog_Click(object sender, EventArgs e)
    {
        using var saveFileDialog = new SaveFileDialog
        {
            AddExtension = true,
            Filter = "*.log|*.log",
            DefaultExt = ".log",
            FileName = new FileInfo(MacroDeckLogger.CurrentFilename).Name,
        };
        if (saveFileDialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.Copy(MacroDeckLogger.CurrentFilename, saveFileDialog.FileName, true);
            MacroDeckLogger.Info(GetType(), $"Exported latest log to {saveFileDialog.FileName}");
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.ExportLatestLog, string.Format(LanguageManager.Strings.LogSuccessfullyExportedToX, saveFileDialog.FileName), MessageBoxButtons.OK);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(GetType(), "Error while exporting latest log: " + ex.Message + Environment.NewLine + ex.StackTrace);
        }
    }
}