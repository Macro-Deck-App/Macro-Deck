using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.ClientTargets.CarThing;

/// <summary>
/// What a Car Thing firmware looks like from the host's side, and where it keeps the two things
/// provisioning needs: the directory its kiosk browser serves, and the service that runs the browser.
///
/// A profile is a *default set*, not a claim. Every value it carries is still shown as an editable
/// field, because a user's device can always be a variant nobody has seen.
/// </summary>
internal sealed record CarThingFirmware(
	string Id,
	string WebRoot,
	string KioskService,
	AdbServiceManager ServiceManager)
{
	/// <summary>
	/// The stock-derived image the community tools produce. Read off a real device: supervisord runs
	/// <c>python -m http.server 8080 -d /var/www</c> and a Chromium kiosk pointed at
	/// <c>http://localhost:8080</c>, so /var/www is the directory that decides what the screen shows.
	/// The original Spotify webapp is left renamed beside it and is not what gets served.
	/// </summary>
	internal static CarThingFirmware SupervisordKiosk { get; } = new("supervisord-kiosk",
		"/var/www",
		"chromium",
		AdbServiceManager.Supervisord);

	/// <summary>
	/// What is assumed when nothing could be established - the same shape as the known image, since a
	/// Car Thing that runs a web app at all is running some descendant of it.
	/// </summary>
	internal static CarThingFirmware Unknown { get; } = SupervisordKiosk with { Id = "unknown" };
}
