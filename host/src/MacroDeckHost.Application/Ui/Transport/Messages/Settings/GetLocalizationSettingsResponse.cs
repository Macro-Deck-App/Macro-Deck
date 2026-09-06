namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetLocalizationSettingsResponse
{
	public string Culture { get; set; } = string.Empty;

	public string FallbackCulture { get; set; } = string.Empty;

	/// <summary>
	/// True while no language has been chosen and <see cref="Culture" /> is the operating system's.
	/// A client presents this as its own "System" entry instead of the culture it resolves to.
	/// </summary>
	public bool FollowSystem { get; set; }
}
