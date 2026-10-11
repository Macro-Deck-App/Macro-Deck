using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.MusicPlayer;
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

	private static async Task<IntegrationRegistry> Registry(Dictionary<string, bool> stored)
	{
		var integration = new MusicPlayerIntegration();
		integration.UseAlbumColor(new FixedAlbumColor());
		var registry = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new StateStore(stored),
			Serilog.Core.Logger.None);
		await registry.RegisterAsync(integration);
		return registry;
	}

	private sealed class StateStore(Dictionary<string, bool> states) : IIntegrationStateStore
	{
		public IReadOnlyDictionary<string, bool> Load() => states;

		public void Save(IReadOnlyDictionary<string, bool> updated)
		{
		}
	}

	private sealed class FixedAlbumColor : IMusicPlayerAlbumColor
	{
		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
			=> [new MusicPlayerInstanceDescriptor("spotify::default", "spotify", "Spotify", "Spotify", false)];

		public Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>("#102030");
	}
}
