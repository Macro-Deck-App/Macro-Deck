using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Variables.Plugin.ViewModels;

namespace SuchByte.MacroDeck.Variables.Plugin.Views
{
    public partial class SaveVariableToFileActionConfigView : ActionConfigControl
    {
        private readonly SaveVariableToFileActionConfigViewModel _viewModel;


        public SaveVariableToFileActionConfigView(PluginAction pluginAction)
        {
            InitializeComponent();
            lblVariable.Text = LanguageManager.Strings.Variable;
            lblPath.Text = LanguageManager.Strings.Path;

            _viewModel = new SaveVariableToFileActionConfigViewModel(pluginAction);
        }

        private void SaveVariableToFileActionConfigView_Load(object sender, EventArgs e)
        {
            foreach (var variable in VariableManager.ListVariables)
            {
                this.variable.Items.Add(variable.Name);
            }

            this.variable.Text = _viewModel.Variable;
            path.Text = _viewModel.FilePath;
        }

        public override bool OnActionSave()
        {
            _viewModel.Variable = variable.Text;
            _viewModel.FilePath = path.Text;
            return _viewModel.SaveConfig();
        }

        private void BtnChoosePath_Click(object sender, EventArgs e)
        {
            using (var saveFileDialog = new SaveFileDialog
                   {
                AddExtension = true,
                DefaultExt = ".txt",
                Filter = "Text file (*.txt)|*.txt|Any (*.*)|*.*",
            })
            {
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    path.Text = saveFileDialog.FileName;
                }
            }
        }
    }
}
