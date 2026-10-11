using System.Globalization;
using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using Heading = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Widget;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam.Config;
using ViewStrings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal static class SteamWidgetConfigViews
{
	private const UiWidgetAppearanceFields Appearance = UiWidgetAppearanceFields.Border |
		UiWidgetAppearanceFields.BackgroundColor |
		UiWidgetAppearanceFields.TransparentBackground;

	public static UiElement Playing(JsonElement data, IReadOnlyList<SteamAccountSnapshot> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		var options = SteamWidgetSettings.ParsePlaying(data);
		var showSessionTime = new UiState<bool>(options.ShowSessionTime);
		var account = new UiState<string>(options.AccountId ?? string.Empty);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "heading", Text = Heading.CurrentlyPlaying.Name() },
					new UiChoiceInput
					{
						Key = SteamWidgetType.AccountKey,
						Label = Strings.Account(),
						Description = Strings.AccountDescription(),
						Placeholder = ViewStrings.ThisComputer(),
						Binding = Bind.To(account),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. accounts.Where(candidate => candidate.HasWebApi).Select(candidate => UiOption.Of(candidate.EntryId, candidate.Title)),
						]),
					},
					new UiBooleanInput
					{
						Key = SteamWidgetType.ShowSessionTimeKey,
						Label = Strings.ShowSessionTime(),
						Binding = Bind.To(showSessionTime),
					},
					UiWidgetAppearance.Section(data, Appearance),
				],
			},
		};
	}

	public static UiElement Launcher(JsonElement data, IReadOnlyList<SteamGame> games)
	{
		ArgumentNullException.ThrowIfNull(games);

		var options = SteamWidgetSettings.ParseLauncher(data);
		var source = new UiState<string>(options.Source);
		var game = new UiState<string>(options.GameAppId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
		var count = new UiState<double>(options.Count);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "heading", Text = Heading.GameLauncher.Name() },
					new UiChoiceInput
					{
						Key = SteamWidgetType.SourceKey,
						Label = Strings.Source(),
						Binding = Bind.To(source),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							UiOption.Of(SteamWidgetType.RecentSource, Strings.SourceRecent()),
							UiOption.Of(SteamWidgetType.GameSource, Strings.SourceGame()),
						]),
					},
					new UiChoiceInput
					{
						Key = SteamWidgetType.GameKey,
						Label = Strings.Game(),
						Description = Strings.GameDescription(),
						Binding = Bind.To(game),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. games.Select(candidate => UiOption.Of(
								candidate.AppId.ToString(CultureInfo.InvariantCulture),
								candidate.Name)),
						]),
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = SteamWidgetType.SourceKey,
							Values = [SteamWidgetType.GameSource],
							SiblingValue = () => source.Value,
						},
					},
					new UiNumberInput
					{
						Key = SteamWidgetType.CountKey,
						Label = Strings.Count(),
						Description = Strings.CountDescription(),
						Binding = Bind.To(count),
						Min = SteamWidgetType.MinimumCount,
						Max = SteamWidgetType.MaximumCount,
						Step = 1,
						VisibleWhen = new UiVisibleWhen
						{
							ParameterName = SteamWidgetType.SourceKey,
							Values = [SteamWidgetType.RecentSource],
							SiblingValue = () => source.Value,
						},
					},
					UiWidgetAppearance.Section(data, Appearance),
				],
			},
		};
	}
}
