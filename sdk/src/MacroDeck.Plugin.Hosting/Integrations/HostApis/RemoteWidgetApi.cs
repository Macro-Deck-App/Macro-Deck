using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IWidgetApi"/> over <c>host.invoke</c> against <see cref="HostApis.Widgets"/>.
/// <see cref="GetWidgets"/> and <see cref="Exists"/> are served from <see cref="HostStateCache"/>;
/// <see cref="ApplyAsync"/> is a real round trip.</summary>
internal sealed class RemoteWidgetApi(
	IHostInvoker invoker,
	PluginConnectionState connectionState,
	HostStateCache stateCache) : IWidgetApi
{
	public IReadOnlyList<WidgetTargetInfo> GetWidgets() =>
		stateCache.GetList<WidgetTargetInfo>(Protocol.Callbacks.HostApis.Widgets);

	public bool Exists(string widgetId)
		=> GetWidgets().Any(widget => string.Equals(widget.Id, widgetId, StringComparison.Ordinal));

	public async Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
	{
		// Negotiated null (no session yet) is treated as v1, defensively - the older, narrower shape a
		// host that has not yet told us its version can be assumed to still understand.
		object arguments = connectionState.NegotiatedVersion is >= 2
			? ToV2(request)
			: ToV1(request);

		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Widgets,
			HostOperations.Widgets.Apply,
			arguments,
			cancellationToken);
		return result?.Deserialize<bool>(PluginProtocolJson.Options) ?? false;
	}

	// SetStateAsync/AdvanceStateAsync are deliberately left on their interface defaults: an explicit
	// state write needs a new Widgets operation and DTOs in the plugin protocol, a separate versioned
	// surface this change does not touch. A remote plugin influences state by providing it instead.

	public Task InvalidateIconAsync(string actionId, CancellationToken cancellationToken = default)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Widgets,
			HostOperations.Widgets.InvalidateIcon,
			new WidgetsInvalidateIconArguments { ActionId = actionId },
			cancellationToken);

	private static WidgetsApplyArgumentsV1 ToV1(WidgetAppearanceRequest request)
		=> new()
		{
			WidgetId = request.WidgetId,
			Patch = ToPatchDto(request.Patch),
			State = StateSelectorOf(request.ResolveStateIds()),
			ClearProperties = [.. request.ClearProperties.Select(property => (int)property)]
		};

	private static WidgetsApplyArgumentsV2 ToV2(WidgetAppearanceRequest request)
		=> new()
		{
			WidgetId = request.WidgetId,
			Patch = ToPatchDto(request.Patch),
			StateIds = request.ResolveStateIds(),
			ClearProperties = [.. request.ClearProperties.Select(property => (int)property)]
		};

	private static WidgetAppearancePatchDto ToPatchDto(WidgetAppearancePatch patch)
		=> new()
		{
			Label = patch.Label,
			BackgroundColor = patch.BackgroundColor,
			LabelColor = patch.LabelColor,
			IconId = patch.IconId,
			IconFit = patch.IconFit,
			IconZoom = patch.IconZoom,
			IconOffsetX = patch.IconOffsetX,
			IconOffsetY = patch.IconOffsetY,
			IconOpacity = patch.IconOpacity,
			FontFaceId = patch.FontFaceId,
			FontSize = patch.FontSize,
			TextAlign = patch.TextAlign,
			LabelPosition = patch.LabelPosition,
			BorderStyle = patch.BorderStyle,
			BorderColor = patch.BorderColor
		};

	/// <summary>
	/// The closest a v1 wire request can come to <paramref name="stateIds" />: the legacy selector has no
	/// way to name an arbitrary id, so anything other than the recognised legacy ids falls back to
	/// "current" rather than guessing.
	/// </summary>
	private static int StateSelectorOf(IReadOnlyCollection<string> stateIds)
	{
		if (stateIds.Count != 1)
		{
			return (int)WidgetStateSelectorV1.Current;
		}

		var id = stateIds.First();

		if (WidgetStates.IsAll(id))
		{
			return (int)WidgetStateSelectorV1.Both;
		}

		if (string.Equals(id, "on", StringComparison.Ordinal))
		{
			return (int)WidgetStateSelectorV1.On;
		}

		if (string.Equals(id, "off", StringComparison.Ordinal))
		{
			return (int)WidgetStateSelectorV1.Off;
		}

		return (int)WidgetStateSelectorV1.Current;
	}

	// A private, unobsoleted mirror of the deprecated MacroDeck.Sdk.Widgets.WidgetStateSelector, so the
	// v1 fallback above can name its four values without triggering CS0618 for reading the very enum
	// this compatibility shim exists to translate away from.
	private enum WidgetStateSelectorV1
	{
		Current,
		On,
		Off,
		Both
	}
}
