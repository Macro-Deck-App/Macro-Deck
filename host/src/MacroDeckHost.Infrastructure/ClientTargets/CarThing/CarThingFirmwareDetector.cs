using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.ClientTargets.CarThing;

/// <summary>
/// Works out which firmware a Car Thing is running, so setup can fill its own fields in instead of
/// asking the user to know (issue #727).
///
/// Deliberately frugal: two read-only commands, and it gives up rather than probing further. A
/// minimal adbd does not survive being interrogated - the same device this exists for drops off the
/// USB bus under repeated shell sessions, which is why the reconcile loop and the property probe
/// both stopped doing that (ADR 0030).
/// </summary>
internal static class CarThingFirmwareDetector
{
	/// <summary>
	/// Web roots to test, most likely first. /var/www leads because that is what a real device's
	/// <c>python -m http.server</c> is actually pointed at; the others are where older or forked
	/// images kept it. The first one that exists wins.
	/// </summary>
	private static readonly string[] _webRootCandidates =
	[
		"/var/www",
		"/usr/share/qt-superbird-app/webapp",
		"/usr/share/chromium/webapp"
	];

	internal static async Task<CarThingFirmware> DetectAsync(
		IAdbManager adbManager,
		string serial,
		CancellationToken cancellationToken)
	{
		var services = await adbManager.QueryAsync(new AdbListServicesCommand(serial, AdbServiceManager.Supervisord),
			cancellationToken);

		if (!services.Success || services.Data is not { } listing)
		{
			return CarThingFirmware.Unknown;
		}

		var kioskService = PickKioskService(listing);
		if (kioskService is null)
		{
			return CarThingFirmware.Unknown;
		}

		var webRoot = await FindWebRootAsync(adbManager, serial, cancellationToken);

		return CarThingFirmware.SupervisordKiosk with
		{
			KioskService = kioskService,
			WebRoot = webRoot ?? CarThingFirmware.SupervisordKiosk.WebRoot
		};
	}

	/// <summary>
	/// The browser among supervisord's programs. Matched by name because that is what a restart needs;
	/// the ordering makes a plain `chromium` win over a variant when both somehow appear.
	/// </summary>
	internal static string? PickKioskService(string supervisorctlStatus)
	{
		var names = supervisorctlStatus
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
			.Where(name => !string.IsNullOrEmpty(name))
			.Select(name => name!)
			.ToList();

		return names.FirstOrDefault(name => string.Equals(name, "chromium", StringComparison.OrdinalIgnoreCase)) ??
			names.FirstOrDefault(name =>
				name.Contains("chromium", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
				name.Contains("kiosk", StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<string?> FindWebRootAsync(
		IAdbManager adbManager,
		string serial,
		CancellationToken cancellationToken)
	{
		foreach (var candidate in _webRootCandidates)
		{
			var exists = await adbManager.QueryAsync(new AdbDirectoryExistsCommand(serial, candidate),
				cancellationToken);
			if (exists.Success)
			{
				return candidate;
			}
		}

		return null;
	}
}
