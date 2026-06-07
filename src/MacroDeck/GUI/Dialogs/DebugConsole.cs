using System.Diagnostics;
using System.IO;
using Serilog;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.StartupConfig;
using Form = SuchByte.MacroDeck.GUI.CustomControls.Form;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class DebugConsole : Form
{
    private static readonly ILogger logger = Log.ForContext(typeof(DebugConsole));

    /// <summary>
    /// The currently open debug console, or null. Used by <see cref="Logging.DebugConsoleSink"/>
    /// to forward log events to the window.
    /// </summary>
    public static DebugConsole? Current { get; private set; }

    /// <summary>
    /// Opens the debug console on its own dedicated UI thread so it is independent of, and
    /// responsive regardless of, the main application's startup work.
    /// </summary>
    public static void Launch()
    {
        var thread = new Thread(() =>
        {
            Current = new DebugConsole();
            Application.Run(Current);
        })
        {
            Name = "DebugConsole",
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

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
        // Closing the debug console only closes the viewer; Macro Deck keeps running.
        // Use the "Exit Macro Deck" button or the tray icon to quit. The dedicated UI
        // thread ends cleanly once its message loop returns.
        Current = null;
    }

    public void AppendText(string text, string sender, Color color)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            // Fire-and-forget onto the console's own UI thread; control access happens there.
            BeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed || logOutput.IsDisposed)
                    {
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(filter.Text))
                    {
                        var filters = filter.Text.Split(";")
                            .Where(x => !string.IsNullOrEmpty(x))
                            .ToArray();
                        if (filters.Length > 0 && Array.IndexOf(filters, sender) == -1)
                        {
                            return;
                        }
                    }

                    logOutput.SelectionStart = logOutput.TextLength;
                    logOutput.SelectionLength = 0;

                    logOutput.SelectionColor = color;
                    logOutput.AppendText(text);
                    logOutput.SelectionColor = logOutput.ForeColor;
                    logOutput.ScrollToCaret();
                }
                catch
                {
                    // The window is being disposed/closed – drop the message.
                }
            });
        }
        catch (Exception)
        {
            // The window is being disposed/closed – drop the message.
        }
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

    private void BtnOpenLogs_Click(object sender, EventArgs e)
    {
        // Open the most recently written log file directly; fall back to the logs directory.
        var target = ApplicationPaths.LogsDirectoryPath;
        try
        {
            var newest = new DirectoryInfo(ApplicationPaths.LogsDirectoryPath).GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            if (newest != null)
            {
                target = newest.FullName;
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Could not determine the newest log file");
        }

        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(target)
                {
                    UseShellExecute = true
                }
            };
            p.Start();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Could not open logs");
        }
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
            FileName = string.Format("debug_output_{0}.log", DateTime.Now.ToString("yy-MM-dd_HH-mm-ss"))
        };
        if (saveFileDialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                logOutput.SaveFile(saveFileDialog.FileName, RichTextBoxStreamType.PlainText);
                logger.Information("Successfully exported debug console output to: " + saveFileDialog.FileName);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error while exporting debug console output");
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
