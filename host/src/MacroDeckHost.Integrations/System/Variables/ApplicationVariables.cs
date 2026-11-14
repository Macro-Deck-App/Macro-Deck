using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.System.State;
using MacroDeckHost.Platform.Applications;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.System.Variables;

namespace MacroDeckHost.Integrations.System.Variables;

internal static class ApplicationVariables
{
	public const string Running = "running";

	public const string Instances = "instances";

	public const string Windows = "windows";

	public const string WindowState = "window_state";

	private const string EntryPrefix = "entry-";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(2);

	private static readonly Slot[] _slots =
	[
		new(Running, VariableType.Boolean, Strings.AppRunning, null),
		new(Instances, VariableType.Numeric, Strings.AppInstances, 0),
		new(Windows, VariableType.Numeric, Strings.AppWindows, 0),
		new(WindowState, VariableType.Text, Strings.AppWindowState, null)
	];

	public static IReadOnlyList<VariableDefinition> Declare(
		string key,
		Guid entryId,
		LocalizedText title = default)
	{
		return
		[
			.. _slots.Select(slot => VariableDefinition.Eager($"system_app_{key}_{slot.Name}",
					slot.Type,
					slot.DecimalPlaces,
					_refreshInterval)
				with
				{
					DisplayName = slot.Label(),
					Id = IdOf(entryId, slot.Name),
					Configuration = new VariableConfiguration(entryId.ToString("N"), title)
				})
		];
	}

	public static IEnumerable<string> IdsOf(Guid entryId)
	{
		return _slots.Select(slot => IdOf(entryId, slot.Name));
	}

	public static bool TrySplit(string? definitionId, out Guid entryId, out string slot)
	{
		entryId = Guid.Empty;
		slot = string.Empty;
		if (definitionId is null
			|| !definitionId.StartsWith(EntryPrefix, StringComparison.Ordinal)
			|| definitionId.Length <= EntryPrefix.Length + 33
			|| definitionId[EntryPrefix.Length + 32] != '-'
			|| !Guid.TryParseExact(definitionId.AsSpan(EntryPrefix.Length, 32), "N", out entryId))
		{
			return false;
		}

		var candidate = definitionId[(EntryPrefix.Length + 33)..].Replace('-', '_');
		if (!_slots.Any(known => string.Equals(known.Name, candidate, StringComparison.Ordinal)))
		{
			return false;
		}

		slot = candidate;
		return true;
	}

	public static VariableReading Read(ApplicationSample? sample, string slot)
	{
		if (sample is null)
		{
			return VariableReading.Unavailable;
		}

		object? value = slot switch
		{
			Running => sample.Instances > 0,
			Instances => sample.Instances,
			Windows => sample.WindowState is null ? null : sample.Windows,
			WindowState => sample.WindowState is { } state ? ApplicationWindowStates.ToWireValue(state) : null,
			_ => null
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	private static string IdOf(Guid entryId, string slot)
	{
		return $"{EntryPrefix}{entryId:N}-{slot.Replace('_', '-')}";
	}

	private sealed record Slot(string Name, VariableType Type, Func<LocalizedString> Label, int? DecimalPlaces);
}
