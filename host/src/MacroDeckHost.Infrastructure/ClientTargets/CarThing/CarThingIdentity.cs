using System.Text;
using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.ClientTargets.CarThing;

/// <summary>
/// Recognises a Spotify Car Thing among the attached adb devices.
///
/// Separators are stripped before matching rather than enumerated: a real device reports
/// <c>Car_Thing</c>, and the community firmwares variously use <c>superbird</c>, <c>Car Thing</c> and
/// <c>CarThing</c> for the same board. Only the product and model are matched - a jailbroken device
/// runs Linux rather than Android, so it has no <c>getprop</c>, and the manufacturer field ends up
/// carrying whatever the device shell printed instead of an identity.
/// </summary>
internal static class CarThingIdentity
{
	private static readonly string[] _markers = ["superbird", "carthing"];

	internal static bool Matches(AdbDevice device)
		=> ContainsMarker(device.Product) || ContainsMarker(device.Model);

	private static bool ContainsMarker(string? value)
	{
		if (value is null)
		{
			return false;
		}

		var normalized = Normalize(value);
		return _markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
	}

	/// <summary>Lower-cases and drops everything that is not a letter or digit.</summary>
	private static string Normalize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value)
		{
			if (char.IsLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
			}
		}

		return builder.ToString();
	}
}
