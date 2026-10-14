using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Integrations.AdGuardHome.Widgets;

internal sealed record AdGuardHomeIconResources(
	UiResource? Logo,
	UiResource? Protected,
	UiResource? Unprotected,
	IReadOnlyDictionary<string, UiResource> Statistics)
{
	public static readonly AdGuardHomeIconResources None = new(null, null, null, new Dictionary<string, UiResource>());

	public UiResource? Statistic(string statistic)
	{
		return Statistics.GetValueOrDefault(statistic);
	}
}

internal static class AdGuardHomeWidgetIcons
{
	public const string IconFolder = "widget";

	private static readonly IReadOnlyDictionary<string, string> _statisticIcons = new Dictionary<string, string>
	{
		[AdGuardHomeWidgetType.DnsQueriesStatistic] = "dns-queries",
		[AdGuardHomeWidgetType.BlockedStatistic] = "blocked",
		[AdGuardHomeWidgetType.BlockedPercentageStatistic] = "blocked-percentage",
		[AdGuardHomeWidgetType.AverageProcessingTimeStatistic] = "processing-time",
		[AdGuardHomeWidgetType.SafeBrowsingStatistic] = "safe-browsing",
		[AdGuardHomeWidgetType.ParentalStatistic] = "parental",
		[AdGuardHomeWidgetType.SafeSearchStatistic] = "safe-search"
	};

	public static async Task<AdGuardHomeIconResources> ResolveAsync(
		IUiResourceRegistry? resources,
		CancellationToken cancellationToken)
	{
		if (resources is null)
		{
			return AdGuardHomeIconResources.None;
		}

		var statistics = new Dictionary<string, UiResource>(StringComparer.Ordinal);
		foreach (var (statistic, name) in _statisticIcons)
		{
			if (await IconAsync(resources, name, cancellationToken).ConfigureAwait(false) is { } icon)
			{
				statistics[statistic] = icon;
			}
		}

		return new AdGuardHomeIconResources(await IconAsync(resources, "logo", cancellationToken).ConfigureAwait(false),
			await IconAsync(resources, "protected", cancellationToken).ConfigureAwait(false),
			await IconAsync(resources, "unprotected", cancellationToken).ConfigureAwait(false),
			statistics);
	}

	private static async Task<UiResource?> IconAsync(
		IUiResourceRegistry resources,
		string name,
		CancellationToken cancellationToken)
	{
		try
		{
			return await resources.GetPluginIconAsync(IconFolder, name, cancellationToken).ConfigureAwait(false);
		}
		catch (UiResourceException)
		{
			return null;
		}
	}
}
