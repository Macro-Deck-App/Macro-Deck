using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// A device session across the real wire: the host opens it and pushes surfaces over
/// <c>capability.invoke</c>, and the plugin reports interactions, fetches icons and closes it over the
/// <c>devices</c> host api. Only the surface service behind the host router is a double - what these
/// prove is the transport, the session-ownership rule and what the serialized frame is allowed to say.
/// </summary>
[TestFixture]
internal sealed class DeviceSessionContractTests : CapabilityContractFixture
{
	private const string ProviderDeviceId = "SERIAL-1";

	private static readonly string[] _forbiddenKeys =
		["flows", "stateMapping", "secretHash", "createdAt", "order", "focusRules", "data"];

	private RecordingDeviceSurfaceService _surfaces = null!;
	private RemoteDeviceSessionRegistry _sessionOwners = null!;
	private RemoteDeviceProviderRegistry? _providers;
	private RecordingDeviceProvider _provider = null!;
	private Guid _deviceId;

	private static DeclaredCapability Provider(int maximum = 2)
		=> new()
		{
			Kind = CapabilityKinds.DeviceProvider,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = maximum }
		};

	private static DeviceDescriptor Deck()
		=> new(ProviderDeviceId,
			"Contract Deck",
			"Deck 3x2",
			"Example",
			ContractDeck.LayoutReference,
			new DeviceCapabilities { KeyCount = 6, SupportsImages = true });

	[TearDown]
	public void DeviceTearDown()
	{
		_providers?.Dispose();
		_providers = null;
	}

	[SetUp]
	public void DeviceSetUp()
	{
		_deviceId = Guid.NewGuid();
		_surfaces = new RecordingDeviceSurfaceService();
		_sessionOwners = new RemoteDeviceSessionRegistry();
		_provider = new RecordingDeviceProvider();

		var services = new ServiceCollection().BuildServiceProvider();
		var router = new PluginCallbackRouter(SessionRegistry,
			Invoker,
			services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			_surfaces,
			_sessionOwners,
			HostAssetSender);

		HostInvokeHandler = (_, payload, cancellationToken)
			=> router.RouteAsync(PluginId, Guid.NewGuid().ToString(), payload, cancellationToken);
	}

	private async Task<IDeviceSurfaceProvider> ConnectSessionCapablePluginAsync(int declaredMaximum = 2,
		int negotiatedVersion = 2)
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([_provider],
					TestMetadata.Default,
					CreatePluginHostInvoker(),
					PluginHostAssets)
			],
			[Provider(declaredMaximum)],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: negotiatedVersion);

		_providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, _sessionOwners);
		return _providers.Resolve(PluginId)!;
	}

	private static DeviceSurface SurfaceFor(Guid deviceId)
	{
		var projection = ContractDeck.Builder()
			.BuildAsync(ContractDeck.Device(deviceId, PluginId, ProviderDeviceId),
				null,
				ContractDeck.FolderId,
				CancellationToken.None)
			.GetAwaiter()
			.GetResult();

		return projection.Surface;
	}

	private JsonElement LastSurfaceFrame()
	{
		var frame = Link.SentByHost.Last(envelope
			=> string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal) &&
			envelope.Payload!.Value.GetRawText()
				.Contains(CapabilityOperations.DeviceProvider.SessionSurface, StringComparison.Ordinal));

		return frame.Payload!.Value;
	}

	[Test]
	public async Task A_session_opens_pushes_a_surface_and_carries_an_interaction_back()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();

		var accepted = await surfaceProvider.OpenAsync(
			new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);
		await surfaceProvider.PushAsync(_deviceId.ToString(), SurfaceFor(_deviceId), CancellationToken.None);

		var result = await _provider.Session!.SendInteractionAsync(new DeviceInteraction
		{
			Kind = DeviceInteractionKind.Press,
			Target = new DeviceInteractionTarget { WidgetId = ContractDeck.WidgetId.ToString() },
			SurfaceRevision = _provider.Session.CurrentSurface.Revision
		});

		var (routedDeviceId, interaction) = _surfaces.Interactions.Single();
		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.True);
			Assert.That(_provider.Session.DeviceId, Is.EqualTo(_deviceId.ToString()));
			Assert.That(_provider.Session.ProviderDeviceId, Is.EqualTo(ProviderDeviceId));
			Assert.That(_provider.Surfaces, Has.Count.EqualTo(1));
			Assert.That(_provider.Surfaces[0].Folder!.Id, Is.EqualTo(ContractDeck.FolderId));
			Assert.That(_provider.Surfaces[0].Layout.LayoutReference, Is.EqualTo(ContractDeck.LayoutReference));
			Assert.That(_provider.Surfaces[0].Widgets.Single().Id, Is.EqualTo(ContractDeck.WidgetId.ToString()));
			Assert.That(routedDeviceId, Is.EqualTo(_deviceId));
			Assert.That(interaction.Kind, Is.EqualTo(DeviceInteractionKind.Press));
			Assert.That(interaction.Target.WidgetId, Is.EqualTo(ContractDeck.WidgetId.ToString()));
			Assert.That(result.Status, Is.EqualTo(DeviceInteractionStatus.Accepted));
		});
	}

	[Test]
	public async Task A_refusal_comes_back_as_a_verdict_the_provider_can_act_on_rather_than_a_fault()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		_surfaces.NextOutcome = DeviceInteractionOutcome.Reject(DeviceSessionReasons.WidgetNotOnSurface);
		var rejected = await _provider.Session!.SendInteractionAsync(new DeviceInteraction
		{
			Kind = DeviceInteractionKind.Press, Target = new DeviceInteractionTarget { WidgetId = "gone" }
		});

		_surfaces.NextOutcome = DeviceInteractionOutcome.NotSupported;
		var unsupported = await _provider.Session.SendInteractionAsync(new DeviceInteraction
		{
			Kind = DeviceInteractionKind.EncoderTurn,
			Target = new DeviceInteractionTarget { WidgetId = ContractDeck.WidgetId.ToString() },
			Value = 3
		});

		Assert.Multiple(() =>
		{
			Assert.That(rejected.Status, Is.EqualTo(DeviceInteractionStatus.Rejected));
			Assert.That(rejected.ReasonCode, Is.EqualTo(DeviceSessionReasons.WidgetNotOnSurface));
			Assert.That(unsupported.Status, Is.EqualTo(DeviceInteractionStatus.NotSupported));
			Assert.That(_surfaces.Interactions[1].Interaction.Value, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task Disposing_the_session_closes_it_on_the_host_and_raises_closed_once()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		await _provider.Session!.DisposeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_surfaces.Closes.Select(close => close.DeviceId), Is.EqualTo(new[] { _deviceId }));
			Assert.That(_provider.CloseCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_host_closing_a_session_reaches_the_provider()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		await surfaceProvider.CloseAsync(_deviceId.ToString(), "device unplugged", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_provider.CloseCount, Is.EqualTo(1));
			Assert.That(_provider.CloseReason, Is.EqualTo("device unplugged"));
		});
	}

	[Test]
	public async Task A_session_id_belonging_to_another_plugin_addresses_nothing()
	{
		await ConnectSessionCapablePluginAsync();
		var foreignSessionId = Guid.NewGuid().ToString();
		_sessionOwners.Add(foreignSessionId, "com.example.other", Guid.NewGuid());

		var invoker = CreatePluginHostInvoker();
		var interaction = Assert.ThrowsAsync<HostInvocationException>(async () => await invoker.InvokeAsync(
			HostApis.Devices,
			HostOperations.Devices.Interaction,
			new DevicesInteractionArguments
			{
				SessionId = foreignSessionId, Kind = nameof(DeviceInteractionKind.Press), WidgetId = "w1"
			},
			CancellationToken.None));

		var close = Assert.ThrowsAsync<HostInvocationException>(async () => await invoker.InvokeAsync(HostApis.Devices,
			HostOperations.Devices.Close,
			new DevicesCloseArguments { SessionId = foreignSessionId },
			CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(interaction!.Code, Is.EqualTo(ProtocolErrorCodes.SessionNotFound));
			Assert.That(close!.Code, Is.EqualTo(ProtocolErrorCodes.SessionNotFound));
			Assert.That(_surfaces.Interactions, Is.Empty);
			Assert.That(_surfaces.Closes, Is.Empty);
		});
	}

	[Test]
	public async Task An_icon_too_large_for_one_message_arrives_intact_over_the_chunked_channel()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		var bytes = new byte[ProtocolLimits.MaxMessageBytes + 4096];
		Random.Shared.NextBytes(bytes);
		_surfaces.NextIcon = new DeviceIconImage
		{
			IconId = ContractDeck.IconId.ToString(),
			ContentType = "image/webp",
			ETag = "etag-1",
			Content = bytes,
			NotModified = false
		};

		var icon = await _provider.Session!.GetIconAsync(ContractDeck.IconId.ToString(), size: 144);

		Assert.Multiple(() =>
		{
			Assert.That(bytes, Has.Length.GreaterThan(ProtocolLimits.MaxMessageBytes));
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon!.NotModified, Is.False);
			Assert.That(icon.ETag, Is.EqualTo("etag-1"));
			Assert.That(icon.ContentType, Is.EqualTo("image/webp"));
			Assert.That(icon.Content.ToArray(), Is.EqualTo(bytes));
			Assert.That(AssetContentHash.Compute(icon.Content.Span),
				Is.EqualTo(AssetContentHash.Compute(bytes)));
			Assert.That(_surfaces.IconRequests.Single().Size, Is.EqualTo(144));
		});
	}

	[Test]
	public async Task A_cached_icon_is_answered_without_starting_a_transfer_at_all()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		_surfaces.NextIcon = new DeviceIconImage
		{
			IconId = ContractDeck.IconId.ToString(),
			ContentType = "image/webp",
			ETag = "etag-1",
			Content = ReadOnlyMemory<byte>.Empty,
			NotModified = true
		};

		var icon = await _provider.Session!.GetIconAsync(ContractDeck.IconId.ToString(),
			size: 72,
			knownETag: "etag-1");

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.True);
			Assert.That(icon.ETag, Is.EqualTo("etag-1"));
			Assert.That(icon.Content.Length, Is.EqualTo(0));
			Assert.That(_surfaces.IconRequests.Single().KnownETag, Is.EqualTo("etag-1"));
			Assert.That(Link.SentByHost.Any(envelope
					=> string.Equals(envelope.Type, MessageTypes.HostAssetBegin, StringComparison.Ordinal)),
				Is.False);
		});
	}

	[Test]
	public async Task A_widget_icon_too_large_for_one_message_arrives_intact_over_the_chunked_channel()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		var bytes = new byte[ProtocolLimits.MaxMessageBytes + 4096];
		Random.Shared.NextBytes(bytes);
		_surfaces.NextWidgetIcon = new DeviceWidgetIconImage
		{
			WidgetId = ContractDeck.WidgetId.ToString(),
			ContentType = "image/png",
			ETag = "sha256:v1",
			Content = bytes,
			NotModified = false
		};

		var icon = await _provider.Session!.GetWidgetIconAsync(ContractDeck.WidgetId.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(bytes, Has.Length.GreaterThan(ProtocolLimits.MaxMessageBytes));
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon!.NotModified, Is.False);
			Assert.That(icon.ETag, Is.EqualTo("sha256:v1"));
			Assert.That(icon.ContentType, Is.EqualTo("image/png"));
			Assert.That(icon.Content.ToArray(), Is.EqualTo(bytes));
			Assert.That(_surfaces.WidgetIconRequests.Single().WidgetId, Is.EqualTo(ContractDeck.WidgetId.ToString()));
		});
	}

	[Test]
	public async Task A_cached_widget_icon_is_answered_without_starting_a_transfer_at_all()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		_surfaces.NextWidgetIcon = new DeviceWidgetIconImage
		{
			WidgetId = ContractDeck.WidgetId.ToString(),
			ContentType = "image/png",
			ETag = "sha256:v1",
			Content = ReadOnlyMemory<byte>.Empty,
			NotModified = true
		};

		var icon = await _provider.Session!.GetWidgetIconAsync(ContractDeck.WidgetId.ToString(),
			knownETag: "sha256:v1");

		Assert.Multiple(() =>
		{
			Assert.That(icon!.NotModified, Is.True);
			Assert.That(icon.ETag, Is.EqualTo("sha256:v1"));
			Assert.That(icon.Content.Length, Is.EqualTo(0));
			Assert.That(_surfaces.WidgetIconRequests.Single().KnownETag, Is.EqualTo("sha256:v1"));
			Assert.That(Link.SentByHost.Any(envelope
					=> string.Equals(envelope.Type, MessageTypes.HostAssetBegin, StringComparison.Ordinal)),
				Is.False);
		});
	}

	[Test]
	public async Task A_widget_with_no_provider_icon_right_now_answers_with_nothing_rather_than_a_failure()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		// _surfaces.NextWidgetIcon is left null - the routine "nothing to serve right now" outcome, not a
		// protocol error (unlike the icon-pack Icon operation's "no such icon" case above).
		var icon = await _provider.Session!.GetWidgetIconAsync(ContractDeck.WidgetId.ToString());

		Assert.That(icon, Is.Null);
	}

	[Test]
	public async Task The_surface_frame_carries_the_resolved_appearance_and_none_of_the_stored_widget_data()
	{
		var surfaceProvider = await ConnectSessionCapablePluginAsync();
		await surfaceProvider.OpenAsync(new DeviceSurfaceSessionDescriptor(_deviceId.ToString(), ProviderDeviceId),
			CancellationToken.None);

		await surfaceProvider.PushAsync(_deviceId.ToString(), SurfaceFor(_deviceId), CancellationToken.None);

		var frame = LastSurfaceFrame();
		var keys = JsonFrame.Keys(frame).Select(key => key.ToLowerInvariant()).ToArray();
		var values = JsonFrame.StringValues(frame);

		Assert.Multiple(() =>
		{
			foreach (var forbidden in _forbiddenKeys)
			{
				Assert.That(keys, Does.Not.Contain(forbidden), $"the frame must not carry '{forbidden}'");
			}

			Assert.That(values, Does.Not.Contain(ContractDeck.WidgetData));
			Assert.That(values, Does.Not.Contain("sh_do_not_ship"));
			Assert.That(values, Does.Contain("Record"));
			Assert.That(values, Does.Contain(ContractDeck.IconId.ToString()));
			Assert.That(frame.GetRawText(), Does.Not.Contain("\"nodes\""));
		});
	}

	[Test]
	public async Task A_plugin_that_negotiated_the_pre_session_version_keeps_working_and_is_never_offered_a_session()
	{
		await ConnectAsync([
				new DeviceProviderCapabilityHandler([_provider],
					TestMetadata.Default,
					CreatePluginHostInvoker(),
					PluginHostAssets)
			],
			[Provider(maximum: 1)],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: 1);

		_providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, _sessionOwners);
		var resolved = _providers.Resolve(PluginId);

		var invoker = CreatePluginHostInvoker();
		var registration = await invoker.InvokeAsync(HostApis.Devices,
			HostOperations.Devices.Register,
			new DevicesRegisterArguments { Device = DeviceDescriptorDtoOf(Deck()) },
			CancellationToken.None);
		await invoker.InvokeAsync(HostApis.Devices,
			HostOperations.Devices.Update,
			new DevicesRegisterArguments { Device = DeviceDescriptorDtoOf(Deck()) },
			CancellationToken.None);
		await invoker.InvokeAsync(HostApis.Devices,
			HostOperations.Devices.Presence,
			new DevicesPresenceArguments
			{
				DeviceId = ProviderDeviceId, Presence = nameof(DevicePresence.Offline)
			},
			CancellationToken.None);
		await invoker.InvokeAsync(HostApis.Devices,
			HostOperations.Devices.Unregister,
			new DevicesUnregisterArguments { DeviceId = ProviderDeviceId },
			CancellationToken.None);

		var stillOnline = DeviceRegistry.IsOnline(PluginId, ProviderDeviceId);
		var sessionOpens = Link.SentByHost.Count(envelope
			=> string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal) &&
			envelope.Payload!.Value.GetRawText()
				.Contains(CapabilityOperations.DeviceProvider.SessionOpen, StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(resolved, Is.Null, "a version-1 plugin must not be served a device-session provider");
			Assert.That(registration?.Deserialize<DevicesRegisterResult>(PluginProtocolJson.Options)?.DeviceId,
				Is.Not.Null);
			Assert.That(DeviceRegistry.AssignedIdOf(PluginId, ProviderDeviceId), Is.Not.Null);
			Assert.That(stillOnline, Is.False, "unregistering must have taken the device offline");
			Assert.That(sessionOpens, Is.Zero);
			Assert.That(_provider.Session, Is.Null);
		});
	}

	private static DeviceDescriptorDto DeviceDescriptorDtoOf(DeviceDescriptor device)
		=> new()
		{
			Id = device.Id,
			Name = device.Name,
			Model = device.Model,
			Manufacturer = device.Manufacturer,
			LayoutReference = device.LayoutReference,
			Presence = device.Presence.ToString()
		};

	/// <summary>A plugin-side provider that keeps whatever session the host offers it, and every surface
	/// pushed to it.</summary>
	private sealed class RecordingDeviceProvider : IPluginIntegration, IDeviceProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public IDeviceSession? Session { get; private set; }

		public List<DeviceSurface> Surfaces { get; } = [];

		public int CloseCount { get; private set; }

		public string? CloseReason { get; private set; }

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName => "Contract Deck";

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public IReadOnlyList<DeviceDescriptor> GetDevices() => [Deck()];

		public Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			Session = session;
			session.SurfaceChanged += (_, args) => Surfaces.Add(args.Surface);
			session.Closed += (_, args) =>
			{
				CloseCount++;
				CloseReason = args.Reason;
			};

			return Task.CompletedTask;
		}
	}
}
