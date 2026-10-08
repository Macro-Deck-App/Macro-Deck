namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetHttpSettingsResponse
{
	public string? CustomUserAgent { get; set; }

	public string EffectiveUserAgent { get; set; } = string.Empty;

	public string DefaultUserAgent { get; set; } = string.Empty;
}
