using SuchByte.MacroDeck.Plugins;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class PluginDetails : UserControl
    {

        public event EventHandler OnBtnDeleteClick;

        public IMacroDeckPlugin Plugin { get; }

        public string PluginName { get; }

        public PluginDetails(IMacroDeckPlugin plugin)
        {
            this.Plugin = plugin;
            this.PluginName = plugin.Name;
            InitializeComponent();
            this.btnConfigure.Text = Language.LanguageManager.Strings.Configure;
            this.btnDelete.Text = Language.LanguageManager.Strings.Uninstall;
            this.lblNotLoaded.Text = Language.LanguageManager.Strings.NotLoaded;


            this.lblPluginName.Text = plugin.Name;
            this.lblVersion.Text = plugin.Version;
            this.lblAuthor.Text = plugin.Author;
            this.lblDescription.Text = plugin.Description;
            if (plugin.GetType().Equals(typeof(DisabledPlugin)))
            {
                this.lblPluginName.ForeColor = Color.IndianRed;
                this.lblNotLoaded.Visible = true;
                this.btnDelete.Enabled = false;
            } else
            {
                this.lblPluginName.ForeColor = Color.LightGray;
                this.lblNotLoaded.Visible = false;
                this.iconBox.BackgroundImage = plugin.Icon != null ? plugin.Icon : Properties.Resources.Icon;

                if (plugin.GetType() == typeof(ActionButton.ActionButtonPlugin) || plugin.GetType() == typeof(Variables.Plugin.VariablesPlugin))
                {
                    this.btnDelete.Enabled = false;
                }

                this.btnConfigure.Visible = plugin.CanConfigure;
            }

            
        }

       /* public PluginDetails(AssemblyName plugin)
        {
            this.PluginName = plugin.Name;
            InitializeComponent();
            this.btnDelete.Text = Language.LanguageManager.Strings.Uninstall;
            this.lblNotLoaded.Text = Language.LanguageManager.Strings.NotLoaded;

            
            this.lblAuthor.Text = "-";
            this.lblDescription.Text = Language.LanguageManager.Strings.PluginCouldNotBeLoaded;
            this.iconBox.BackgroundImage = Properties.Resources.Macro_Deck_error;
            this.lblNotLoaded.Visible = true;
        }*/

        private void PluginDetails_Load(object sender, EventArgs e)
        {

        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            if (this.OnBtnDeleteClick != null)
            {
                this.OnBtnDeleteClick(this, EventArgs.Empty);
            }
        }

        private void btnConfigure_Click(object sender, EventArgs e)
        {
            this.Plugin.OpenConfigurator();
        }

    }
}
