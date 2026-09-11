namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateLocalizationSettingsRequest
{
	public string? Culture { get; set; }

	public string? TimeFormat { get; set; }

	/// <summary>
	/// Clears the stored choice so the operating system's language applies, now and whenever it changes.
	/// <see cref="Culture" /> is ignored when this is set - the two are alternatives, not a value and a
	/// hint, so a client cannot ask for both and get whichever the host happens to prefer.
	/// </summary>
	public bool FollowSystem { get; set; }
}
