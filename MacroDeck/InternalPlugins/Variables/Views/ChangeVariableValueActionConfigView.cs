using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.InternalPlugins.Variables.Enums;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;

namespace SuchByte.MacroDeck.Variables.Plugin.GUI;

public partial class ChangeVariableValueActionConfigView : ActionConfigControl
{
    private readonly ChangeVariableValueActionConfigViewModel _viewModel;

    public ChangeVariableValueActionConfigView(PluginAction pluginAction)
    {
        _viewModel = new ChangeVariableValueActionConfigViewModel(pluginAction);
        InitializeComponent();

        radioCountUp.Text = LanguageManager.Strings.CountUp;
        radioCountDown.Text = LanguageManager.Strings.CountDown;
        radioSet.Text = LanguageManager.Strings.Set;
        radioToggle.Text = LanguageManager.Strings.Toggle;
        lblVariable.Text = LanguageManager.Strings.Variable;
        lblOnlyUserCreatedVariablesVisible.Text = LanguageManager.Strings.OnlyUserCreatedVariablesVisible;

        radioCountUp.Visible = false;
        radioCountDown.Visible = false;
        radioSet.Visible = false;
        radioToggle.Visible = false;
        value.Visible = false;
        btnTemplateEditor.Visible = false;

        variables.SelectedIndexChanged += Variables_SelectedIndexChanged;
    }


    private void Variables_SelectedIndexChanged(object sender, EventArgs e)
    {
        try
        {
            var variable = VariableManager.ListVariables.Where(v => v.Name == variables.Text).FirstOrDefault();
            if (variable != null)
            {
                switch (variable.Type)
                {
                    case nameof(VariableType.String):
                        radioCountUp.Visible = false;
                        radioCountDown.Visible = false;
                        radioSet.Visible = true;
                        radioToggle.Visible = false;
                        radioSet.Checked = true;
                        btnTemplateEditor.Visible = true;
                        break;
                    case nameof(VariableType.Bool):
                        radioCountUp.Visible = false;
                        radioCountDown.Visible = false;
                        radioSet.Visible = true;
                        radioToggle.Visible = true;
                        radioSet.Checked = true;
                        btnTemplateEditor.Visible = true;
                        break;
                    case nameof(VariableType.Integer):
                    case nameof(VariableType.Float):
                        radioCountUp.Visible = true;
                        radioCountDown.Visible = true;
                        radioSet.Visible = true;
                        btnTemplateEditor.Visible = true;
                        radioToggle.Visible = false;
                        break;
                }

                if (value != null)
                {
                    value.SetAutoCompleteMode(AutoCompleteMode.Suggest);
                    value.SetAutoCompleteSource(AutoCompleteSource.CustomSource);
                    var suggestions = new AutoCompleteStringCollection();
                    suggestions.AddRange(variable.Suggestions);
                    value.SetAutoCompleteCustomSource(suggestions);
                }
                   

            }
        }
        catch { }
    }

    private void MethodChanged(object sender, EventArgs e)
    {
        value.Visible = radioSet.Checked;
    }

    public override bool OnActionSave()
    {
        _viewModel.Variable = variables.Text;

        if (radioCountUp.Checked)
        {
            _viewModel.Method = ChangeVariableMethod.countUp;
        }
        else if (radioCountDown.Checked)
        {
            _viewModel.Method = ChangeVariableMethod.countDown;
        }
        else if (radioSet.Checked)
        {
            _viewModel.Method = ChangeVariableMethod.set;
        }
        else if (radioToggle.Checked)
        {
            _viewModel.Method = ChangeVariableMethod.toggle;
        }

        _viewModel.Value = radioSet.Checked ? value.Text : string.Empty;

        return _viewModel.SaveConfig();
    }

    private void LoadVariables()
    {
        variables.Items.Clear();
        foreach (var variable in VariableManager.ListVariables.Where(v => v.Creator.Equals("User")))
        {
            variables.Items.Add(variable.Name);
        }
    }

        

    private void ChangeVariableValueConfigurator_Load(object sender, EventArgs e)
    {
        LoadVariables();

        switch (_viewModel.Method)
        {
            case ChangeVariableMethod.countUp:
                radioCountUp.Checked = true;
                break;
            case ChangeVariableMethod.countDown:
                radioCountDown.Checked = true;
                break;
            case ChangeVariableMethod.set:
                radioSet.Checked = true;
                break;
            case ChangeVariableMethod.toggle:
                radioToggle.Checked = true;
                break;
        }

        variables.Text = _viewModel.Variable;
        value.Text = _viewModel.Value;
        value.Visible = radioSet.Checked;

    }

    private void BtnTemplateEditor_Click(object sender, EventArgs e)
    {
        using (var templateEditor = new TemplateEditor(value.Text))
        {
            if (templateEditor.ShowDialog() == DialogResult.OK)
            {
                value.Text = templateEditor.Template;
            }
        }
    }
}