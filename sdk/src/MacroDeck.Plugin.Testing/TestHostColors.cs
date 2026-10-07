using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Internal;
using MacroDeck.Sdk.Colors;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <see cref="MacroDeckTestHost" />'s side of the <c>colors</c> host api. Seed what a value resolves to
/// with <see cref="SetAsync" />; an unseeded fixed colour resolves to its canonical form and an unseeded
/// reference to nothing. Records each hosted plugin's watch table and pushes the resolved table to it after
/// every table change and every <see cref="SetAsync" /> that touches one of its watches.
/// </summary>
public sealed class TestHostColors
{
	private readonly ConcurrentDictionary<(string Value, string? WidgetId), string?> _seeded = new();
	private readonly ConcurrentDictionary<string, IReadOnlyList<ColorWatchDto>> _tables = new(StringComparer.Ordinal);
	private long _revision;

	internal Func<string, ProtocolEnvelope, Task>? Sender { get; set; }

	/// <summary>The watches <paramref name="pluginId" /> last synced; empty when it watches none.</summary>
	public IReadOnlyList<ColorWatchDto> WatchesOf(string pluginId)
		=> _tables.TryGetValue(pluginId, out var table) ? table : [];

	/// <summary>Makes <paramref name="value" /> resolve to <paramref name="color" /> and pushes the new table
	/// to every hosted plugin watching it.</summary>
	public async Task SetAsync(string value, string? color, string? widgetId = null)
	{
		_seeded[(value, widgetId)] = color;

		foreach (var (pluginId, table) in _tables)
		{
			if (table.Any(watch => watch.Value == value && watch.WidgetId == widgetId))
			{
				await PushAsync(pluginId).ConfigureAwait(false);
			}
		}
	}

	internal HostInvokeOutcome Dispatch(string pluginId, HostInvokePayload payload)
	{
		if (payload.Operation == HostOperations.Colors.Resolve)
		{
			var arguments = payload.Arguments?.Deserialize<ColorsResolveArguments>(PluginProtocolJson.Options);
			return arguments is null
				? HostInvokeOutcome.Failed(ProtocolErrorCodes.InvalidPayload, "This operation requires arguments.")
				: HostInvokeOutcome.Ok(new ColorsResolveResult { Color = Resolve(arguments.Value, arguments.WidgetId) });
		}

		var table = payload.Arguments?.Deserialize<ColorsWatchesArguments>(PluginProtocolJson.Options) ??
			new ColorsWatchesArguments();
		if (table.Watches.Count > ProtocolLimits.MaxColorWatches)
		{
			return HostInvokeOutcome.Failed(ProtocolErrorCodes.InvalidPayload,
				$"A plugin can watch at most {ProtocolLimits.MaxColorWatches} colours.");
		}

		if (table.Watches.Count == 0)
		{
			_tables.TryRemove(pluginId, out _);
		}
		else
		{
			_tables[pluginId] = table.Watches;
			_ = Task.Run(() => PushAsync(pluginId));
		}

		return HostInvokeOutcome.Ok(null);
	}

	private string? Resolve(string value, string? widgetId)
		=> _seeded.TryGetValue((value, widgetId), out var color) ? color : LocalColorApi.Resolve(value);

	private async Task PushAsync(string pluginId)
	{
		if (Sender is not { } send || !_tables.TryGetValue(pluginId, out var table))
		{
			return;
		}

		var state = new ColorWatchStateDto
		{
			Revision = Interlocked.Increment(ref _revision),
			Values = [.. table.Select(watch => new ColorWatchValueDto
			{
				WatchId = watch.WatchId,
				Color = Resolve(watch.Value, watch.WidgetId)
			})]
		};

		await send(pluginId,
				new ProtocolEnvelope
				{
					Type = MessageTypes.HostState,
					Id = Guid.NewGuid().ToString(),
					Payload = JsonSerializer.SerializeToElement(new HostStatePayload
						{
							Api = HostApis.Colors,
							Data = JsonSerializer.SerializeToElement(state, PluginProtocolJson.Options)
						},
						PluginProtocolJson.Options)
				})
			.ConfigureAwait(false);
	}
}
