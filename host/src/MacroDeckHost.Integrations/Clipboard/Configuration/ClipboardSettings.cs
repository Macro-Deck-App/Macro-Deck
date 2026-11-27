using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Integrations.Clipboard.Configuration;

internal sealed record ClipboardSettings(bool Watch, bool ExposeText)
{
	public static ClipboardSettings Off { get; } = new(false, false);

	public bool ShowsText => Watch && ExposeText;

	public static async Task<ClipboardSettings> ReadAsync(
		IIntegrationConfig config,
		CancellationToken cancellationToken)
	{
		var entries = await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
		if (entries.Count == 0)
		{
			return Off;
		}

		var entryId = entries[0].Id;
		var watch = ReadFlag(await config.GetStringAsync(entryId, ClipboardConfigKeys.Watch, cancellationToken)
			.ConfigureAwait(false));
		var exposeText = ReadFlag(await config
			.GetStringAsync(entryId, ClipboardConfigKeys.ExposeText, cancellationToken)
			.ConfigureAwait(false));
		return new ClipboardSettings(watch, watch && exposeText);
	}

	public static bool ReadFlag(object? raw)
	{
		return raw switch
		{
			bool flag => flag,
			string text => bool.TryParse(text.Trim(), out var parsed) && parsed,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			JsonElement { ValueKind: JsonValueKind.String } element => ReadFlag(element.GetString()),
			_ => false
		};
	}

	public static string Format(bool flag)
	{
		return flag ? "true" : "false";
	}
}
