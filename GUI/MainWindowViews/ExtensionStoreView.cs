using SuchByte.MacroDeck.GUI.ExtensionStore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.MainWindowViews
{
    public partial class ExtensionStoreView : UserControl
    {

        private ExtensionStorePluginsView extensionStorePluginsView;

        public ExtensionStoreView()
        {
            InitializeComponent();
        }

        private void ExtensionStoreView_Load(object sender, EventArgs e)
        {
            if (extensionStorePluginsView != null)
            {
                extensionStorePluginsView.Dispose();
                this.tabPlugins.Controls.Remove(extensionStorePluginsView);
            }
            extensionStorePluginsView = new ExtensionStorePluginsView();
            this.tabPlugins.Controls.Add(extensionStorePluginsView);

        }
    }
}
