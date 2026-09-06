using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Capabilities.FolderViewProvider;
using MacroDeck.Plugin.Hosting.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Hosting.Capabilities.WidgetTypeProvider;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Hosting.Capabilities.Issues;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Hosting.Capabilities.Migration;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Weather;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MacroDeck.Plugin.Hosting.DependencyInjection;

/// <summary>
/// Registers the things a plugin offers.
///
/// <para>
/// Internal on purpose: <c>PluginHostBuilder.RegisterIntegration</c> and
/// <c>PluginHostBuilder.RegisterCapabilityHandler</c> are the author-facing door, and there is only one.
/// Registering an integration straight onto the <see cref="IServiceCollection" /> skipped the actions
/// handler and every capability handler its type implies, which is a mistake that only shows up as a
/// capability silently missing from the host - MDP2004 exists because of it.
/// </para>
/// </summary>
internal static class MacroDeckServiceCollectionExtensions
{
	/// <summary>
	/// Registers an integration, and with it the actions it declares plus a handler for every other
	/// capability kind the integration's type implements - <c>variables</c> for
	/// <see cref="IVariableProvider" />, <c>events</c> for <see cref="IEventProvider" />, <c>issues</c>
	/// for <see cref="IIntegrationIssueProvider" />.
	///
	/// <para>
	/// Singleton, because <see cref="IPluginIntegration" />'s lifecycle is explicitly the process: it is
	/// initialized once, holds whatever connections it needs, and is shut down once. Constructor
	/// injection works as usual, so an integration can take <c>IHttpClientFactory</c>,
	/// <c>IOptions&lt;T&gt;</c> or <c>ILogger&lt;T&gt;</c>.
	/// </para>
	/// </summary>
	public static IServiceCollection AddMacroDeckIntegration<TIntegration>(this IServiceCollection services)
		where TIntegration : class, IPluginIntegration
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton<TIntegration>();
		services.AddSingleton<IPluginIntegration>(provider => provider.GetRequiredService<TIntegration>());

		return services.AddActionsCapability().AddCapabilitiesFor<TIntegration>();
	}

	/// <summary>
	/// Registers an integration built by <paramref name="factory" />, for the cases construction is not
	/// just dependency injection - a value read from configuration, a client the plugin already owns.
	/// </summary>
	public static IServiceCollection AddMacroDeckIntegration<TIntegration>(
		this IServiceCollection services,
		Func<IServiceProvider, TIntegration> factory)
		where TIntegration : class, IPluginIntegration
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(factory);

		services.TryAddSingleton(factory);
		services.AddSingleton<IPluginIntegration>(provider => provider.GetRequiredService<TIntegration>());

		return services.AddActionsCapability().AddCapabilitiesFor<TIntegration>();
	}

	/// <summary>
	/// Registers a handler for one capability kind. This is the seam every capability beyond actions
	/// plugs into: the transport and the dispatcher never learn about a specific kind.
	/// </summary>
	public static IServiceCollection AddMacroDeckCapabilityHandler<THandler>(this IServiceCollection services)
		where THandler : class, ICapabilityHandler
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSingleton<ICapabilityHandler, THandler>();
		return services;
	}

	/// <summary>
	/// Adds the actions and ui handlers once, however many integrations are registered. Registering
	/// either per integration would declare every action, or the ui capability, once per integration.
	///
	/// <para>
	/// Unlike <c>variables</c>/<c>events</c>/<c>issues</c> below, whether the <c>ui</c> capability has
	/// anything to declare is not decided by a registered type at all: an
	/// <see cref="MacroDeck.Sdk.Actions.IUiConfigurableActionDefinition" /> action or an
	/// <see cref="MacroDeck.Sdk.ConfigFlow.IUiConfigFlowProvider" /> flow can make it so without the
	/// integration implementing <see cref="IUiProvider" /> at all - that is instance data
	/// <see cref="UiCapabilityHandler" /> reads for itself, not type data this method could gate on.
	/// Registering it here and declaring nothing still costs nothing.
	/// </para>
	/// </summary>
	private static IServiceCollection AddActionsCapability(this IServiceCollection services)
	{
		services.TryAddSingleton<PluginConfigFlowSessions>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, ActionsCapabilityHandler>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, UiCapabilityHandler>());

		return services;
	}

	/// <summary>
	/// Adds the variables/events/issues handlers conditionally, one time each, based on which
	/// SDK-declared provider interfaces <typeparamref name="TIntegration" />'s own type implements -
	/// unlike actions, these handlers cost every declared integration a describe/list round trip on
	/// their kind, so a type that implements none of the three interfaces should never see one
	/// registered on its behalf. <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)" />
	/// still guards against a duplicate registration when several integrations share a capability kind.
	///
	/// <para>
	/// There is no <c>icons</c> branch here: the icon is the manifest's, not an integration's, so its
	/// handler is registered by the builder against what <c>manifest.json</c> declares.
	/// </para>
	/// </summary>
	private static IServiceCollection AddCapabilitiesFor<TIntegration>(this IServiceCollection services)
		where TIntegration : class, IPluginIntegration
	{
		if (typeof(IVariableProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			// VariableSubscriptions belongs to the same handler, so it is registered with it rather than
			// on its own condition: SupportsCatalog is an instance property with a default implementation
			// and nothing here has an instance to ask. A plugin with no variable provider at all still
			// builds with no trace of either.
			services.TryAddSingleton<VariableSubscriptions>();
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, VariablesCapabilityHandler>());
		}

		if (typeof(IEventProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, EventsCapabilityHandler>());
		}

		if (typeof(IIntegrationIssueProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, IssuesCapabilityHandler>());
		}

		if (typeof(IMusicPlayerProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, MusicPlayerCapabilityHandler>());
		}

		if (typeof(IWeatherProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, WeatherCapabilityHandler>());
		}

		if (typeof(IMigrationProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, MigrationCapabilityHandler>());
		}

		if (typeof(IProfileProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<ICapabilityHandler, VirtualProfilesCapabilityHandler>());
		}

		if (typeof(IDeviceProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<ICapabilityHandler, DeviceProviderCapabilityHandler>());
		}

		if (typeof(ILayoutProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(
				ServiceDescriptor.Singleton<ICapabilityHandler, LayoutProviderCapabilityHandler>());
		}

		if (typeof(IFolderViewProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<ICapabilityHandler, FolderViewProviderCapabilityHandler>());
		}

		if (typeof(IWidgetTypeProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor
				.Singleton<ICapabilityHandler, WidgetTypeProviderCapabilityHandler>());
		}

		if (typeof(IConfigFlowProvider).IsAssignableFrom(typeof(TIntegration)))
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, ConfigFlowCapabilityHandler>());
		}

		return services;
	}
}
