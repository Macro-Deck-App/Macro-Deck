using System.Windows.Forms;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;

namespace SuchByte.MacroDeck.GUI.CustomControls;

public partial class ActionConfiguratorPluginItem : RoundedUserControl
{
    private bool selected;

    public MacroDeckPlugin Plugin { get; set; }

    public bool Selected 
    { 
        get => selected;
        set
        {
            selected = value;
            chevron.BackgroundImage = selected ? Resources.Chevron_Down : Resources.Chevron_Right;
        }
    }

    public ActionConfiguratorPluginItem(MacroDeckPlugin macroDeckPlugin)
    {
        Plugin ??= macroDeckPlugin;
        InitializeComponent();
        DoubleBuffered = true;

        pluginIcon.MouseClick += Control_MouseClick;
        pluginName.MouseClick += Control_MouseClick;
        lblCountActions.MouseClick += Control_MouseClick;
        chevron.MouseClick += Control_MouseClick;
    }


    private void Control_MouseClick(object sender, MouseEventArgs e)
    {
        OnMouseClick(e);
    }

    private void ActionConfiguratorPluginItem_Load(object sender, EventArgs e)
    {
        if (Plugin == null) return;
        pluginIcon.BackgroundImage = Plugin.PluginIcon ?? Resources.Icon;
        pluginName.Text = Plugin.Name;
        lblCountActions.Text = string.Format((Plugin.Actions.Count == 1 ? LanguageManager.Strings.XAction : LanguageManager.Strings.XActions), Plugin.Actions.Count);
    }


}