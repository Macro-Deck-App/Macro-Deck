using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.ActionButton
{
    public class ButtonLabel 
    {
        public event EventHandler LabelBase64Changed;

        private string _labelBase64 = "";
        public string LabelBase64
        {
            get { return _labelBase64; }
            set
            {
                _labelBase64 = value;
                if (LabelBase64Changed != null)
                {
                    LabelBase64Changed(this, EventArgs.Empty);
                }
            }
        }

        public string LabelText = "";

        public ButtonLabelPosition LabelPosition = ButtonLabelPosition.BOTTOM;

        public Color LabelColor = Color.White;

        public float Size = 6;

        public string FontFamily = "Impact";
    }


    public enum ButtonLabelPosition
    {
        TOP,
        CENTER,
        BOTTOM,
    }
}
