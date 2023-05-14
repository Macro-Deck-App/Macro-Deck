using System;
using System.Drawing;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.ActionButton;

public class ButtonLabel 
{
    public event EventHandler LabelBase64Changed;

    private string _labelBase64 = "";
    private string _labelHex128_64Base64 = "";

    public string LabelBase64
    {
        get => _labelBase64;
        set
        {
            _labelBase64 = value;
            UpdateLabelHex128_64();
            LabelBase64Changed?.Invoke(this, EventArgs.Empty);
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
            if (string.IsNullOrWhiteSpace(_labelHex128_64Base64))
            {
                UpdateLabelHex128_64();
            }
            return _labelHex128_64Base64;
        }
    }

    private void UpdateLabelHex128_64()
    {
        var contentAlignment = LabelPosition switch
        {
            ButtonLabelPosition.TOP => ContentAlignment.TopCenter,
            ButtonLabelPosition.BOTTOM => ContentAlignment.BottomCenter,
            _ => ContentAlignment.MiddleCenter
        };
        _labelHex128_64Base64 = Base64.GetBase64ByteArray((Bitmap)Base64.GetImageFromBase64(_labelBase64), new Size(128, 64), contentAlignment);
    }
}


public enum ButtonLabelPosition
{
    TOP,
    CENTER,
    BOTTOM,
}