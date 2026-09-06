using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

/// <summary>Builds the real <see cref="VariableService"/> for a test that only cares about the three
/// collaborators it has always taken, defaulting the owner-dispatch ones ADR 0081 added to an empty
/// integration set - a write to an integration-owned variable then reports its owner unavailable, which
/// is what a test that never registered one means.</summary>
internal static class TestVariableServices
{
	/// <summary>Registers the real scoped <see cref="VariableService"/> together with the owner-dispatch
	/// collaborators it needs, so a test container only has to supply the registry, store and mediator it
	/// already cared about.</summary>
	public static IServiceCollection AddTestVariableService(
		this IServiceCollection services,
		IIntegrationRegistry? integrations = null)
	{
		var owners = integrations ?? new ConfigurableIntegrationRegistry([]);

		return services
			.AddSingleton(new VariableCatalogProviders(owners))
			.AddSingleton<IVariableRefreshSignal>(new VariableRefreshSignal())
			.AddSingleton<IMusicPlayerPollNudge>(new MusicPlayerPollNudge(owners))
			.AddScoped<IVariableService, VariableService>();
	}

	public static VariableService Create(
		VariableRegistry registry,
		IUserVariableStore store,
		IMediator mediator,
		IIntegrationRegistry? integrations = null)
	{
		var owners = integrations ?? new ConfigurableIntegrationRegistry([]);

		return new VariableService(registry,
			store,
			mediator,
			new VariableCatalogProviders(owners),
			new VariableRefreshSignal(),
			new MusicPlayerPollNudge(owners));
	}
}
