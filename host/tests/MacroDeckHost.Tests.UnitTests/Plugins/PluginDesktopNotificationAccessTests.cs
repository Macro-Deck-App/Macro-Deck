using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginDesktopNotificationAccessTests
{
	private const string PluginId = "com.example.alerts";

	[Test]
	public async Task An_installed_plugin_without_the_declaration_is_refused_and_told_it_cannot_show_any()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed, [PluginPermissions.HostCompanionDevices]);

		var result = await world.ShowAsync("Hello", "World");
		await world.Pusher.PushAllAsync(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.DesktopNotificationsNotAllowed));
			Assert.That(world.Desktop.Shown, Is.Empty);
			Assert.That(world.PushedSupport(), Is.EqualTo(new[] { false }));
		});
	}

	[Test]
	public async Task An_installed_plugin_that_declares_the_permission_shows_through_the_hosts_desktop_notifications()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed, [PluginPermissions.HostDesktopNotifications]);

		var result = await world.ShowAsync("Build finished", "All green");
		await world.Pusher.PushAllAsync(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(world.Desktop.Shown, Is.EqualTo(new[] { ("Build finished", "All green") }));
			Assert.That(world.PushedSupport(), Is.EqualTo(new[] { true }));
		});
	}

	[Test]
	public async Task A_self_registered_session_may_show_them_without_a_declaration()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.SelfRegistered, permissions: null);

		var result = await world.ShowAsync("Hello", "World");

		Assert.That(result.Error, Is.Null);
	}

	[Test]
	public async Task A_declaring_plugin_on_a_computer_without_desktop_notifications_is_told_they_are_unsupported()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed,
			[PluginPermissions.HostDesktopNotifications],
			supported: false);

		await world.Pusher.PushAllAsync(PluginId);

		Assert.That(world.PushedSupport(), Is.EqualTo(new[] { false }));
	}

	[Test]
	public async Task A_show_without_arguments_is_an_invalid_payload()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed, [PluginPermissions.HostDesktopNotifications]);

		var result = await world.Callbacks.ExecuteAsync(new HostInvokePayload
		{
			Api = HostApis.DesktopNotifications,
			Operation = HostOperations.DesktopNotifications.Show
		}, CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	private sealed class World
	{
		private World(IReadOnlyList<string>? permissions, bool supported)
		{
			Desktop = new RecordingDesktopNotifier(supported);
			var policy = new PluginDesktopNotificationAccessPolicy(Sessions,
				new SingleInstalledPlugin(permissions),
				new FixedManifestReader(permissions));
			Callbacks = new PluginDesktopNotificationCallbacks(Desktop, policy);
			Pusher = new HostStatePusher(Sessions, new FakeDeckNavigator(), new FakeScriptApi(), new FakeWidgetApi(),
				new StubEventBindingTracker(), new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new FakeAdbManager(), new FixedAdbAccessPolicy(PluginAdbAccess.NotEnabled), Serilog.Core.Logger.None,
				desktopNotifications: Callbacks);
		}

		public PluginSessionRegistry Sessions { get; } = new(TimeProvider.System, Serilog.Core.Logger.None);

		public FakePluginConnection Connection { get; } = new();

		public RecordingDesktopNotifier Desktop { get; }

		public PluginDesktopNotificationCallbacks Callbacks { get; }

		public HostStatePusher Pusher { get; }

		public static async Task<World> ConnectAsync(PluginSessionOrigin origin,
			IReadOnlyList<string>? permissions,
			bool supported = true)
		{
			var world = new World(permissions, supported);
			await world.Sessions.Create(new PluginSessionRecord
			{
				SessionId = "session-1",
				PluginId = PluginId,
				DisplayName = "Alerts",
				Origin = origin,
				NegotiatedVersion = 1,
				Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
				DeclaredCapabilities = [],
				State = PluginSessionState.Awaiting,
				CreatedAt = TimeProvider.System.GetUtcNow()
			});
			world.Sessions.TryAttach("session-1", world.Connection, null);
			return world;
		}

		public async Task<HostCallbackResult> ShowAsync(string title, string message)
		{
			var payload = new HostInvokePayload
			{
				Api = HostApis.DesktopNotifications,
				Operation = HostOperations.DesktopNotifications.Show,
				Arguments = JsonSerializer.SerializeToElement(
					new DesktopNotificationShowArguments { Title = title, Message = message },
					PluginProtocolJson.Options)
			};

			return Callbacks.Admit(PluginId) ?? await Callbacks.ExecuteAsync(payload, CancellationToken.None);
		}

		public List<bool> PushedSupport()
			=> Connection.Sent.ToList()
				.Where(envelope => envelope.Type == MessageTypes.HostState)
				.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
				.Where(payload => payload.Api == HostApis.DesktopNotifications)
				.Select(payload => payload.Data!.Value
					.Deserialize<DesktopNotificationsStateDto>(PluginProtocolJson.Options)!.Supported)
				.ToList();
	}

	private sealed class RecordingDesktopNotifier(bool supported) : IDesktopNotifier
	{
		public List<(string Title, string Message)> Shown { get; } = [];

		public bool IsSupported => supported;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
		{
			Shown.Add((title, message));
			return Task.CompletedTask;
		}
	}

	private sealed class SingleInstalledPlugin(IReadOnlyList<string>? permissions) : IPluginInstallationCatalog
	{
		public IReadOnlyList<InstalledPlugin> Discover() => [];

		public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
		{
			version = permissions is null
				? null
				: new InstalledPluginVersion { Version = "1.0.0", VersionDirectory = "v", ManifestPath = "v/manifest.json" };
			return version is not null;
		}

		public void Invalidate()
		{
		}
	}

	private sealed class FixedManifestReader(IReadOnlyList<string>? permissions) : IPluginManifestReader
	{
		public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
			=> PluginManifestReadResult.Ok(new PluginManifest
			{
				ManifestVersion = 1,
				Id = expectedPluginId,
				Name = "Alerts",
				Version = expectedVersion,
				Entrypoints = new Dictionary<string, PluginEntrypoint>(),
				Permissions = permissions
			});

		public PluginManifestReadResult ReadFromJson(string json,
			string? versionDirectory,
			string expectedPluginId,
			string expectedVersion)
			=> throw new NotSupportedException();
	}
}
