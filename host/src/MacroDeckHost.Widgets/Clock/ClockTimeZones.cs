using MacroDeck.Ui.Config.Options;

namespace MacroDeckHost.Widgets.Clock;

/// <summary>Every IANA time zone this host's runtime knows, for the Clock configuration's zone picker.
/// <see cref="MacroDeck.Ui.Model.References.UiTimeReference" /> reads an IANA id, and .NET's own catalog
/// answers a platform id on Windows unless converted - see <see cref="TimeZoneInfo.HasIanaId" />.</summary>
internal static class ClockTimeZones
{
	public static IReadOnlyList<UiOption> Options()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var options = new List<UiOption>();

		foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
		{
			var id = IanaId(zone);

			if (seen.Add(id))
			{
				options.Add(UiOption.Of(id));
			}
		}

		options.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));

		return options;
	}

	private static string IanaId(TimeZoneInfo zone)
	{
		if (zone.HasIanaId)
		{
			return zone.Id;
		}

		return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;
	}
}
