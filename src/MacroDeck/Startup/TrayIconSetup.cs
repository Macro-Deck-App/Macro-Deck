using SuchByte.MacroDeck.Language;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.Startup;

public static class TrayIconSetup
{
    public static NotifyIcon SetupTrayIcon(this NotifyIcon trayIcon, 
        ContextMenuStrip trayIconContextMenu,
        Action showAction,
        Action restartAction,
        Action exitAction)
    {
        trayIcon.Visible = true; 
        trayIcon.MouseDown += (obj, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                showAction?.Invoke();
            }
        };

        var showItem = new ToolStripMenuItem
        {
            Text = LanguageManager.Strings.Show,
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        showItem.Click += (obj, e) =>
        {
            showAction?.Invoke();
        };

        var restartItem = new ToolStripMenuItem
        {
            Text = LanguageManager.Strings.Restart,
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        restartItem.Click += (obj, e) =>
        {
            restartAction?.Invoke();
        };

        var exitItem = new ToolStripMenuItem
        {
            Text = LanguageManager.Strings.Exit,
            Font = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        exitItem.Click += (obj, e) =>
        {
            exitAction?.Invoke();
        };

        trayIconContextMenu.Items.Add(showItem);
        trayIconContextMenu.Items.Add(restartItem);
        trayIconContextMenu.Items.Add(exitItem);
        return trayIcon;
    }
}