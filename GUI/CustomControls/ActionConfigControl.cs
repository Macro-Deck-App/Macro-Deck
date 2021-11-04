using SuchByte.MacroDeck.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{

    public partial class ActionConfigControl : UserControl
    {
        
        /// <summary>
        /// Gets called when the user clicks the "Ok" button in the ActionConfigurator.
        /// Replaces the ActionSave event.
        /// </summary>
        /// <returns>return true = the user comnfigured the action; false = the user didn't configured</returns>
        public virtual bool OnActionSave()
        {
            return true;
        }

        public ActionConfigControl()
        {
            InitializeComponent();
        }
    }
}
