namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateHttpSettingsResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public string? CustomUserAgent { get; set; }

	public string EffectiveUserAgent { get; set; } = string.Empty;

	public string DefaultUserAgent { get; set; } = string.Empty;
}
