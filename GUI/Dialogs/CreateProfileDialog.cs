using System;
using System.Windows.Forms;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Profiles;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    public partial class CreateProfileDialog : DialogForm
    {

        public MacroDeckProfile Profile;
        public CreateProfileDialog(MacroDeckProfile macroDeckProfile = null)
        {
            Profile = macroDeckProfile;
            InitializeComponent();
            lblName.Text = LanguageManager.Strings.Name;
            btnOk.Text = LanguageManager.Strings.Ok;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (profileName.Text.Length < 1) return;

            if (Profile == null)
            {
                foreach (var profile in ProfileManager.Profiles)
                {
                    if (profile.DisplayName.Equals(profileName.Text))
                    {
                        using (var msgBox = new MessageBox())
                        {
                            msgBox.ShowDialog(LanguageManager.Strings.CantCreateProfile, string.Format(LanguageManager.Strings.ProfileCalledXAlreadyExists, profileName.Text), MessageBoxButtons.OK);
                        }
                        return;
                    }
                }


                Profile = ProfileManager.CreateProfile(profileName.Text);
            } else
            {
                Profile.DisplayName = profileName.Text;
                ProfileManager.Save();
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CreateProfileDialog_Load(object sender, EventArgs e)
        {
            if (Profile != null)
            {
                profileName.Text = Profile.DisplayName;
            }
        }
    }
}
