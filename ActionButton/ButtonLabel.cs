using Newtonsoft.Json;
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
        private string _labelHex128_64Base64 = "";

        [JsonIgnore]
        public string LabelBase64
        {
            get { return _labelBase64; }
            set
            {
                this._labelBase64 = value;
                this.UpdateLabelHex128_64();
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

        [JsonIgnore]
        public string LabelHex128_64Base64
        {
            get
            {
                if (string.IsNullOrWhiteSpace(this._labelHex128_64Base64))
                {
                    this.UpdateLabelHex128_64();
                }
                return this._labelHex128_64Base64;
            }
        }

        private void UpdateLabelHex128_64()
        {
            ContentAlignment contentAlignment = ContentAlignment.MiddleCenter;
            switch (LabelPosition)
            {
                case ButtonLabelPosition.TOP:
                    contentAlignment = ContentAlignment.TopCenter;
                    break;
                case ButtonLabelPosition.BOTTOM:
                    contentAlignment = ContentAlignment.BottomCenter;
                    break;
            }
            this._labelHex128_64Base64 = Utils.Base64.GetBase64ByteArray((Bitmap)Utils.Base64.GetImageFromBase64(this._labelBase64), new Size(128, 64), contentAlignment);
        }
    }


    public enum ButtonLabelPosition
    {
        TOP,
        CENTER,
        BOTTOM,
    }
}
