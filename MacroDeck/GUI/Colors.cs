﻿using System.Drawing;
using Windows.UI.ViewManagement;

namespace SuchByte.MacroDeck.GUI;

public class Colors
{
    public static void Initialize()
    {
        try
        {
            var uISettings = new UISettings();
            var accentColor = uISettings.GetColorValue(UIColorType.Accent);
            WindowsAccentColor = ConvertWindowsUiColorToColor(accentColor);
            WindowsAccentColorLight = LighterColor(ConvertWindowsUiColorToColor(accentColor), 30);
            WindowsAccentColorDark = DarkerColor(ConvertWindowsUiColorToColor(accentColor), 30);
        } catch {}
    }

    public static Color WindowsAccentColor { get; set; } = DefaultAccentColor;

    public static Color WindowsAccentColorLight { get; set; } = DefaultAccentColorLight;

    public static Color WindowsAccentColorDark { get; set; } = DefaultAccentColorDark;

    public static Color DefaultAccentColor => Color.FromArgb(0, 123, 255);

    public static Color DefaultAccentColorLight => Color.FromArgb(20, 143, 255);

    public static Color DefaultAccentColorDark => Color.FromArgb(0, 103, 205);

    public static Color DarkerColor(Color color, float correctionfactory = 50f)
    {
        const float hundredpercent = 100f;
        return Color.FromArgb((int)((color.R / hundredpercent) * correctionfactory),
            (int)((color.G / hundredpercent) * correctionfactory), (int)((color.B / hundredpercent) * correctionfactory));
    }

    public static Color LighterColor(Color color, float correctionfactory = 50f)
    {
        correctionfactory = correctionfactory / 100f;
        const float rgb255 = 255f;
        return Color.FromArgb((int)(color.R + ((rgb255 - color.R) * correctionfactory)), (int)(color.G + ((rgb255 - color.G) * correctionfactory)), (int)(color.B + ((rgb255 - color.B) * correctionfactory)));
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