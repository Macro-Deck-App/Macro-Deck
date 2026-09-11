namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetAppearanceSettingsResponse
{
	public string ThemeMode { get; set; } = string.Empty;

	public string AccentColor { get; set; } = string.Empty;

	public string FontFamily { get; set; } = string.Empty;
}
