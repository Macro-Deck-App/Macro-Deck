using System.Diagnostics;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public class MacroDeck3NoticeDialog : DialogForm
{
    private const string WebsiteUrl = "https://macro-deck.app";
    private const string DocsUrl = "https://docs.macro-deck.app/";

    private readonly CheckBox _checkDoNotShowAgain;

    public bool DoNotShowAgain => _checkDoNotShowAgain.Checked;

    public MacroDeck3NoticeDialog()
    {
        var strings = LanguageManager.Strings;

        Text = strings.MacroDeck3IsHere;
        ClientSize = new Size(940, 560);

        var preview = new PictureBox
        {
            Image = LoadImage("MacroDeck3Preview.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(20, 20),
            Size = new Size(480, 300)
        };

        var icon = new PictureBox
        {
            Image = LoadImage("MacroDeck3Icon.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(520, 20),
            Size = new Size(56, 56)
        };

        var title = new Label
        {
            Text = strings.MacroDeck3IsHere,
            Font = new Font("Tahoma", 18F, FontStyle.Bold),
            Location = new Point(596, 20),
            Size = new Size(324, 56),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };

        var intro = new Label
        {
            Text = strings.MacroDeck3Intro,
            Location = new Point(520, 90),
            Size = new Size(400, 66),
            UseMnemonic = false
        };

        var features = new Label
        {
            Text = string.Join(Environment.NewLine + Environment.NewLine,
                new[]
                {
                    strings.MacroDeck3FeatureCrossPlatform,
                    strings.MacroDeck3FeatureWidgets,
                    strings.MacroDeck3FeaturePluginWidgets,
                    strings.MacroDeck3FeatureSdk,
                    strings.MacroDeck3FeatureMore
                }.Select(x => "•  " + x)),
            Location = new Point(520, 160),
            Size = new Size(400, 290),
            UseMnemonic = false
        };

        var sideBySide = new Label
        {
            Text = strings.MacroDeck3SideBySideHint,
            BackColor = Color.FromArgb(60, 60, 60),
            Padding = new Padding(10),
            Location = new Point(20, 340),
            Size = new Size(480, 70),
            UseMnemonic = false
        };

        _checkDoNotShowAgain = new CheckBox
        {
            Text = strings.DontShowThisAgain,
            AutoSize = true,
            Location = new Point(20, 516),
            UseMnemonic = false
        };

        var btnLearnMore = new ButtonPrimary
        {
            Text = strings.MacroDeck3LearnMore,
            Location = new Point(620, 508),
            Size = new Size(300, 36),
            UseWindowsAccentColor = true
        };
        btnLearnMore.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(WebsiteUrl) { UseShellExecute = true });
        };

        var linkDocs = new LinkLabel
        {
            Text = strings.MacroDeck3Documentation,
            LinkColor = Color.DeepSkyBlue,
            ActiveLinkColor = Color.DeepSkyBlue,
            AutoSize = true,
            Location = new Point(520, 470),
            UseMnemonic = false
        };
        linkDocs.LinkClicked += (_, _) =>
        {
            Process.Start(new ProcessStartInfo(DocsUrl) { UseShellExecute = true });
        };

        Controls.AddRange([linkDocs, icon, preview, title, intro, features, sideBySide, _checkDoNotShowAgain, btnLearnMore]);
    }

    private static Image? LoadImage(string fileName)
    {
        using var stream = typeof(MacroDeck).Assembly
            .GetManifestResourceStream($"SuchByte.MacroDeck.Resources.{fileName}");
        return stream == null ? null : Image.FromStream(stream);
    }
}
