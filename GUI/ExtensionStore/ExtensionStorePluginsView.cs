using SuchByte.MacroDeck.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.ExtensionStore
{
    public partial class ExtensionStorePluginsView : UserControl
    {
        public ExtensionStorePluginsView()
        {
            InitializeComponent();
        }

        private void ExtensionStorePluginsView_Load(object sender, EventArgs e)
        {
            MacroDeckLogger.Info(GetType(), "Loaded");
            for (int i = 0; i < 10; i++)
            {
                ExtensionStorePluginItemView extensionStorePluginItemView = new ExtensionStorePluginItemView();
                this.pluginsContainer.Controls.Add(extensionStorePluginItemView);
            }
        }
    }
}
