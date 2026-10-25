using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.MusicPlayer;

[TestFixture]
internal sealed class MusicPlayerSystemIntegrationTests
{
	[Test]
	public async Task A_previously_disabled_album_colour_integration_starts_enabled_and_lists_its_variables()
	{
		var registry = await Registry(new Dictionary<string, bool> { [MusicPlayerIntegration.IntegrationId] = false });

		var provider = new VariableCatalogProviders(registry).ResolveOwner(MusicPlayerIntegration.IntegrationId);

		Assert.Multiple(() =>
		{
			Assert.That(registry.IsEnabled(MusicPlayerIntegration.IntegrationId), Is.True);
			Assert.That(provider?.Variables.Select(variable => variable.Name),
				Is.EqualTo(new[] { "music_player_spotify_album_color" }));
		});
	}

	[Test]
	public async Task The_album_colour_integration_cannot_be_turned_off_and_is_not_listed_as_an_integration()
	{
		var registry = await Registry(new Dictionary<string, bool>());
		registry.SetEnabled(MusicPlayerIntegration.IntegrationId, false);

		var listed = await new GetIntegrationsRequestMessageHandler(registry, null!, null!)
			.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(registry.IsEnabled(MusicPlayerIntegration.IntegrationId), Is.True);
			Assert.That(listed.Integrations.Select(integration => integration.Id),
				Does.Not.Contain(MusicPlayerIntegration.IntegrationId));
		});
	}

	[Test]
	public async Task Clients_receive_the_album_colour_integration_name_to_label_its_variables()
	{
		var registry = await Registry(new Dictionary<string, bool>());

		var listed = await new GetIntegrationsRequestMessageHandler(registry, null!, null!)
			.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		Assert.That(listed.SystemIntegrations.Single(entry => entry.Id == MusicPlayerIntegration.IntegrationId).Name,
			Is.EqualTo(new MusicPlayerIntegration().Name));
	}

	private static async Task<IntegrationRegistry> Registry(Dictionary<string, bool> stored)
	{
		var integration = new MusicPlayerIntegration();
		var players = new FixedMusicPlayerApi();
		players.Players.Add(new MusicPlayerInstanceSnapshot
		{
			Id = "spotify::default", ProviderId = "spotify", ProviderName = "Spotify", DisplayName = "Spotify"
		});
		await integration.InitializeAsync(FixedMusicPlayerApi.ContextWith(players));
		var registry = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new StateStore(stored),
			Serilog.Core.Logger.None);
		await registry.RegisterAsync(integration);
		return registry;
	}

	private sealed class StateStore(Dictionary<string, bool> states) : IIntegrationStateStore
	{
		public IReadOnlyDictionary<string, bool> Load()
		{
			return states;
		}

		public void Save(IReadOnlyDictionary<string, bool> updated)
		{
		}
	}
}
