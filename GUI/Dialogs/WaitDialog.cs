using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    internal partial class WaitDialog : DialogForm
    {
        public WaitDialog()
        {
            InitializeComponent();
            this.SetCloseIconVisible(false);
            this.lblPleaseWait.Text = LanguageManager.Strings.PleaseWait;
        }
    }

    public static class SpinnerDialog
    {
        private static WaitDialog waitDialog = new WaitDialog();

        public static void SetVisisble(bool visible, System.Windows.Forms.Form owner)
        {
            if (owner != null)
            {
                owner.Invoke(new Action(() =>
                {
                    if (visible)
                    {
                        if (waitDialog.Visible) return;
                        waitDialog.ShowDialog();
                    }
                    else
                    {
                        waitDialog.Hide();
                    }
                }));
            }
           
        }


    }
}
