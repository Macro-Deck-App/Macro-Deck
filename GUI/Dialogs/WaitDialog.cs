using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using Form = System.Windows.Forms.Form;

namespace SuchByte.MacroDeck.GUI.Dialogs
{
    internal partial class WaitDialog : DialogForm
    {
        public WaitDialog()
        {
            InitializeComponent();
            SetCloseIconVisible(false);
            lblPleaseWait.Text = LanguageManager.Strings.PleaseWait;
        }
    }

    public static class SpinnerDialog
    {
        private static WaitDialog waitDialog = new();

        public static void SetVisisble(bool visible, Form owner)
        {
            owner?.Invoke(() =>
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
            });

        }


    }
}
