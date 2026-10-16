using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Voicemeeter.Models;
using MacroDeckHost.Integrations.Voicemeeter.State;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal static class VoicemeeterOptions
{
	private const int CacheSeconds = 15;

	public static DynamicOptionsResult Channels(VoicemeeterChannelCatalog catalog, VoicemeeterChannelKind kind)
	{
		return new DynamicOptionsResult
		{
			Options = catalog.For(kind)
				.Select(option => new ActionParameterOption { Value = option.Value, Label = option.Label })
				.ToList(),
			AllowsCustomValue = true,
			CacheSeconds = CacheSeconds
		};
	}

	public static DynamicOptionsResult BusAssignments(VoicemeeterChannelCatalog catalog)
	{
		return new DynamicOptionsResult
		{
			Options = catalog.BusAssignments
				.Select(bus => new ActionParameterOption { Value = bus, Label = bus })
				.ToList(),
			AllowsCustomValue = true,
			CacheSeconds = CacheSeconds
		};
	}

	public static DynamicOptionsResult ForParameter(VoicemeeterChannelCatalog catalog, string parameterName)
	{
		return parameterName switch
		{
			VoicemeeterActionValues.StripParameter => Channels(catalog, VoicemeeterChannelKind.Strip),
			VoicemeeterActionValues.BusParameter => Channels(catalog, VoicemeeterChannelKind.Bus),
			_ => new DynamicOptionsResult { Options = [], AllowsCustomValue = true }
		};
	}
}
