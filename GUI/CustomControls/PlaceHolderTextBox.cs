using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    [Obsolete("Use RoundedTextBox instad")]
    public partial class PlaceHolderTextBox : TextBox
    {

        bool isPlaceHolder = true;
        string _placeHolderText;
        public string PlaceHolderText
        {
            get => _placeHolderText;
            set
            {
                _placeHolderText = value;
                SetPlaceholder();
            }
        }

        public new string Text
        {
            get => (isPlaceHolder || base.Text == _placeHolderText) ? string.Empty : base.Text;
            set {
                if (value != _placeHolderText && value.Length > 0 && value != "")
                {
                    RemovePlaceHolder();
                } else if (value.Length == 0 || value != "")
                {
                    SetPlaceholder();
                    base.Text = _placeHolderText;
                }
                base.Text = value; 
            }
        }



        //when the control loses focus, the placeholder is shown
        private void SetPlaceholder()
        {
            if (string.IsNullOrEmpty(base.Text))
            {
                base.Text = PlaceHolderText;
                ForeColor = Color.Gray;
                Font = new Font(Font, FontStyle.Italic);
                isPlaceHolder = true;
            }
        }

        //when the control is focused, the placeholder is removed
        private void RemovePlaceHolder()
        {
            if (isPlaceHolder)
            {
                base.Text = "";
                ForeColor = Color.White;
                Font = new Font(Font, FontStyle.Regular);
                isPlaceHolder = false;
            }
        }
        public PlaceHolderTextBox()
        {
            SetStyle(ControlStyles.Selectable, false);
            GotFocus += removePlaceHolder;
            LostFocus += setPlaceholder;
            RemovePlaceHolder();
        }

        private void setPlaceholder(object sender, EventArgs e)
        {
            SetPlaceholder();
        }

        private void removePlaceHolder(object sender, EventArgs e)
        {
            RemovePlaceHolder();
        }
    }
}
