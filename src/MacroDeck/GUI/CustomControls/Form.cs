using System.IO;
using Serilog;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class Form : System.Windows.Forms.Form
{
    // Base class of all dialogs/forms: resolve the contextual logger from the runtime type
    // so log entries carry the concrete derived form name as their SourceContext.
    private readonly ILogger logger;

    public event EventHandler FormWindowStateChanged;

    public Form()
    {
        logger = Log.ForContext(GetType());
        InitializeComponent();
    }

    protected override void WndProc(ref Message m)
    {
        var originalState = WindowState;
        base.WndProc(ref m);
        if (WindowState == originalState)
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
            FileName = new FileInfo(MacroDeckLogger.CurrentFilename).Name
        };
        if (saveFileDialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.Copy(MacroDeckLogger.CurrentFilename, saveFileDialog.FileName, true);
            logger.Information($"Exported latest log to {saveFileDialog.FileName}");
            using var msgBox = new MessageBox();
            msgBox.ShowDialog(LanguageManager.Strings.ExportLatestLog,
                string.Format(LanguageManager.Strings.LogSuccessfullyExportedToX, saveFileDialog.FileName),
                MessageBoxButtons.OK);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error while exporting latest log");
        }
    }
}
