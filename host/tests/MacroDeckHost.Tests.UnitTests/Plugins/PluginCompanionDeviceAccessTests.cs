using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.CompanionApp;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginCompanionDeviceAccessTests
{
	private const string PluginId = "com.example.phones";
	private static readonly Guid _phone = Guid.Parse("33333333-3333-3333-3333-333333333333");

	[Test]
	public async Task Plugins_may_not_use_companion_devices_until_the_user_allows_it_even_with_the_declaration()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostCompanionDevices] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginCompanionDeviceAccess.NotAllowed));
	}

	[Test]
	public async Task Once_allowed_an_installed_plugin_needs_the_declaration()
	{
		var declaring = new World { InstalledPermissions = [PluginPermissions.HostCompanionDevices] };
		var adbOnly = new World { InstalledPermissions = [PluginPermissions.HostAdb] };
		await declaring.ConnectAsync(PluginSessionOrigin.Managed);
		await adbOnly.ConnectAsync(PluginSessionOrigin.Managed);
		await declaring.Policy.SetAllowPluginsAsync(true);
		await adbOnly.Policy.SetAllowPluginsAsync(true);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await declaring.Policy.EvaluateAsync(PluginId),
				Is.EqualTo(PluginCompanionDeviceAccess.Available));
			Assert.That(await adbOnly.Policy.EvaluateAsync(PluginId),
				Is.EqualTo(PluginCompanionDeviceAccess.NotAllowed));
		});
	}

	[Test]
	public async Task A_self_registered_session_is_allowed_without_a_declaration_once_plugins_are_allowed()
	{
		var world = new World { InstalledPermissions = null };
		await world.ConnectAsync(PluginSessionOrigin.SelfRegistered);
		await world.Policy.SetAllowPluginsAsync(true);

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginCompanionDeviceAccess.Available));
	}

	[Test]
	public async Task The_choice_is_stored_and_a_change_is_announced_once()
	{
		var world = new World();
		var changes = 0;
		world.Policy.Changed += () => changes++;

		await world.Policy.SetAllowPluginsAsync(true);
		await world.Policy.SetAllowPluginsAsync(true);

		Assert.Multiple(() =>
		{
			Assert.That(world.Preferences.Values[PluginCompanionDeviceAccessPolicy.AllowPluginsKey],
				Is.EqualTo("true"));
			Assert.That(changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_stored_choice_is_read_back()
	{
		var world = new World();
		world.Preferences.Values[PluginCompanionDeviceAccessPolicy.AllowPluginsKey] = "true";

		Assert.That(await world.Policy.GetAllowPluginsAsync(), Is.True);
	}

	[Test]
	public async Task A_read_in_flight_when_the_host_shuts_down_still_completes()
	{
		var preferences = new GatedPreferences();
		preferences.Values[PluginCompanionDeviceAccessPolicy.AllowPluginsKey] = "true";
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceRepository>(preferences);
		services.AddSingleton<IPluginSessionRegistry>(new PluginSessionRegistry(TimeProvider.System,
			Serilog.Core.Logger.None));
		services.AddSingleton<IPluginInstallationCatalog>(new SingleInstalledPlugin([]));
		services.AddSingleton<IPluginManifestReader>(new FixedManifestReader([]));
		services.AddSingleton<IPluginCompanionDeviceAccessPolicy, PluginCompanionDeviceAccessPolicy>();
		var host = services.BuildServiceProvider();

		var read = host.GetRequiredService<IPluginCompanionDeviceAccessPolicy>().GetAllowPluginsAsync();
		await preferences.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		await host.DisposeAsync();
		preferences.Open();

		Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
	}

	[Test]
	public async Task Installing_a_plugin_that_declares_companion_devices_while_plugins_may_not_use_them_asks_the_user()
	{
		var world = new World();

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Phone Tools", true, false);

		var notification = world.Store.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(notification.SourceId, Is.EqualTo(PluginId));
			Assert.That(notification.Actions.Select(action => action.Kind),
				Does.Contain(UserNotificationActionKind.EnablePluginCompanionDevices));
			Assert.That(notification.Message, Does.Contain("Phone Tools"));
		});
	}

	[TestCase(true, false, false)]
	[TestCase(false, true, false)]
	[TestCase(false, false, true)]
	public async Task Nothing_is_asked_when_there_is_nothing_to_decide(
		bool allowPlugins,
		bool previouslyDeclared,
		bool declaresNothing)
	{
		var world = new World();
		await world.Policy.SetAllowPluginsAsync(allowPlugins);

		await world.Notifier.NotifyIfNeededAsync(PluginId, "Phone Tools", !declaresNothing, previouslyDeclared);

		Assert.That(world.Store.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task A_refused_plugin_is_asked_about_once_per_run_and_allowing_clears_the_question()
	{
		var world = new World();

		await world.Notifier.AskAfterRefusalAsync(PluginId, "Phone Tools");
		world.Store.Dismiss(world.Store.Snapshot().Single().Id);
		await world.Notifier.AskAfterRefusalAsync(PluginId, "Phone Tools");
		var afterSecondRefusal = world.Store.Snapshot().Count;
		await world.Notifier.NotifyIfNeededAsync("com.example.other", "Other", true, false);
		world.Notifier.DismissAll();

		Assert.Multiple(() =>
		{
			Assert.That(afterSecondRefusal, Is.Zero);
			Assert.That(world.Store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_call_from_a_plugin_that_is_not_allowed_is_refused_and_the_user_is_asked()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostCompanionDevices] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);

		var refusal = await world.Callbacks().AdmitAsync(PluginId, Send(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(refusal?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CompanionDevicesNotAllowed));
			Assert.That(world.Store.Snapshot().Single().Actions.Select(action => action.Kind),
				Does.Contain(UserNotificationActionKind.EnablePluginCompanionDevices));
		});
	}

	[Test]
	public async Task A_plugin_without_the_declaration_is_refused_without_asking()
	{
		var world = new World { InstalledPermissions = [] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);
		await world.Policy.SetAllowPluginsAsync(true);

		var refusal = await world.Callbacks().AdmitAsync(PluginId, Send(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(refusal?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CompanionDevicesNotAllowed));
			Assert.That(world.Store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task An_allowed_plugin_reaches_the_device_with_the_command_it_sent()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostCompanionDevices] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);
		await world.Policy.SetAllowPluginsAsync(true);
		world.Devices.Connect(_phone, new CompanionDeviceState { ConfirmsCommands = true });
		world.Devices.RequestResult = CompanionDeviceCommandResult.Unconfirmed;
		var callbacks = world.Callbacks();

		var admitted = await callbacks.AdmitAsync(PluginId, Send(), CancellationToken.None);
		var sent = await callbacks.ExecuteAsync(PluginId, Send(), CancellationToken.None);
		var requested = await callbacks.ExecuteAsync(PluginId,
			Payload(HostOperations.CompanionDevices.Request,
				new CompanionDeviceCommandArguments
				{
					DeviceId = _phone.ToString("D"), Command = CompanionDeviceCommands.SetBrightness,
					BrightnessPercent = 30
				}),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(admitted, Is.Null);
			Assert.That(Read<CompanionDeviceSendResultDto>(sent)?.Sent, Is.True);
			Assert.That(Read<CompanionDeviceRequestResultDto>(requested)?.Outcome,
				Is.EqualTo(CompanionDeviceOutcomes.Unconfirmed));
			Assert.That(world.Devices.Calls,
				Is.EqualTo(new[]
				{
					("send", _phone, (CompanionDeviceCommand?)CompanionDeviceCommand.Vibrate),
					("request", _phone, (CompanionDeviceCommand?)CompanionDeviceCommand.SetBrightness(30))
				}));
		});
	}

	[Test]
	public async Task A_command_the_devices_do_not_understand_is_an_invalid_payload()
	{
		var world = new World();

		var result = await world.Callbacks()
			.ExecuteAsync(PluginId,
				Payload(HostOperations.CompanionDevices.Send,
					new CompanionDeviceCommandArguments { DeviceId = _phone.ToString("D"), Command = "cancelRequest" }),
				CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public async Task A_screenshot_is_announced_in_the_reply_and_sent_over_the_host_asset_channel()
	{
		byte[] png = [137, 80, 78, 71];
		var world = new World
		{
			Devices =
			{
				ScreenshotResult = CompanionScreenshotResult.Of(png)
			}
		};

		var result = await world.Callbacks().ExecuteAsync(PluginId, Screenshot("full"), CancellationToken.None);
		var announced = Read<CompanionScreenshotResultDto>(result);
		var sent = await world.Assets.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(announced?.Failure, Is.Null);
			Assert.That(announced?.ByteLength, Is.EqualTo(png.Length));
			Assert.That(announced?.ContentHash, Is.EqualTo(AssetContentHash.Compute(png)));
			Assert.That(sent, Is.EqualTo((PluginId, announced?.TransferId, AssetKinds.CompanionScreenshot)));
			Assert.That(world.Devices.Calls.Single().Operation, Is.EqualTo("screenshot:FullScreen"));
		});
	}

	[Test]
	public async Task A_screenshot_the_user_declined_on_the_device_reports_consent_denied()
	{
		var world = new World
		{
			Devices =
			{
				ScreenshotResult = CompanionScreenshotResult.Failed(CompanionDeviceFailure.ConsentDenied)
			}
		};

		var result = await world.Callbacks().ExecuteAsync(PluginId, Screenshot("full"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Read<CompanionScreenshotResultDto>(result)?.Failure,
				Is.EqualTo(CompanionDeviceFailures.ConsentDenied));
			Assert.That(world.Assets.Sent.Task.IsCompleted, Is.False);
		});
	}

	private static HostInvokePayload Send()
	{
		return Payload(HostOperations.CompanionDevices.Send,
			new CompanionDeviceCommandArguments
				{ DeviceId = _phone.ToString("D"), Command = CompanionDeviceCommands.Vibrate });
	}

	private static HostInvokePayload Screenshot(string mode)
	{
		return Payload(HostOperations.CompanionDevices.Screenshot,
			new CompanionScreenshotArguments { DeviceId = _phone.ToString("D"), Mode = mode });
	}

	private static HostInvokePayload Payload(string operation, object arguments)
	{
		return new HostInvokePayload
		{
			Api = HostApis.CompanionDevices,
			Operation = operation,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
		};
	}

	private static T? Read<T>(HostCallbackResult result)
	{
		return result.Data is { } data
			? data.Deserialize<T>(PluginProtocolJson.Options)
			: default;
	}

	private sealed class World
	{
		private readonly Lazy<PluginCompanionDeviceAccessPolicy> _policy;
		private readonly Lazy<PluginCompanionDeviceConsentNotifier> _notifier;
		private readonly IServiceScopeFactory _scopeFactory;

		public World()
		{
			var services = new ServiceCollection();
			services.AddSingleton<IAppPreferenceRepository>(Preferences);
			services.AddScoped<IAppPreferenceService>(_ => new FakeAdbPreferenceService());
			_scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
			_policy = new Lazy<PluginCompanionDeviceAccessPolicy>(() => new PluginCompanionDeviceAccessPolicy(Sessions,
				new SingleInstalledPlugin(InstalledPermissions),
				new FixedManifestReader(InstalledPermissions),
				_scopeFactory));
			_notifier = new Lazy<PluginCompanionDeviceConsentNotifier>(() =>
				new PluginCompanionDeviceConsentNotifier(Store, _scopeFactory, TestLocalization.Resolver, Policy));
		}

		public IReadOnlyList<string>? InstalledPermissions { get; init; } = [];

		public CompanionAppServiceTests.MemoryPreferences Preferences { get; } = new();

		public PluginSessionRegistry Sessions { get; } = new(TimeProvider.System, Serilog.Core.Logger.None);

		public UserNotificationStore Store { get; } = new();

		public ScriptedCompanionDevices Devices { get; } = new();

		public RecordingHostAssetSender Assets { get; } = new();

		public PluginCompanionDeviceAccessPolicy Policy => _policy.Value;

		public PluginCompanionDeviceConsentNotifier Notifier => _notifier.Value;

		public PluginCompanionDeviceCallbacks Callbacks()
		{
			return new PluginCompanionDeviceCallbacks(Devices, Policy, Notifier, Sessions, Assets);
		}

		public async Task ConnectAsync(PluginSessionOrigin origin)
		{
			await Sessions.Create(new PluginSessionRecord
			{
				SessionId = "session-1",
				PluginId = PluginId,
				DisplayName = "Phone Tools",
				Origin = origin,
				NegotiatedVersion = 1,
				Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
				DeclaredCapabilities = [],
				State = PluginSessionState.Awaiting,
				CreatedAt = TimeProvider.System.GetUtcNow()
			});
			Sessions.TryAttach("session-1", new FakePluginConnection(), null);
		}
	}

	private sealed class GatedPreferences : CompanionAppServiceTests.MemoryPreferences
	{
		private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void Open()
		{
			_open.TrySetResult();
		}

		public override async Task<AppPreferenceEntity?> GetByKey(string key)
		{
			Entered.TrySetResult();
			await _open.Task;
			return await base.GetByKey(key);
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
				Name = "Phone Tools",
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

	internal sealed class RecordingHostAssetSender : IPluginHostAssetSender
	{
		public TaskCompletionSource<(string PluginId, string? TransferId, string Kind)> Sent { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<bool> SendAsync(
			string pluginId,
			string transferId,
			string kind,
			string mimeType,
			ReadOnlyMemory<byte> content,
			string contentHash,
			CancellationToken cancellationToken)
		{
			Sent.TrySetResult((pluginId, transferId, kind));
			return Task.FromResult(true);
		}

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope ack)
		{
			return false;
		}

		public void DropSession(string pluginId)
		{
		}
	}
}
