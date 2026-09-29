using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
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
		var showSource = new UiState<bool>(WidgetConfigJson.ReadBool(data, "showSource") ?? true);

		var border = WidgetConfigJson.ReadObject(data, "border");
		var borderStyle = new UiState<string>(WidgetConfigJson.ReadString(border, "style") ?? "off");
		var borderColor = new UiState<string>(WidgetConfigJson.ReadString(border, "color") ?? string.Empty);

		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));

		var storedOptions = MusicPlayerWidgetData.Parse(data).ReadInstanceOptions();
		var optionFields = new UiState<IReadOnlyList<InstanceOptionFields>>(
			InstanceOptionFields.For(registry, instanceId.Peek(), storedOptions));

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
					PlayerPicker(registry, instanceId, instanceOptions, optionFields),
					new UiRepeat<InstanceOptionFields>
					{
						Key = "instance-options",
						Items = UiValue.From(() => optionFields.Value),
						KeySelector = fields => fields.ItemKey,
						Template = (fields, _) => fields.Build(),
					},
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
						Key = "showSource",
						Label = AppStrings.Widgets.Music.Source(),
						Binding = Bind.To(showSource),
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
		UiOptionsState instanceOptions,
		UiState<IReadOnlyList<InstanceOptionFields>> optionFields)
	{
		var picker = new UiDynamicChoiceInput
		{
			Key = "instanceId",
			Label = AppStrings.Widgets.Music.MusicPlayer(),
			Binding = Bind.Custom(() => instanceId.Value,
				value =>
				{
					// The new fields start from the new instance's defaults, and exist before the selection
					// changes, so the fields are never built for one instance from another's cells.
					if (!string.Equals(value, instanceId.Peek(), StringComparison.Ordinal))
					{
						optionFields.Value = InstanceOptionFields.For(registry, value, stored: null);
					}

					instanceId.Value = value;
					instanceOptions.Reload();
				}),
			OptionsState = instanceOptions,
		};

		return registry.GetInstances().Count == 0
			? picker with { Description = AppStrings.Widgets.Music.NoProvidersHint() }
			: picker;
	}

	internal static List<UiOption> InstanceOptions(IMusicPlayerRegistry registry, string selected)
	{
		var instances = registry.GetInstances();

		var options = new List<UiOption>
		{
			// UiOption.Of rejects an empty value, so the active-player sentinel is composed directly.
			new() { Value = string.Empty, Label = AppStrings.Widgets.Music.ActivePlayer() },
		};

		options.AddRange(instances.Select(instance => UiOption.Of(instance.InstanceId, instance.DisplayName)));

		if (!string.IsNullOrEmpty(selected) &&
			!instances.Any(instance => string.Equals(instance.InstanceId, selected, StringComparison.Ordinal)))
		{
			options.Add(UiOption.Of(selected, AppStrings.Widgets.Music.UnavailablePlayer()));
		}

		return options;
	}

	internal sealed class InstanceOptionFields
	{
		private readonly IReadOnlyList<ActionParameter> _options;
		private readonly Dictionary<string, object> _cells;

		private InstanceOptionFields(
			string instanceId,
			IReadOnlyList<ActionParameter> options,
			IReadOnlyDictionary<string, object> values)
		{
			InstanceId = instanceId;
			_options = options;
			_cells = options.ToDictionary(option => option.Name,
				option => values[option.Name] switch
				{
					double number => (object)new UiState<double>(number),
					bool flag => new UiState<bool>(flag),
					var other => new UiState<string>(Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty),
				},
				StringComparer.Ordinal);
		}

		public string InstanceId { get; }

		// Instance ids contain separators an item key may not, so the key is a digest of the id.
		public string ItemKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(InstanceId)).AsSpan(0, 8))
			.ToLowerInvariant();

		public static IReadOnlyList<InstanceOptionFields> For(
			IMusicPlayerRegistry registry,
			string instanceId,
			IReadOnlyDictionary<string, JsonElement>? stored)
		{
			var descriptor = registry.GetInstances()
				.FirstOrDefault(instance => string.Equals(instance.InstanceId, instanceId, StringComparison.Ordinal));

			return descriptor is not null && MusicPlayerVariant.For(descriptor, stored) is { } variant
				? [new InstanceOptionFields(descriptor.InstanceId, descriptor.Options, variant.Options)]
				: [];
		}

		public UiElement Build()
			=> new UiObjectInput
			{
				Key = "instanceOptions",
				HideLabel = true,
				Children = [.. _options.Select(Field)],
			};

		private UiElement Field(ActionParameter option)
		{
			var label = option.Label.IsEmpty ? LocalizedText.FromLiteral(option.Name) : option.Label;

			return _cells[option.Name] switch
			{
				UiState<double> number => new UiNumberInput
				{
					Key = option.Name,
					Label = label,
					Description = option.Description,
					Binding = Bind.To(number),
					Min = option.Min is { } min ? min : default(UiValue<double>),
					Max = option.Max is { } max ? max : default(UiValue<double>),
					Step = option.Step is { } step ? step : default(UiValue<double>),
					ShowSlider = option.ShowSlider,
				},
				UiState<bool> flag => new UiBooleanInput
				{
					Key = option.Name, Label = label, Description = option.Description, Binding = Bind.To(flag),
				},
				UiState<string> text when option.Type == ActionParameterType.Choice => new UiChoiceInput
				{
					Key = option.Name,
					Label = label,
					Description = option.Description,
					Binding = Bind.To(text),
					Options = UiValue.Of<IReadOnlyList<UiOption>>([
						.. option.Options!.Select(choice => new UiOption
						{
							Value = choice.Value,
							Label = choice.Label.IsEmpty ? LocalizedText.FromLiteral(choice.Value) : choice.Label,
						}),
					]),
				},
				UiState<string> text => new UiStringInput
				{
					Key = option.Name, Label = label, Description = option.Description, Binding = Bind.To(text),
				},
				_ => throw new InvalidOperationException($"No field for music player option '{option.Name}'."),
			};
		}
	}
}
