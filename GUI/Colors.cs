using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using Windows.UI.ViewManagement;

namespace SuchByte.MacroDeck.GUI
{
    public static class Colors
    {

        static UISettings uISettings = new UISettings();

        public static Color WindowsAccentColor
        {
            get
            {
                var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                return Color.FromArgb(accentColor.A, accentColor.R, accentColor.G, accentColor.B);
            }
        }

        public static Color WindowsAccentColorLight
        {
            get
            {
                var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                return Color.FromArgb(accentColor.A, accentColor.R + 20, accentColor.G + 20, accentColor.B + 20);
            }
        }

        public static Color WindowsAccentColorDark
        {
            get
            {
                var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                return Color.FromArgb(accentColor.A, accentColor.R - 20, accentColor.G - 20, accentColor.B - 20);
            }
        }

        public static Color DefaultAccentColor
        {
            get
            {
                return Color.FromArgb(0, 123, 255);
            }
        }

        public static Color DefaultAccentColorLight
        {
            get
            {
                return Color.FromArgb(20, 143, 255);
            }
        }

        public static Color DefaultAccentColorDark
        {
            get
            {
                return Color.FromArgb(0, 103, 225);
            }
        }


    }
}
