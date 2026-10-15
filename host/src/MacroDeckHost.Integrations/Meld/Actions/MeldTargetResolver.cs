using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Meld.Connections;
using MacroDeckHost.Integrations.Meld.State;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldTargetResolver
{
	public static bool TryRequireConnection(MeldConnection? connection, out ActionResult? error)
	{
		if (connection is null || !connection.IsConnected)
		{
			error = ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.Meld.Errors.NotConnected());
			return false;
		}

		error = null;
		return true;
	}

	public static bool TryResolve<T>(
		LocalizedText kindNoun,
		string? value,
		IReadOnlyDictionary<string, T> byId,
		IEnumerable<T> all,
		Func<T, string> nameOf,
		out T? target,
		out ActionResult? error)
		where T : class
	{
		target = null;
		if (string.IsNullOrWhiteSpace(value))
		{
			error = ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				AppStrings.Integrations.Meld.Errors.NoTargetSelected(kindNoun));
			return false;
		}

		if (byId.TryGetValue(value, out var byIdMatch))
		{
			target = byIdMatch;
			error = null;
			return true;
		}

		target = all.FirstOrDefault(item => string.Equals(nameOf(item), value, StringComparison.OrdinalIgnoreCase));
		if (target is not null)
		{
			error = null;
			return true;
		}

		error = ActionResult.Failed(ActionErrorCodes.NotFound,
			AppStrings.Integrations.Meld.Errors.TargetNotFound(kindNoun, value));
		return false;
	}

	public static bool CurrentMuted(MeldConnection connection, MeldTrack track)
	{
		return connection.TryGetGain(track.Id, out var gain) ? gain.Muted : track.Muted;
	}
}
