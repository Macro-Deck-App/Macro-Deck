using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class DebugConsole : GUI.CustomControls.Form
    {
        public DebugConsole()
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.DebugConsoleFilters))
            {
                this.filter.Text = Properties.Settings.Default.DebugConsoleFilters;
            }
            foreach (LogLevel logLevels in (LogLevel[])Enum.GetValues(typeof(LogLevel)))
            {
                this.logLevel.Items.Add(logLevels.ToString());
            }
            this.logLevel.Text = MacroDeckLogger.LogLevel.ToString();
            this.FormClosed += DebugConsole_FormClosed;
            this.filtersList.ItemClicked += FiltersList_ItemClicked;
            this.filter.TextChanged += Filter_TextChanged;
        }

        private void Filter_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.DebugConsoleFilters = this.filter.Text;
            Properties.Settings.Default.Save();
        }

        private void DebugConsole_FormClosed(object sender, FormClosedEventArgs e)
        {
            Environment.Exit(0);
        }

        public void AppendText(string text, string sender, Color color)
        {
            if (!string.IsNullOrWhiteSpace(this.filter.Text))
            {
                string[] filters = this.filter.Text.Split(";");
                filters = filters.Where(x => !string.IsNullOrEmpty(x)).ToArray(); // Remove empty strings
                if (Array.IndexOf(filters, sender) == -1) return;
            }
            this.Invoke(new Action(() =>
            {

                this.logOutput.SelectionStart = this.logOutput.TextLength;
                this.logOutput.SelectionLength = 0;

                this.logOutput.SelectionColor = color;
                this.logOutput.AppendText(text);
                this.logOutput.SelectionColor = this.logOutput.ForeColor;
                this.logOutput.ScrollToCaret();
            }));
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            this.logOutput.Text = string.Empty;
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
                StartInfo = new ProcessStartInfo(MacroDeck.UserDirectoryPath)
                {
                    UseShellExecute = true
                }
            };
            p.Start();
        }

        private void LogLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Enum.TryParse(typeof(LogLevel), this.logLevel.SelectedItem.ToString(), true, out object logLevel))
            {
                MacroDeckLogger.LogLevel = (LogLevel)logLevel;
            }
        }

        private void BtnExportOutput_Click(object sender, EventArgs e)
        {
            using (var saveFileDialog = new SaveFileDialog
            {
                AddExtension = true,
                Filter = ".log|*.log",
                FileName = String.Format("debug_output_{0}.log", DateTime.Now.ToString("yy-MM-dd_HH-mm-ss")),
            })
            {
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        this.logOutput.SaveFile(saveFileDialog.FileName, RichTextBoxStreamType.PlainText);
                        MacroDeckLogger.Info("Successfully exported debug console output to: " + saveFileDialog.FileName);
                    } catch (Exception ex)
                    {
                        MacroDeckLogger.Error("Error while exporting debug console output: " + ex.Message + Environment.NewLine + ex.StackTrace);
                    }
                }
            }
        }

        private void BtnAddFilter_Click(object sender, EventArgs e)
        {
            this.filtersList.Items.Clear();
            this.filtersList.Items.Add("Macro Deck").ForeColor = Color.White;
            foreach (var plugin in PluginManager.Plugins.Values)
            {
                this.filtersList.Items.Add(plugin.Name).ForeColor = Color.White;
            }
            this.filtersList.Show(Cursor.Position);
        }
        private void FiltersList_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            this.filter.Text += ";" + e.ClickedItem.Text;
        }

        private void DebugConsole_Load(object sender, EventArgs e)
        {
        }

        private void btnRemoveFilters_Click(object sender, EventArgs e)
        {
            this.filter.Text = string.Empty;
        }

        private void btnTestNotification_Click(object sender, EventArgs e)
        {
            NotificationManager.SystemNotification("Test", $"Test notification sent at {DateTime.Now.ToString()}", true);
        }
    }
}
