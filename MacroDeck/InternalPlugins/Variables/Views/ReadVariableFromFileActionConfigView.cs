using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;

namespace SuchByte.MacroDeck.Variables.Plugin.Views;

public partial class ReadVariableFromFileActionConfigView : ActionConfigControl
{
    private readonly ReadVariableFromFileActionConfigViewModel _viewModel;


    public ReadVariableFromFileActionConfigView(PluginAction pluginAction)
    {
        InitializeComponent();
        lblVariable.Text = LanguageManager.Strings.Variable;
        lblPath.Text = LanguageManager.Strings.Path;

        _viewModel = new ReadVariableFromFileActionConfigViewModel(pluginAction);
    }

    private void ReadVariableFromFileActionConfigView_Load(object sender, EventArgs e)
    {
        LoadVariables();
        variable.Text = _viewModel.Variable;
        path.Text = _viewModel.FilePath;
    }

    private void LoadVariables()
    {
        this.variable.Items.Clear();
        foreach (var variable in VariableManager.ListVariables.Where(x => x.Creator == "User"))
        {
            this.variable.Items.Add(variable.Name);
        }
    }

    public override bool OnActionSave()
    {
        _viewModel.Variable = variable.Text;
        _viewModel.FilePath = path.Text;
        return _viewModel.SaveConfig();
    }

    private void BtnChoosePath_Click(object sender, EventArgs e)
    {
        using var openFileDialog = new OpenFileDialog
        {
            AddExtension = true,
            DefaultExt = ".txt",
            Filter = "Text file (*.txt)|*.txt|Any (*.*)|*.*",
        };
        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
            path.Text = openFileDialog.FileName;
        }
    }

    private void BtnCreateVariable_Click(object sender, EventArgs e)
    {
        using var variableDialog = new VariableDialog();
        if (variableDialog.ShowDialog() == DialogResult.OK)
        {
            VariableManager.InsertVariable(variableDialog.Variable);
            LoadVariables();
            variable.Text = variableDialog.Variable.Name;
        }
    }

}