using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class MessageBox : DialogForm
    {
        public MessageBox()
        {
            InitializeComponent();
            this.SetCloseIconVisible(false);
            if (this.Parent == null)
            {
                this.StartPosition = FormStartPosition.CenterScreen;
            }
        }

        public DialogResult ShowDialog(String title, String message, MessageBoxButtons messageBoxButtons)
        {
            this.lblTitle.Text = title;
            this.lblMessage.Text = message;
            buttonMessageBoxPanel.Controls.Clear();
            switch (messageBoxButtons)
            {
                default:
                case MessageBoxButtons.OK:
                    ButtonPrimary btnOK = new ButtonPrimary();
                    btnOK.Text = Language.LanguageManager.Strings.Ok;
                    btnOK.AutoSize = true;
                    btnOK.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnOK.MinimumSize = new Size(75, 0);
                    btnOK.Select();
                    btnOK.Click += (sender, args) =>
                    {
                        this.DialogResult = DialogResult.OK;
                        Close();
                    };
                    buttonMessageBoxPanel.Controls.Add(btnOK);
                    break;
                case MessageBoxButtons.YesNo:
                    ButtonPrimary btnNo = new ButtonPrimary();
                    btnNo.Text = Language.LanguageManager.Strings.No;
                    btnNo.AutoSize = true;
                    btnNo.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnNo.MinimumSize = new Size(75, 0);
                    btnNo.Click += (sender, args) =>
                    {
                        this.DialogResult = DialogResult.No;
                        Close();
                    };
                    ButtonPrimary btnYes = new ButtonPrimary();
                    btnYes.Text = Language.LanguageManager.Strings.Yes;
                    btnYes.AutoSize = true;
                    btnYes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    btnYes.MinimumSize = new Size(75, 0);
                    btnYes.Select();
                    btnYes.Click += (sender, args) =>
                    {
                        this.DialogResult = DialogResult.Yes;
                        Close();
                    };
                    
                    buttonMessageBoxPanel.Controls.Add(btnNo);
                    buttonMessageBoxPanel.Controls.Add(btnYes);
                    break;
            }
            return this.ShowDialog();
        }
    }
}
