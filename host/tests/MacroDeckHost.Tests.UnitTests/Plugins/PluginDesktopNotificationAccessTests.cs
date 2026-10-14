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
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.Notifications.Shell;
using MacroDeckHost.Platform.Notifications;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Delegation;
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
		var world = await World.ConnectAsync(PluginSessionOrigin.SelfRegistered, null);

		var result = await world.ShowAsync("Hello", "World");

		Assert.That(result.Error, Is.Null);
	}

	[Test]
	public async Task A_declaring_plugin_on_a_computer_without_desktop_notifications_is_told_they_are_unsupported()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed,
			[PluginPermissions.HostDesktopNotifications],
			false);

		await world.Pusher.PushAllAsync(PluginId);

		Assert.That(world.PushedSupport(), Is.EqualTo(new[] { false }));
	}

	[Test]
	public async Task A_connected_plugin_learns_when_the_desktop_shell_comes_and_goes_where_the_platform_cannot_notify()
	{
		var time = new FakeTimeProvider();
		using var shell = new ShellNotificationBridge(time);
		var notifier = new PlatformDesktopNotifier(new Lazy<IPlatformNotificationService>(() =>
			new ShellFirstNotificationService(shell, new UnsupportedPlatformNotifications())));
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed,
			[PluginPermissions.HostDesktopNotifications],
			notifier: notifier,
			shell: shell);
		await world.Pusher.PushAllAsync(PluginId);

		await PollAsync(shell);
		await world.WaitForPushesAsync(2);
		await PollAsync(shell);
		time.Advance(ShellNotificationBridge.AttachmentWindow);
		await world.WaitForPushesAsync(3);

		Assert.That(world.PushedSupport(), Is.EqualTo(new[] { false, true, false }));
	}

	[Test]
	public async Task A_show_without_arguments_is_an_invalid_payload()
	{
		var world = await World.ConnectAsync(PluginSessionOrigin.Managed, [PluginPermissions.HostDesktopNotifications]);

		var result = await world.Callbacks.ExecuteAsync(new HostInvokePayload
			{
				Api = HostApis.DesktopNotifications,
				Operation = HostOperations.DesktopNotifications.Show
			},
			CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	private static async Task PollAsync(ShellNotificationBridge shell)
	{
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await shell.WaitAsync(TimeSpan.FromSeconds(20), abandoned.Token);
	}

	private sealed class World
	{
		private World(
			IReadOnlyList<string>? permissions,
			bool supported,
			IDesktopNotifier? notifier,
			IShellNotificationBridge? shell)
		{
			Desktop = new RecordingDesktopNotifier(supported);
			var policy = new PluginDesktopNotificationAccessPolicy(Sessions,
				new SingleInstalledPlugin(permissions),
				new FixedManifestReader(permissions));
			Callbacks = new PluginDesktopNotificationCallbacks(notifier ?? Desktop, policy);
			Pusher = new HostStatePusher(Sessions,
				new FakeDeckNavigator(),
				new FakeScriptApi(),
				new FakeWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new FakeAdbManager(),
				new FixedAdbAccessPolicy(PluginAdbAccess.NotEnabled),
				Serilog.Core.Logger.None,
				desktopNotifications: Callbacks,
				shellNotifications: shell);
		}

		public PluginSessionRegistry Sessions { get; } = new(TimeProvider.System, Serilog.Core.Logger.None);

		public FakePluginConnection Connection { get; } = new();

		public RecordingDesktopNotifier Desktop { get; }

		public PluginDesktopNotificationCallbacks Callbacks { get; }

		public HostStatePusher Pusher { get; }

		public static async Task<World> ConnectAsync(
			PluginSessionOrigin origin,
			IReadOnlyList<string>? permissions,
			bool supported = true,
			IDesktopNotifier? notifier = null,
			IShellNotificationBridge? shell = null)
		{
			var world = new World(permissions, supported, notifier, shell);
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

		public async Task WaitForPushesAsync(int count)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (DateTime.UtcNow < deadline && PushedSupport().Count < count)
			{
				await Task.Delay(10);
			}
		}

		public List<bool> PushedSupport()
		{
			return Connection.Sent.ToList()
				.Where(envelope => envelope.Type == MessageTypes.HostState)
				.Select(envelope => envelope.Payload!.Value.Deserialize<HostStatePayload>(PluginProtocolJson.Options)!)
				.Where(payload => payload.Api == HostApis.DesktopNotifications)
				.Select(payload => payload.Data!.Value
					.Deserialize<DesktopNotificationsStateDto>(PluginProtocolJson.Options)!.Supported)
				.ToList();
		}
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

	private sealed class UnsupportedPlatformNotifications : IPlatformNotificationService
	{
		public bool IsSupported => false;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class SingleInstalledPlugin(IReadOnlyList<string>? permissions) : IPluginInstallationCatalog
	{
		public IReadOnlyList<InstalledPlugin> Discover()
		{
			return [];
		}

		public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
		{
			version = permissions is null
				? null
				: new InstalledPluginVersion
					{ Version = "1.0.0", VersionDirectory = "v", ManifestPath = "v/manifest.json" };
			return version is not null;
		}

		public void Invalidate()
		{
		}
	}

	private sealed class FixedManifestReader(IReadOnlyList<string>? permissions) : IPluginManifestReader
	{
		public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
		{
			return PluginManifestReadResult.Ok(new PluginManifest
			{
				ManifestVersion = 1,
				Id = expectedPluginId,
				Name = "Alerts",
				Version = expectedVersion,
				Entrypoints = new Dictionary<string, PluginEntrypoint>(),
				Permissions = permissions
			});
		}

		public PluginManifestReadResult ReadFromJson(
			string json,
			string? versionDirectory,
			string expectedPluginId,
			string expectedVersion)
		{
			throw new NotSupportedException();
		}
	}
}
