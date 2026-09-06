using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

/// <summary>
/// What an out-of-process plugin says its actions are called has to arrive as a reference and be
/// resolved by whoever renders it, so a German reader sees German even though the plugin's author
/// wrote it in English. Before protocol v3 the plugin flattened it into its own language first, and a
/// German reader was stuck with the author's wording.
/// </summary>
[TestFixture]
internal sealed class RemotePluginDescriptorLocalizationTests
{
	private const string PluginId = "com.example.spotify";

	private static readonly LocalizationKey _playKey =
		new(LocalizationScope.ForPlugin(PluginId), "Actions.Play");

	private static LocalizationResolver Resolver()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(new LocalizationCatalog(LocalizationScope.ForPlugin(PluginId),
			"en",
			new Dictionary<string, IReadOnlyDictionary<string, string>>
			{
				["en"] = new Dictionary<string, string>
				{
					["Actions.Play"] = "Start playback", ["Parameters.Device"] = "Device"
				},
				["de"] = new Dictionary<string, string>
				{
					["Actions.Play"] = "Wiedergabe starten", ["Parameters.Device"] = "Gerät"
				}
			}));

		return new LocalizationResolver(registry);
	}

	private static ActionDescriptorDto Descriptor() => new()
	{
		LocalId = "play",
		Name = new LocalizedString(_playKey),
		Description = "Starts playback",
		Parameters =
		[
			new ActionParameterDto
			{
				Name = "device",
				Type = "String",
				Label = new LocalizedString(new LocalizationKey(LocalizationScope.ForPlugin(PluginId),
					"Parameters.Device"))
			}
		]
	};

	[Test]
	public void An_action_name_reaches_the_reader_in_the_readers_language()
	{
		var descriptor = ActionCatalogMapper.ToDomain(Descriptor());
		var resolver = Resolver();

		Assert.Multiple(() =>
		{
			Assert.That(resolver.Resolve(descriptor.Name, "de"), Is.EqualTo("Wiedergabe starten"));
			Assert.That(resolver.Resolve(descriptor.Name, "en"), Is.EqualTo("Start playback"));
			Assert.That(resolver.Resolve(descriptor.Parameters[0].Label, "de"), Is.EqualTo("Gerät"));
		});
	}

	[Test]
	public void The_action_the_host_builds_from_it_still_carries_the_reference()
	{
		var action = RemoteActionDefinitionFactory.Create(new ThrowingCapabilityInvoker(),
			PluginId,
			ActionCatalogMapper.ToDomain(Descriptor()));

		// Through the adapter, not just the mapper: the host resolves nothing itself, so a language
		// change re-renders from the catalog the client already holds instead of needing a refetch.
		Assert.That(Resolver().Resolve(action.Name, "de"), Is.EqualTo("Wiedergabe starten"));
	}

	[Test]
	public async Task A_persisted_snapshot_stores_the_reference_and_not_yesterdays_language()
	{
		var paths = new TestPaths();

		try
		{
			await new RemotePluginSnapshotStore(paths, Log.Logger).SaveAsync(
				RemotePluginCapabilitySnapshot.Empty(PluginId) with
				{
					Actions = [ActionCatalogMapper.ToDomain(Descriptor())]
				});

			// A snapshot written while the app was in English must not pin the picker to English on a
			// start where the reader has since switched - it is a cache of the plugin's answer, not of
			// how that answer happened to read once.
			var reloaded = new RemotePluginSnapshotStore(paths, Log.Logger).GetSnapshot(PluginId);

			Assert.That(Resolver().Resolve(reloaded.Actions[0].Name, "de"), Is.EqualTo("Wiedergabe starten"));
		}
		finally
		{
			paths.Cleanup();
		}
	}

	private sealed class ThrowingCapabilityInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("These tests never invoke the plugin.");

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => throw new NotSupportedException();

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
