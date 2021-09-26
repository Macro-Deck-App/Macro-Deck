using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class CreateProfileDialog : DialogForm
    {

        public MacroDeckProfile Profile;
        public CreateProfileDialog(MacroDeckProfile macroDeckProfile = null)
        {
            this.Profile = macroDeckProfile;
            InitializeComponent();
            this.lblName.Text = Language.LanguageManager.Strings.Name;
            this.btnOk.Text = Language.LanguageManager.Strings.Ok;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (profileName.Text.Length < 1) return;

            if (this.Profile == null)
            {
                foreach (MacroDeckProfile profile in MacroDeck.ProfileManager.Profiles)
                {
                    if (profile.DisplayName.Equals(profileName.Text))
                    {
                        using (var msgBox = new CustomControls.MessageBox())
                        {
                            msgBox.ShowDialog(Language.LanguageManager.Strings.CantCreateProfile, String.Format(Language.LanguageManager.Strings.ProfileCalledXAlreadyExists, this.profileName.Text), MessageBoxButtons.OK);
                        }
                        return;
                    }
                }


                this.Profile = MacroDeck.ProfileManager.CreateProfile(profileName.Text);
            } else
            {
                this.Profile.DisplayName = profileName.Text;
                MacroDeck.ProfileManager.Save();
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CreateProfileDialog_Load(object sender, EventArgs e)
        {
            if (this.Profile != null)
            {
                this.profileName.Text = this.Profile.DisplayName;
            }
        }
    }
}
