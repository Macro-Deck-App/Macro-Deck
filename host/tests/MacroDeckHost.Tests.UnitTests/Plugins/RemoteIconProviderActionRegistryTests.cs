using System.Text.Json;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

/// <summary>
/// <see cref="RemoteIconProviderActionRegistry" /> is the standalone registry ADR 0056 (Consequences)
/// calls for instead of a ninth <c>RemoteActionDefinitionFactory</c> leaf - see the registry's own class
/// remarks. These tests exercise only its resolution logic against a snapshot store; the operations it
/// forwards once resolved are covered end to end by the plugin contract tests.
/// </summary>
[TestFixture]
public class RemoteIconProviderActionRegistryTests
{
	private const string PluginId = "com.example.plugin";

	private static readonly IPluginCapabilityInvoker _invoker = new ThrowingInvoker();

	private static RemoteActionDescriptor Descriptor(string localId, bool providesIcon)
		=> new(localId, "Action", string.Empty, [], null, false, false, false, providesIcon);

	[Test]
	public void An_unregistered_plugin_resolves_to_null()
	{
		var registry = new RemoteIconProviderActionRegistry(new InMemorySnapshotStore(),
			_invoker,
			new InMemoryPluginAssetCache());

		Assert.That(registry.Resolve(PluginId, "provide-icon"), Is.Null);
	}

	[Test]
	public void An_unknown_local_id_resolves_to_null()
	{
		var snapshots = new InMemorySnapshotStore();
		snapshots.Save(PluginId, [Descriptor("provide-icon", providesIcon: true)]);
		var registry = new RemoteIconProviderActionRegistry(snapshots, _invoker, new InMemoryPluginAssetCache());

		Assert.That(registry.Resolve(PluginId, "nope"), Is.Null);
	}

	[Test]
	public void An_action_that_does_not_provide_an_icon_resolves_to_null()
	{
		var snapshots = new InMemorySnapshotStore();
		snapshots.Save(PluginId, [Descriptor("play", providesIcon: false)]);
		var registry = new RemoteIconProviderActionRegistry(snapshots, _invoker, new InMemoryPluginAssetCache());

		Assert.That(registry.Resolve(PluginId, "play"), Is.Null);
	}

	[Test]
	public void An_icon_provider_action_resolves_to_the_same_cached_adapter()
	{
		var snapshots = new InMemorySnapshotStore();
		snapshots.Save(PluginId, [Descriptor("provide-icon", providesIcon: true)]);
		var registry = new RemoteIconProviderActionRegistry(snapshots, _invoker, new InMemoryPluginAssetCache());

		var first = registry.Resolve(PluginId, "provide-icon");
		var second = registry.Resolve(PluginId, "provide-icon");

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Not.Null);
			Assert.That(first, Is.InstanceOf<IIconProviderActionDefinition>());
			Assert.That(second, Is.SameAs(first));
		});
	}

	[Test]
	public void Two_actions_on_the_same_plugin_resolve_to_distinct_adapters()
	{
		var snapshots = new InMemorySnapshotStore();
		snapshots.Save(PluginId,
			[Descriptor("current-track", providesIcon: true), Descriptor("weather-icon", providesIcon: true)]);
		var registry = new RemoteIconProviderActionRegistry(snapshots, _invoker, new InMemoryPluginAssetCache());

		var trackIcon = registry.Resolve(PluginId, "current-track");
		var weatherIcon = registry.Resolve(PluginId, "weather-icon");

		Assert.That(trackIcon, Is.Not.SameAs(weatherIcon));
	}

	private sealed class ThrowingInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("Resolution alone never invokes the plugin.");

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope result)
			=> throw new NotSupportedException();

		public void AbortAll(string pluginId, MacroDeck.Plugin.Protocol.Errors.ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
	{
		private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

		public void Save(string pluginId, IReadOnlyList<RemoteActionDescriptor> actions)
			=> _byPluginId[pluginId] = RemotePluginCapabilitySnapshot.Empty(pluginId) with { Actions = actions };

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
}
