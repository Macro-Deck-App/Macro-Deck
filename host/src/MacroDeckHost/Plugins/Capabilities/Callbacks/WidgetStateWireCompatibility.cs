using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

/// <summary>
/// Translates the <c>widgets</c> host api between its two wire shapes and the SDK's
/// <see cref="WidgetAppearanceRequest" />/<see cref="WidgetTargetInfo" />, keyed on the session's
/// negotiated protocol version - <see cref="PluginCallbackRouter.RouteWidgetsAsync" /> for an incoming
/// <c>apply</c> call, <see cref="HostStatePusher" /> for the outgoing <c>host.state</c> push.
///
/// <para>
/// This is the one place left in the host that still reads the deprecated
/// <see cref="WidgetStateSelector" />: a v1 plugin's <c>apply</c> call carries only that four-way
/// selector, and it is translated to state ids here so nothing downstream has to know the selector
/// ever existed. The positional fallback must stay identical to
/// <c>WidgetAppearanceJson.ResolveStates</c>, or the same selector would land on different states
/// depending on whether the caller was a plugin or an in-process action.
/// </para>
/// </summary>
internal static class WidgetStateWireCompatibility
{
	/// <summary>
	/// Builds the domain request for a v1 <c>apply</c> call. The legacy selector is resolved against
	/// <paramref name="targetStates" />: <c>On</c>/<c>Off</c> prefer a state literally id'd "on"/"off",
	/// falling back to the second/first declared state, and become a no-op (no state ids - nothing to
	/// change) when the widget has neither.
	/// </summary>
	public static WidgetAppearanceRequest ToApplyRequest(
		WidgetsApplyArgumentsV1 arguments,
		IReadOnlyList<WidgetStateInfo> targetStates)
	{
#pragma warning disable CS0618 // v1's wire payload carries only the legacy selector - see the type remarks above.
		var selector = (WidgetStateSelector)arguments.State;

		var stateIds = selector switch
		{
			WidgetStateSelector.Both => [WidgetStates.All],
			WidgetStateSelector.On => ResolveNamedOrPositional(targetStates, "on", fallbackIndex: 1),
			WidgetStateSelector.Off => ResolveNamedOrPositional(targetStates, "off", fallbackIndex: 0),
			_ => [WidgetStates.Current]
		};

		return new WidgetAppearanceRequest
		{
			WidgetId = arguments.WidgetId,
			Patch = ToPatch(arguments.Patch),
			State = selector,
			StateIds = stateIds,
			ClearProperties = ToClearProperties(arguments.ClearProperties)
		};
#pragma warning restore CS0618
	}

	/// <summary>Builds the domain request for a v2 <c>apply</c> call - <see cref="WidgetsApplyArgumentsV2.StateIds" />
	/// passes straight through, no legacy selector involved.</summary>
	public static WidgetAppearanceRequest ToApplyRequest(WidgetsApplyArgumentsV2 arguments)
		=> new()
		{
			WidgetId = arguments.WidgetId,
			Patch = ToPatch(arguments.Patch),
			StateIds = [.. arguments.StateIds],
			ClearProperties = ToClearProperties(arguments.ClearProperties)
		};

	/// <summary>The <c>host.state</c> push payload for <paramref name="widgets" />, in whichever wire
	/// shape <paramref name="negotiatedVersion" /> uses. Null (no session yet) is treated as v1,
	/// defensively - the older, narrower shape a plugin the host has not finished negotiating with can be
	/// assumed to still understand.</summary>
	public static object ToWirePayload(IReadOnlyList<WidgetTargetInfo> widgets, int? negotiatedVersion)
		=> negotiatedVersion is >= 2
			? (object)widgets.Select(ToV2).ToList()
			: widgets.Select(ToV1).ToList();

	/// <summary>v1 target shape: <c>hasOnOffStates</c> collapsed from the real state count, <c>states</c>
	/// and <c>currentStateId</c> omitted - <see cref="WidgetTargetInfoDtoV1" /> has no properties for
	/// them.</summary>
	private static WidgetTargetInfoDtoV1 ToV1(WidgetTargetInfo info)
		=> new()
		{
			Id = info.Id,
			Label = info.Label,
			Location = info.Location,
			Type = info.Type,
			HasOnOffStates = info.States.Count > 1,
			AppearanceProperties = [.. info.AppearanceProperties.Select(property => (int)property)]
		};

	private static WidgetTargetInfoDtoV2 ToV2(WidgetTargetInfo info)
		=> new()
		{
			Id = info.Id,
			Label = info.Label,
			Location = info.Location,
			Type = info.Type,
			States = [.. info.States.Select(state => new WidgetStateInfoDto { Id = state.Id, Label = state.Label })],
			CurrentStateId = info.CurrentStateId,
			AppearanceProperties = [.. info.AppearanceProperties.Select(property => (int)property)]
		};

	private static WidgetAppearancePatch ToPatch(WidgetAppearancePatchDto dto)
		=> new()
		{
			Label = dto.Label,
			BackgroundColor = dto.BackgroundColor,
			LabelColor = dto.LabelColor,
			IconId = dto.IconId,
			IconFit = dto.IconFit,
			IconZoom = dto.IconZoom,
			IconOffsetX = dto.IconOffsetX,
			IconOffsetY = dto.IconOffsetY,
			IconOpacity = dto.IconOpacity,
			FontFaceId = dto.FontFaceId,
			FontSize = dto.FontSize,
			TextAlign = dto.TextAlign,
			LabelPosition = dto.LabelPosition,
			BorderStyle = dto.BorderStyle,
			BorderColor = dto.BorderColor
		};

	private static IReadOnlyCollection<WidgetAppearanceProperty> ToClearProperties(IReadOnlyCollection<int> wire)
		=> [.. wire.Select(property => (WidgetAppearanceProperty)property)];

	private static IReadOnlyCollection<string> ResolveNamedOrPositional(
		IReadOnlyList<WidgetStateInfo> states,
		string literalId,
		int fallbackIndex)
	{
		foreach (var state in states)
		{
			if (string.Equals(state.Id, literalId, StringComparison.Ordinal))
			{
				return [state.Id];
			}
		}

		return fallbackIndex < states.Count ? [states[fallbackIndex].Id] : [];
	}
}
