using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;

namespace MacroDeckHost.Tests.UnitTests;

public class ListUiPreviewsRequestMessageHandlerTests
{
	private sealed class FakeUiPreviewSource : IUiPreviewSource
	{
		public IReadOnlyList<MacroDeck.Sdk.Ui.UiSurfaceDeclaration> Surfaces { get; } = [];

		public IReadOnlyList<UiPreviewDescriptor> Previews { get; init; } = [];

		public IReadOnlyList<UiPreviewSkipped> Skipped { get; init; } = [];

		public Task<MacroDeck.Sdk.Ui.IUiSession?> CreateSessionAsync(MacroDeck.Sdk.Ui.UiSessionRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult<MacroDeck.Sdk.Ui.IUiSession?>(null);
	}

	private sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
	{
		private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

		public void Set(RemotePluginCapabilitySnapshot snapshot) => _byPluginId[snapshot.PluginId] = snapshot;

		public RemotePluginCapabilitySnapshot GetSnapshot(string pluginId)
			=> _byPluginId.TryGetValue(pluginId, out var snapshot)
				? snapshot
				: RemotePluginCapabilitySnapshot.Empty(pluginId);

		public bool Has(string pluginId) => _byPluginId.ContainsKey(pluginId);

		public Task SaveAsync(RemotePluginCapabilitySnapshot snapshot, CancellationToken cancellationToken = default)
		{
			_byPluginId[snapshot.PluginId] = snapshot;
			return Task.CompletedTask;
		}
	}

	[Test]
	public async Task Listing_merges_first_party_and_plugin_previews_with_distinct_ids()
	{
		var firstParty = new FakeUiPreviewSource
		{
			Previews =
			[
				new UiPreviewDescriptor
					{ Id = "app:Clock.Default", View = "Clock", Scenario = "Default", Profile = "widget" },
			],
			Skipped = [new UiPreviewSkipped { Member = "App.Bad.Method", Reason = "A preview method must be static." }],
		};

		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "com.example.plugin" });

		var snapshots = new InMemorySnapshotStore();
		snapshots.Set(RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			UiPreviews =
			[
				new RemoteUiPreviewDescriptor
					{ Id = "plugin:Clock.Default", View = "Clock", Scenario = "Default", Profile = "widget" },
			],
		});

		var handler = new ListUiPreviewsRequestMessageHandler([firstParty], registry, snapshots);

		var response = await handler.Handle(new ListUiPreviewsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Previews, Has.Count.EqualTo(2));
			Assert.That(response.Previews.Select(preview => preview.Id).Distinct().Count(),
				Is.EqualTo(2),
				"The first-party and plugin previews shared an id even though they share a view name.");

			var firstPartyEntry = response.Previews.Single(preview =>
				string.Equals(preview.Id, "app:Clock.Default", StringComparison.Ordinal));
			Assert.That(firstPartyEntry.OwnerId, Is.Empty);

			var pluginEntry = response.Previews.Single(preview =>
				string.Equals(preview.Id, "plugin:Clock.Default", StringComparison.Ordinal));
			Assert.That(pluginEntry.OwnerId, Is.EqualTo("com.example.plugin"));

			Assert.That(response.Diagnostics, Has.Count.EqualTo(1));
			Assert.That(response.Diagnostics[0].Member, Is.EqualTo("App.Bad.Method"));
		});
	}

	[Test]
	public async Task A_disconnected_plugin_contributes_no_previews()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Id = "com.example.plugin" });

		var handler = new ListUiPreviewsRequestMessageHandler([], registry, new InMemorySnapshotStore());

		var response = await handler.Handle(new ListUiPreviewsRequest(), CancellationToken.None);

		Assert.That(response.Previews, Is.Empty);
	}
}
