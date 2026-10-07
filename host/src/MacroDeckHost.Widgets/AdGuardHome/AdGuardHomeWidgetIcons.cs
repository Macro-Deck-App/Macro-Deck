using System.Reflection;
using System.Runtime.CompilerServices;
using MacroDeck.Sdk;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal sealed record AdGuardHomeIconResources(
	UiResource? Logo,
	UiResource Protected,
	UiResource Unprotected,
	IReadOnlyDictionary<string, UiResource> Statistics);

internal static class AdGuardHomeWidgetIcons
{
	private static readonly IReadOnlyDictionary<string, string> _statisticIcons = new Dictionary<string, string>
	{
		[AdGuardHomeWidgetType.DnsQueriesStatistic] = "dns-queries",
		[AdGuardHomeWidgetType.BlockedStatistic] = "blocked",
		[AdGuardHomeWidgetType.BlockedPercentageStatistic] = "blocked-percentage",
		[AdGuardHomeWidgetType.AverageProcessingTimeStatistic] = "processing-time",
		[AdGuardHomeWidgetType.SafeBrowsingStatistic] = "safe-browsing",
		[AdGuardHomeWidgetType.ParentalStatistic] = "parental",
		[AdGuardHomeWidgetType.SafeSearchStatistic] = "safe-search",
	};

	private static readonly ConditionalWeakTable<IUiResourceStore, Lazy<AdGuardHomeIconResources>> _registrations =
		new();

	public static AdGuardHomeIconResources EnsureRegistered(IUiResourceStore store, IIntegrationRegistry? integrations)
	{
		ArgumentNullException.ThrowIfNull(store);

		return _registrations.GetValue(store,
				registryStore => new Lazy<AdGuardHomeIconResources>(() => RegisterAll(registryStore, integrations)))
			.Value;
	}

	private static AdGuardHomeIconResources RegisterAll(IUiResourceStore store, IIntegrationRegistry? integrations)
	{
		var assembly = typeof(AdGuardHomeWidgetIcons).Assembly;

		UiResource Register(string name)
			=> store.Register(new UiResourceRegistration
			{
				OwnerId = AdGuardHomeWidgetType.OwnerId,
				Name = name,
				MediaType = "image/svg+xml",
				Content = ReadEmbeddedSvg(assembly, $".AdGuardHome.Icons.{name}.svg"),
			});

		var logo = integrations?.Integrations.FirstOrDefault(integration =>
			integration.Id == AdGuardHomeWidgetType.OwnerId) is IIntegrationIconProvider provider
			? store.Register(new UiResourceRegistration
			{
				OwnerId = AdGuardHomeWidgetType.OwnerId,
				Name = "logo",
				MediaType = provider.IconMimeType,
				Content = provider.GetIcon(),
			})
			: null;

		return new AdGuardHomeIconResources(logo,
			Register("protected"),
			Register("unprotected"),
			_statisticIcons.ToDictionary(pair => pair.Key, pair => Register(pair.Value), StringComparer.Ordinal));
	}

	private static byte[] ReadEmbeddedSvg(Assembly assembly, string suffix)
	{
		var resourceName = Array.Find(assembly.GetManifestResourceNames(),
				candidate => candidate.EndsWith(suffix, StringComparison.Ordinal)) ??
			throw new InvalidOperationException($"No embedded resource ending with '{suffix}'.");

		using var stream = assembly.GetManifestResourceStream(resourceName)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
