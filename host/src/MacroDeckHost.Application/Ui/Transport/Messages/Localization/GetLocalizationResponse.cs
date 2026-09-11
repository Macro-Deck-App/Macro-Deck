using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Localization;

public class GetLocalizationResponse
{
	public string Culture { get; set; } = string.Empty;

	public string FallbackCulture { get; set; } = string.Empty;

	public Dictionary<string, string> Translations { get; set; } = new();

	public List<string> AvailableCultures { get; set; } = new();

	/// <summary>
	/// True while no language has been chosen and <see cref="Culture" /> is the operating system's.
	/// A client presents this as its own "System" entry instead of the culture it resolves to.
	/// </summary>
	public bool FollowSystem { get; set; }

	public string TimeFormat { get; set; } = AppPreferenceService.TimeFormatSystem;

	public string HourCycle { get; set; } = HourCycles.H23;
}
