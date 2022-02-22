using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Windows.UI.ViewManagement;

namespace SuchByte.MacroDeck.GUI
{
    public class Colors : UserControl
    {

        static readonly UISettings uISettings = new UISettings();

        public static Color WindowsAccentColor
        {
            get
            {
                try
                {
                    var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                    return ConvertWindowsUiColorToColor(accentColor);
                } catch
                {
                    return DefaultAccentColor;
                }
            }
        }

        public static Color WindowsAccentColorLight
        {
            get
            {
                try
                {
                    var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                    return LighterColor(ConvertWindowsUiColorToColor(accentColor), 30);
                } catch
                {
                    return DefaultAccentColorLight;
                }
            }
        }

        public static Color WindowsAccentColorDark
        {
            get
            {
                try
                {
                    var accentColor = uISettings.GetColorValue(UIColorType.Accent);
                    return DarkerColor(ConvertWindowsUiColorToColor(accentColor), 30);
                } catch
                {
                    return DefaultAccentColorDark;
                }
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

        public static Color DarkerColor(Color color, float correctionfactory = 50f)
        {
            const float hundredpercent = 100f;
            return Color.FromArgb((int)(((float)color.R / hundredpercent) * correctionfactory),
                (int)(((float)color.G / hundredpercent) * correctionfactory), (int)(((float)color.B / hundredpercent) * correctionfactory));
        }

        public static Color LighterColor(Color color, float correctionfactory = 50f)
        {
            correctionfactory = correctionfactory / 100f;
            const float rgb255 = 255f;
            return Color.FromArgb((int)((float)color.R + ((rgb255 - (float)color.R) * correctionfactory)), (int)((float)color.G + ((rgb255 - (float)color.G) * correctionfactory)), (int)((float)color.B + ((rgb255 - (float)color.B) * correctionfactory))
                );
        }

        public static Color ConvertWindowsUiColorToColor(Windows.UI.Color windowsUiColor)
        {
            int alpha = windowsUiColor.A;
            int red = windowsUiColor.R;
            int green = windowsUiColor.G;
            int blue = windowsUiColor.B;

            if (alpha > 255)
                alpha = 255;
            if (alpha < 0)
                alpha = 0;

            if (red > 255)
                red = 255;
            if (red < 0)
                red = 0;

            if (green > 255)
                green = 255;
            if (green < 0)
                green = 0;

            if (blue > 255)
                blue = 255;
            if (blue < 0)
                blue = 0;

            return Color.FromArgb(alpha, red, green, blue);
        }

    }
}
