using System.Drawing;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Enums;
using SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.ViewModels;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.InternalPlugins.ActionButtonPlugin.Views;

public partial class ActionButtonSetBackgroundColorActionConfigView : ActionConfigControl
{
    private readonly ActionButtonSetBackgroundColorActionConfigViewModel _viewModel;

    public ActionButtonSetBackgroundColorActionConfigView(PluginAction pluginAction)
    {
        _viewModel = new ActionButtonSetBackgroundColorActionConfigViewModel(pluginAction);
        InitializeComponent();
        radioFixed.Text = LanguageManager.Strings.Fixed;
        radioRandom.Text = LanguageManager.Strings.Random;
    }

    private void ActionButtonSetBackgroundColorActionConfigView_Load(object sender, EventArgs e)
    {
        btnChangeColor.BackColor = _viewModel.Color;
        switch (_viewModel.Method)
        {
            case SetBackgroundColorMethod.Fixed:
                radioFixed.Checked = true;
                break;
            case SetBackgroundColorMethod.Random:
                radioRandom.Checked = true;
                break;
        }
    }

    public override bool OnActionSave()
    {
        _viewModel.Color = btnChangeColor.BackColor;
        if (radioFixed.Checked)
        {
            _viewModel.Method = SetBackgroundColorMethod.Fixed;
        } else if (radioRandom.Checked)
        {
            _viewModel.Method = SetBackgroundColorMethod.Random;
        }
        return _viewModel.SaveConfig();
    }

    private void BtnChangeColor_Click(object sender, EventArgs e)
    {
        using var colorDialog = new ColorDialog
        {
            Color = btnChangeColor.BackColor,
            FullOpen = true,
            CustomColors = new[] { ColorTranslator.ToOle(Color.FromArgb(35,35,35)) }
        };
        if (colorDialog.ShowDialog() == DialogResult.OK)
        {
            btnChangeColor.BackColor = colorDialog.Color;
        }
    }

    private void RadioRandom_CheckedChanged(object sender, EventArgs e)
    {
            
    }

    private void RadioFixed_CheckedChanged(object sender, EventArgs e)
    {
        btnChangeColor.Visible = radioFixed.Checked;
    }
}