using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal static class NowPlayingScreenSaverConfigView
{
	public static UiElement Build(JsonElement stored, IMusicPlayerRegistry registry)
	{
		var instanceId = new UiState<string>(WidgetConfigJson.ReadString(stored, "instanceId") ?? string.Empty);

		UiOptionsState? instanceOptions = null;
		instanceOptions = new UiOptionsState(UiOptionSource.From((_, _)
			=> Task.FromResult<IReadOnlyList<UiOption>>(
				MusicPlayerWidgetConfigView.InstanceOptions(registry, instanceId.Peek()))));
		instanceOptions.Reload();

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

		return new UiConfigStack
		{
			Key = "root",
			Children =
			[
				registry.GetInstances().Count == 0
					? picker with { Description = AppStrings.Widgets.Music.NoProvidersHint() }
					: picker,
			],
		};
	}
}
