using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// Wires the real production classes for the dynamic-variable pipeline - registry, variable service,
/// binding service, subscription coordinator, both background services - around fakes only at the two true
/// external boundaries: the integration registry and the provider itself. Assembled once per test so a
/// scenario can drive bind/unbind/restart/reconcile exactly the way the host does.
/// </summary>
internal sealed class VariableCatalogHarness
{
	public VariableRegistry Registry { get; } = new();

	public RecordingMediator Mediator { get; } = new();

	public InMemoryVariableBindingStore BindingStore { get; } = new();

	public ConfigurableIntegrationRegistry Integrations { get; }

	public VariableCatalogProviders Providers { get; }

	public VariableNameFactory NameFactory { get; }

	public VariableUpdateChannel Channel { get; } = new();

	public VariableCatalogInvalidationSignal Invalidation { get; } = new();

	public VariableRefreshSignal Refresh { get; } = new();

	public VariableSubscriptionCoordinator Coordinator { get; }

	public VariableBindingService BindingService { get; }

	public IVariableService VariableService { get; }

	public IServiceScopeFactory ScopeFactory { get; }

	public VariableCatalogUpdateBackgroundService UpdateService { get; }

	public VariableBindingRestoreBackgroundService BindingBackgroundService { get; }

	public StartupReadiness Readiness { get; } = new();

	public VariableCatalogHarness(params IIntegration[] integrations)
	{
		Integrations = new ConfigurableIntegrationRegistry(integrations);
		Providers = new VariableCatalogProviders(Integrations);
		NameFactory = new VariableNameFactory(Registry);

		var services = new ServiceCollection();
		services.AddSingleton(Registry);
		services.AddSingleton<IUserVariableStore>(new NullUserVariableStore());
		services.AddSingleton<IMediator>(Mediator);
		services.AddSingleton(Providers);
		services.AddSingleton<IVariableRefreshSignal>(Refresh);
		services.AddSingleton<IMusicPlayerPollNudge>(new MusicPlayerPollNudge(Integrations));
		services.AddScoped<IVariableService, VariableService>();
		var provider = services.BuildServiceProvider();
		ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

		VariableService = ScopeFactory.CreateScope().ServiceProvider.GetRequiredService<IVariableService>();

		Coordinator = new VariableSubscriptionCoordinator(Providers,
			BindingStore,
			Channel,
			Invalidation,
			ScopeFactory,
			Log.Logger);

		BindingService = new VariableBindingService(Providers,
			BindingStore,
			VariableService,
			Registry,
			NameFactory,
			Coordinator,
			Mediator,
			new VariableBindingLookup(Registry, BindingStore));

		UpdateService = new VariableCatalogUpdateBackgroundService(new StartedHostLifetime(),
			ScopeFactory,
			Channel,
			Registry,
			BindingStore,
			Providers,
			Refresh,
			Log.Logger);

		BindingBackgroundService = new VariableBindingRestoreBackgroundService(new StartedHostLifetime(),
			ScopeFactory,
			BindingStore,
			NameFactory,
			Coordinator,
			Readiness,
			Log.Logger);
	}
}
