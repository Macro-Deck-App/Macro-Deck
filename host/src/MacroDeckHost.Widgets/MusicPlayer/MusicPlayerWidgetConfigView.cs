using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>Builds a Music Player widget's <c>widget-config</c> tree from its stored data - see ADR 0050.
/// </summary>
internal static class MusicPlayerWidgetConfigView
{
	public static UiElement Build(JsonElement data, IMusicPlayerRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		var instanceId = new UiState<string>(WidgetConfigJson.ReadString(data, "instanceId") ?? string.Empty);
		var coverStyle = new UiState<string>(WidgetConfigJson.ReadString(data, "coverStyle") ?? "small");
		var showHeader = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showHeader") ?? true);
		var showTitle = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showTitle") ?? true);
		var showArtist = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showArtist") ?? true);
		var showAlbum = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showAlbum") ?? true);
		var showTimeline = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showTimeline") ?? true);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		// Reloaded whenever the picked instance changes, so a stale selection's synthetic "unavailable"
		// entry - added below for exactly the id currently bound - never lingers once a real one replaces
		// it, and a freshly-missing one appears without the user reopening the editor.
		UiOptionsState? instanceOptions = null;
		instanceOptions = new UiOptionsState(UiOptionSource.From((_, _)
			=> Task.FromResult<IReadOnlyList<UiOption>>(InstanceOptions(registry, instanceId.Peek()))));
		instanceOptions.Reload();

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "binding-heading", Text = AppStrings.Widgets.Editor.Binding() },
					PlayerPicker(registry, instanceId, instanceOptions),
					new UiHeading { Key = "style-heading", Text = AppStrings.Widgets.Editor.Style() },
					new UiChoiceInput
					{
						Key = "coverStyle",
						Label = AppStrings.Widgets.Editor.Style(),
						HideLabel = true,
						Cards = true,
						Binding = Bind.To(coverStyle),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							new()
							{
								Value = MusicPlayerWidgetData.SmallCoverStyle,
								Label = AppStrings.Widgets.Music.SmallCover(),
								Description = AppStrings.Widgets.Music.SmallCoverDesc(),
							},
							new()
							{
								Value = MusicPlayerWidgetData.FullCoverStyle,
								Label = AppStrings.Widgets.Music.FullCover(),
								Description = AppStrings.Widgets.Music.FullCoverDesc(),
							},
						]),
					},
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiBooleanInput
					{
						Key = "showHeader",
						Label = AppStrings.Widgets.Music.HeaderProvider(),
						Binding = Bind.To(showHeader),
					},
					new UiBooleanInput
					{
						Key = "showTitle",
						Label = AppStrings.Widgets.Music.Title(),
						Binding = Bind.To(showTitle),
					},
					new UiBooleanInput
					{
						Key = "showArtist",
						Label = AppStrings.Widgets.Music.Artist(),
						Binding = Bind.To(showArtist),
					},
					new UiBooleanInput
					{
						Key = "showAlbum",
						Label = AppStrings.Widgets.Music.Album(),
						Binding = Bind.To(showAlbum),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = "coverStyle",
							Values = [MusicPlayerWidgetData.SmallCoverStyle],
						},
					},
					new UiBooleanInput
					{
						Key = "showTimeline",
						Label = AppStrings.Widgets.Music.Timeline(),
						Binding = Bind.To(showTimeline),
					},
					new UiProse
					{
						Key = "live-update-hint", Text = AppStrings.Widgets.Music.LiveUpdateHint(),
					},
					new UiHeading { Key = "border-heading", Text = AppStrings.Widgets.Editor.Border() },
					WidgetConfigFragments.Border(borderStyle, borderColor, labelled: false),
				],
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows),
		};
	}

	/// <summary>The player picker, carrying the "nothing connected yet" sentence only while there is
	/// nothing to pick. Set rather than blanked, for the reason the border fragment's label gives: a
	/// description the DSL never saw is absent, where an empty one is a text with nothing in it.</summary>
	private static UiDynamicChoiceInput PlayerPicker(
		IMusicPlayerRegistry registry,
		UiState<string> instanceId,
		UiOptionsState instanceOptions)
	{
		var picker = new UiDynamicChoiceInput
		{
			Key = "instanceId",
			Label = AppStrings.Widgets.Music.MusicPlayer(),
			Binding = Bind.Custom(() => instanceId.Value,
				value =>
				{
					instanceId.Value = value;
					instanceOptions.Reload();
				}),
			OptionsState = instanceOptions,
		};

		return registry.GetInstances().Count == 0
			? picker with { Description = AppStrings.Widgets.Music.NoProvidersHint() }
			: picker;
	}

	/// <summary>The known instances plus "first available", and - when <paramref name="selected" /> names
	/// none of them - a synthetic entry carrying it, labelled unavailable, so a saved selection whose
	/// player no longer exists stays visible rather than silently resetting to "first available".</summary>
	private static List<UiOption> InstanceOptions(IMusicPlayerRegistry registry, string selected)
	{
		var instances = registry.GetInstances();

		var options = new List<UiOption>
		{
			// UiOption.Of rejects an empty value, so the "first available" sentinel is composed directly.
			new() { Value = string.Empty, Label = AppStrings.Widgets.Music.FirstAvailable() },
		};

		options.AddRange(instances.Select(instance => UiOption.Of(instance.InstanceId, instance.DisplayName)));

		if (!string.IsNullOrEmpty(selected) &&
			!instances.Any(instance => string.Equals(instance.InstanceId, selected, StringComparison.Ordinal)))
		{
			options.Add(UiOption.Of(selected, AppStrings.Widgets.Music.UnavailablePlayer()));
		}

		return options;
	}
}
