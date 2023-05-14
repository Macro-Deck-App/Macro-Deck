using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.Startup;
using Form = SuchByte.MacroDeck.GUI.CustomControls.Form;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class DebugConsole : Form
{
    public DebugConsole()
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(Settings.Default.DebugConsoleFilters))
        {
            filter.Text = Settings.Default.DebugConsoleFilters;
        }
        foreach (var logLevels in (LogLevel[])Enum.GetValues(typeof(LogLevel)))
        {
            logLevel.Items.Add(logLevels.ToString());
        }
        logLevel.Text = MacroDeckLogger.LogLevel.ToString();
        FormClosed += DebugConsole_FormClosed;
        filtersList.ItemClicked += FiltersList_ItemClicked;
        filter.TextChanged += Filter_TextChanged;
    }

    private void Filter_TextChanged(object sender, EventArgs e)
    {
        Settings.Default.DebugConsoleFilters = filter.Text;
        Settings.Default.Save();
    }

    private void DebugConsole_FormClosed(object sender, FormClosedEventArgs e)
    {
        Environment.Exit(0);
    }

    public void AppendText(string text, string sender, Color color)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var filters = filter.Text.Split(";");
            filters = filters.Where(x => !string.IsNullOrEmpty(x)).ToArray(); // Remove empty strings
            if (Array.IndexOf(filters, sender) == -1) return;
        }
        Invoke(() =>
        {

            logOutput.SelectionStart = logOutput.TextLength;
            logOutput.SelectionLength = 0;

            logOutput.SelectionColor = color;
            logOutput.AppendText(text);
            logOutput.SelectionColor = logOutput.ForeColor;
            logOutput.ScrollToCaret();
        });
    }

    private void BtnClear_Click(object sender, EventArgs e)
    {
        logOutput.Text = string.Empty;
    }

    private void BtnRestartMacroDeck_Click(object sender, EventArgs e)
    {
        MacroDeck.RestartMacroDeck(string.Join(" ", MacroDeck.StartParameters));
    }

    private void BtnExit_Click(object sender, EventArgs e)
    {
        Environment.Exit(0);
    }

    private void BtnOpenUser_Click(object sender, EventArgs e)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(ApplicationPaths.UserDirectoryPath)
            {
                UseShellExecute = true
            }
        };
        p.Start();
    }

    private void LogLevel_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (Enum.TryParse(typeof(LogLevel), this.logLevel.SelectedItem.ToString(), true, out var logLevel))
        {
            MacroDeckLogger.LogLevel = (LogLevel)logLevel;
        }
    }

    private void BtnExportOutput_Click(object sender, EventArgs e)
    {
        using var saveFileDialog = new SaveFileDialog
        {
            AddExtension = true,
            Filter = ".log|*.log",
            FileName = string.Format("debug_output_{0}.log", DateTime.Now.ToString("yy-MM-dd_HH-mm-ss")),
        };
        if (saveFileDialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                logOutput.SaveFile(saveFileDialog.FileName, RichTextBoxStreamType.PlainText);
                MacroDeckLogger.Info("Successfully exported debug console output to: " + saveFileDialog.FileName);
            } catch (Exception ex)
            {
                MacroDeckLogger.Error("Error while exporting debug console output: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
    }

    private void BtnAddFilter_Click(object sender, EventArgs e)
    {
        filtersList.Items.Clear();
        filtersList.Items.Add("Macro Deck").ForeColor = Color.White;
        foreach (var plugin in PluginManager.Plugins.Values)
        {
            filtersList.Items.Add(plugin.Name).ForeColor = Color.White;
        }
        filtersList.Show(Cursor.Position);
    }
    private void FiltersList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
    {
        filter.Text += ";" + e.ClickedItem.Text;
    }

    private void DebugConsole_Load(object sender, EventArgs e)
    {
    }

    private void btnRemoveFilters_Click(object sender, EventArgs e)
    {
        filter.Text = string.Empty;
    }

    private void btnTestNotification_Click(object sender, EventArgs e)
    {
        NotificationManager.SystemNotification("Test", $"Test notification sent at {DateTime.Now.ToString()}", true);
    }
}