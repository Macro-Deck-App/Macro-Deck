using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Feeds.State;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal static class FeedWidgetConfigView
{
	public static UiElement Build(FeedWidgetKind kind, JsonElement data, IReadOnlyList<FeedSnapshot> feeds)
	{
		ArgumentNullException.ThrowIfNull(feeds);

		var options = FeedWidgetOptions.Parse(kind, data);
		var source = new UiState<string>(options.Source);
		var feed = new UiState<string>(options.FeedId?.ToString("D") ?? string.Empty);
		var group = new UiState<string>(options.Group ?? string.Empty);
		var count = new UiState<double>(options.Count);
		var showDescription = new UiState<bool>(options.ShowDescription);
		var showThumbnail = new UiState<bool>(options.ShowThumbnail);
		var cycleSeconds = new UiState<double>(options.CycleSeconds);
		var tap = new UiState<string>(options.Tap);
		var flows = new UiState<JsonElement>(FeedWidgetOptions.ReadFlows(data));

		var groups = feeds.Select(candidate => candidate.Group)
			.OfType<string>()
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var children = new List<UiElement>
		{
			new UiHeading
			{
				Key = "heading",
				Text = kind == FeedWidgetKind.List ? Strings.FeedList.Name() : Strings.Featured.Name()
			},
			new UiChoiceInput
			{
				Key = FeedWidgetTypes.SourceKey,
				Label = Strings.Config.Source(),
				Binding = Bind.To(source),
				Options = UiValue.Of<IReadOnlyList<UiOption>>([
					UiOption.Of(FeedWidgetTypes.AllSource, Strings.Config.SourceAll()),
					UiOption.Of(FeedWidgetTypes.FeedSource, Strings.Config.SourceFeed()),
					UiOption.Of(FeedWidgetTypes.GroupSource, Strings.Config.SourceGroup())
				])
			},
			new UiChoiceInput
			{
				Key = FeedWidgetTypes.FeedKey,
				Label = Strings.Config.Feed(),
				Binding = Bind.To(feed),
				Options = UiValue.Of<IReadOnlyList<UiOption>>([
					.. feeds.Select(candidate => UiOption.Of(candidate.EntryId.ToString("D"), candidate.Title))
				]),
				VisibleWhen = new UiVisibleWhen
				{
					ParameterName = FeedWidgetTypes.SourceKey,
					Values = [FeedWidgetTypes.FeedSource],
					SiblingValue = () => source.Value
				}
			},
			new UiChoiceInput
			{
				Key = FeedWidgetTypes.GroupKey,
				Label = Strings.Config.Group(),
				Binding = Bind.To(group),
				Options = UiValue.Of<IReadOnlyList<UiOption>>([.. groups.Select(name => UiOption.Of(name, name))]),
				VisibleWhen = new UiVisibleWhen
				{
					ParameterName = FeedWidgetTypes.SourceKey,
					Values = [FeedWidgetTypes.GroupSource],
					SiblingValue = () => source.Value
				}
			},
			new UiNumberInput
			{
				Key = FeedWidgetTypes.CountKey,
				Label = kind == FeedWidgetKind.List ? Strings.Config.Count() : Strings.Config.FeaturedCount(),
				Description = kind == FeedWidgetKind.List
					? Strings.Config.CountDescription()
					: Strings.Config.FeaturedCountDescription(),
				Binding = Bind.To(count),
				Min = 1,
				Max = FeedWidgetTypes.MaxCount,
				Step = 1
			},
			new UiBooleanInput
			{
				Key = FeedWidgetTypes.ShowDescriptionKey,
				Label = Strings.Config.ShowDescription(),
				Binding = Bind.To(showDescription)
			},
			new UiBooleanInput
			{
				Key = FeedWidgetTypes.ShowThumbnailKey,
				Label = Strings.Config.ShowThumbnail(),
				Binding = Bind.To(showThumbnail)
			}
		};

		if (kind == FeedWidgetKind.Featured)
		{
			children.Add(new UiNumberInput
			{
				Key = FeedWidgetTypes.CycleSecondsKey,
				Label = Strings.Config.CycleSeconds(),
				Description = Strings.Config.CycleSecondsDescription(),
				Binding = Bind.To(cycleSeconds),
				Min = 0,
				Max = FeedWidgetTypes.MaxCycleSeconds,
				Step = 1
			});
		}

		children.Add(new UiChoiceInput
		{
			Key = FeedWidgetTypes.TapKey,
			Label = Strings.Config.Tap(),
			Description = Strings.Config.TapDescription(),
			Binding = Bind.To(tap),
			Options = UiValue.Of<IReadOnlyList<UiOption>>([
				UiOption.Of(FeedWidgetTypes.OpenTap, Strings.Config.TapOpen()),
				UiOption.Of(FeedWidgetTypes.DialogTap, Strings.Config.TapDialog()),
				UiOption.Of(FeedWidgetTypes.FlowsTap, Strings.Config.TapFlows())
			])
		});
		children.Add(UiWidgetAppearance.Section(data,
			UiWidgetAppearanceFields.Border
			| UiWidgetAppearanceFields.BackgroundColor
			| UiWidgetAppearanceFields.TransparentBackground));

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties { Key = "properties", Children = children },
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children =
				[
					new UiActionsListEditor
					{
						Key = FeedWidgetTypes.FlowsKey,
						Binding = Bind.To(flows),
						CanRun = true
					}
				]
			}
		};
	}
}
