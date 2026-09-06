namespace MacroDeckHost.Application.Rendering;

public static class LabelGroups
{
	public static string For(string widgetId, string state)
		=> $"label:{widgetId}:{Normalize(state)}";

	/// <summary>
	/// Sanitizes a client-supplied state id so it is safe to use as both a realtime group name and a
	/// dictionary key: trimmed, lowercased, restricted to the state id charset, clamped to 64 chars.
	/// An empty result falls back to "off" so a stray subscribe never produces a group nobody can push to.
	/// </summary>
	public static string Normalize(string state)
	{
		if (string.IsNullOrWhiteSpace(state))
		{
			return "off";
		}

		var trimmed = state.Trim().ToLowerInvariant();
		var filtered = new System.Text.StringBuilder(Math.Min(trimmed.Length, 64));
		foreach (var c in trimmed)
		{
			if (filtered.Length >= 64)
			{
				break;
			}

			if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-')
			{
				filtered.Append(c);
			}
		}

		return filtered.Length == 0 ? "off" : filtered.ToString();
	}
}
