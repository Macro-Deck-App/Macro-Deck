using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;

/// <summary>Turns a pushed surface back into the SDK shape a provider renders from.</summary>
internal static class DeviceSurfaceMapper
{
	public static DeviceSurface ToSurface(DeviceSurfaceDto dto)
		=> new()
		{
			Revision = dto.Revision,
			Profile = dto.Profile is { } profile ? new DeviceSurfaceProfile(profile.Id, profile.Name) : null,
			Folder = dto.Folder is { } folder
				? new DeviceSurfaceFolder(folder.Id, folder.Name, folder.ParentId, folder.IsRoot)
				: null,
			Layout = new DeviceSurfaceLayout
			{
				Rows = dto.Layout.Rows,
				Columns = dto.Layout.Columns,
				WidgetSpacing = dto.Layout.WidgetSpacing,
				WidgetBorderRadius = dto.Layout.WidgetBorderRadius,
				BackgroundColor = dto.Layout.BackgroundColor,
				LayoutReference = dto.Layout.LayoutReference
			},
			Widgets = [.. dto.Widgets.Select(ToWidget)]
		};

	public static DevicesInteractionArguments ToArguments(string sessionId, DeviceInteraction interaction)
		=> new()
		{
			SessionId = sessionId,
			Kind = interaction.Kind.ToString(),
			WidgetId = interaction.Target.WidgetId,
			ControlIndex = interaction.Target.ControlIndex,
			Value = interaction.Value,
			SurfaceRevision = interaction.SurfaceRevision,
			Data = interaction.Data
		};

	private static DeviceSurfaceWidget ToWidget(DeviceSurfaceWidgetDto dto)
		=> new()
		{
			Id = dto.Id,
			Type = dto.Type,
			PositionX = dto.PositionX,
			PositionY = dto.PositionY,
			Width = dto.Width,
			Height = dto.Height,
			IsPinned = dto.IsPinned,
			StateId = dto.StateId,
			StateLabel = dto.StateLabel,
			Appearance = dto.Appearance is { } appearance
				? new DeviceSurfaceAppearance
				{
					Label = appearance.Label,
					LabelColor = appearance.LabelColor,
					BackgroundColor = appearance.BackgroundColor,
					IconId = appearance.IconId,
					IconVersion = appearance.IconVersion,
					HasProviderIcon = appearance.HasProviderIcon,
					IconFit = appearance.IconFit,
					IconZoom = appearance.IconZoom,
					IconOffsetX = appearance.IconOffsetX,
					IconOffsetY = appearance.IconOffsetY,
					FontSize = appearance.FontSize,
					TextAlign = appearance.TextAlign,
					LabelPosition = appearance.LabelPosition,
					Extra = appearance.Extra
				}
				: null,
			// An unrecognised kind name becomes Unknown rather than a parse failure: a host that learned a
			// new kind must not be able to break a provider built against an older SDK.
			SupportedInteractions =
			[
				.. dto.SupportedInteractions.Select(name
					=> Enum.TryParse<DeviceInteractionKind>(name, ignoreCase: true, out var kind)
						? kind
						: DeviceInteractionKind.Unknown)
			]
		};
}
