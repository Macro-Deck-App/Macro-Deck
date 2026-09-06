using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeck.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Mediator;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal abstract class CapabilityContractFixture
{
	protected const string PluginId = "com.example.contract";

	protected ManualTimeProvider Time { get; private set; } = null!;

	protected PluginSessionRegistry SessionRegistry { get; private set; } = null!;

	protected IIntegrationRegistry IntegrationRegistry { get; private set; } = null!;

	protected IPluginCapabilityInvoker Invoker => _invoker;

	protected RecordingNotificationStore Notifications { get; private set; } = null!;

	protected RecordingMediator Mediator { get; private set; } = null!;

	private PluginCapabilityInvoker _invoker = null!;

	private InMemoryPluginLink _link = null!;
	private PluginConnectionState _pluginConnectionState = null!;
	private PluginHostAssetReceiver _pluginHostAssets = null!;
	private IHostInvoker _pluginHostInvoker = null!;
	private CapabilityDispatcher _dispatcher = null!;
	private PluginAssetUploader _assetUploader = null!;
	private Task<ConnectionOutcome>? _connectionRun;
	private string _sessionId = string.Empty;

	protected RemotePluginIntegrationRegistrar Registrar { get; private set; } = null!;

	protected InMemorySnapshotStore SnapshotStore { get; private set; } = null!;

	protected InMemoryPluginAssetCache AssetCache { get; private set; } = null!;

	protected PluginAssetReceiver AssetReceiver { get; private set; } = null!;

	protected RemotePluginSnapshotRefresher SnapshotRefresher { get; private set; } = null!;

	protected RecordingDeviceRegistry DeviceRegistry { get; private set; } = null!;

	/// <summary>The host-to-plugin chunked asset channel, the way icon bytes actually reach a plugin.</summary>
	protected PluginHostAssetSender HostAssetSender { get; private set; } = null!;

	/// <summary>The plugin-side reassembler a device session's <c>GetIconAsync</c> claims its bytes from.</summary>
	internal IPluginHostAssetReceiver PluginHostAssets => _pluginHostAssets;

	private PluginSessionConnection? _connection;

	[SetUp]
	public void BaseSetUp()
	{
		Time = new ManualTimeProvider();
		SnapshotStore = new InMemorySnapshotStore();
		AssetCache = new InMemoryPluginAssetCache();
		SessionRegistry = new PluginSessionRegistry(Time, Serilog.Core.Logger.None);

		// Created here rather than in ConnectAsync so a handler that needs a plugin-side IHostInvoker -
		// UiCapabilityHandler is the one so far - can be built before the connection exists. HostInvoker
		// only reads this state's ActiveConnection when a call is actually made, so an as-yet-unconnected
		// instance is exactly what production DI hands the same handler at container build time.
		_pluginConnectionState = new PluginConnectionState();
		_pluginHostInvoker = new HostInvoker(_pluginConnectionState, TimeProvider.System, Serilog.Core.Logger.None);
		_pluginHostAssets = new PluginHostAssetReceiver();
		HostAssetSender = new PluginHostAssetSender(SessionRegistry, TimeProvider.System, Serilog.Core.Logger.None);

		Mediator = new RecordingMediator();
		var services = new ServiceCollection();
		services.AddSingleton<IMediator>(Mediator);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		IntegrationRegistry = new IntegrationRegistry(scopeFactory,
			new InMemoryIntegrationStateStore(),
			Serilog.Log.Logger);
		_invoker = new PluginCapabilityInvoker(SessionRegistry, Time, Serilog.Core.Logger.None);
		Notifications = new RecordingNotificationStore();
		AssetReceiver = new PluginAssetReceiver(AssetCache);
		SnapshotRefresher = new RemotePluginSnapshotRefresher(Invoker, SnapshotStore);
		DeviceRegistry = new RecordingDeviceRegistry();

		Registrar = new RemotePluginIntegrationRegistrar(SessionRegistry,
			IntegrationRegistry,
			SnapshotStore,
			SnapshotRefresher,
			Invoker,
			new PluginSessionConnectionState(SessionRegistry),
			AssetCache,
			AssetReceiver,
			new EmptyInstallationCatalog(),
			new NeverFindsManifest(),
			Notifications,
			scopeFactory,
			new LocalizationCatalogRegistry(),
			DeviceRegistry,
			new LayoutRegistry(Mediator),
			new FolderViewRegistry(Mediator),
			new WidgetTypeRegistry(Mediator),
			Time,
			Serilog.Log.Logger);
	}

	[TearDown]
	public async Task BaseTearDown()
	{
		Registrar.Dispose();

		if (_connectionRun is not null)
		{
			CapabilityOperationCoverage.RecordFrom(_link.SentByHost);

			await _link.DisposeAsync();
			await _connectionRun;
			_connectionRun.Dispose();

			if (_connection is not null)
			{
				await _connection.DisposeAsync();
			}
		}

		_invoker.Dispose();
		_dispatcher.Dispose();
	}

	protected InMemoryPluginLink Link => _link;

	/// <summary>A plugin-side <c>IHostInvoker</c> a handler in the list given to <see cref="ConnectAsync" />
	/// can be built with, so a handler such as <c>UiCapabilityHandler</c> that takes one as a constructor
	/// argument - the same shape production DI hands it - can push <c>host.invoke</c> once the connection
	/// this fixture opens is live. The same instance is wired into <see cref="PluginSessionConnection" />'s
	/// own <c>hostInvoker</c> parameter in <see cref="ConnectAsync" />, which is what completes a pending
	/// call once the host's <c>host.result</c> comes back - without that wiring the call hangs until its
	/// own protocol timeout, since nothing ever calls <see cref="IHostInvoker.TryComplete" /> on it.</summary>
	protected IHostInvoker CreatePluginHostInvoker() => _pluginHostInvoker;

	/// <summary>Set before <see cref="ConnectAsync" /> to route the plugin's <c>host.invoke</c> calls into
	/// a real host callback router, so a contract test can exercise both directions of an exchange.</summary>
	protected Func<string, HostInvokePayload, CancellationToken, Task<HostCallbackResult>>? HostInvokeHandler
	{
		get;
		set;
	}

	protected string SessionId => _sessionId;

	protected async Task<IIntegration> ConnectAsync(
		IReadOnlyList<ICapabilityHandler> handlers,
		IReadOnlyList<DeclaredCapability> declaredCapabilities,
		IReadOnlyCollection<string> acceptedKinds,
		Func<Task>? beforeRegister = null,
		int negotiatedCapabilityVersion = 1)
	{
		_sessionId = Guid.CreateVersion7().ToString("D");

		var negotiated = acceptedKinds.ToDictionary(kind => kind,
			kind => CapabilityNegotiationResult.Accept(kind, negotiatedCapabilityVersion),
			StringComparer.Ordinal);

		var record = new PluginSessionRecord
		{
			SessionId = _sessionId,
			PluginId = PluginId,
			DisplayName = "Contract Plugin",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = negotiated,
			DeclaredCapabilities = declaredCapabilities,
			State = PluginSessionState.Awaiting,
			CreatedAt = Time.GetUtcNow()
		};

		await SessionRegistry.Create(record);

		_link = new InMemoryPluginLink(_sessionId)
		{
			AssetReceiver = AssetReceiver,
			AssetPluginId = PluginId,
			HostInvokeHandler = HostInvokeHandler,
			HostAssetAckHandler = ack => HostAssetSender.TryComplete(PluginId, ack)
		};
		_link.CapabilityResultReceived += envelope => Invoker.TryComplete(PluginId, envelope);
		_link.StateUpdateHandler = async (kind, cancellationToken) =>
		{
			var result = await SnapshotRefresher.RefreshKindAsync(PluginId, kind, cancellationToken)
				.ConfigureAwait(false);
			if (result.AllSucceeded)
			{
				await Registrar.ApplyRefreshedSnapshotAsync(PluginId, result.Snapshot, cancellationToken)
					.ConfigureAwait(false);
			}
		};

		if (!SessionRegistry.TryAttach(_sessionId, _link, "contract-fixture"))
		{
			throw new InvalidOperationException("The session could not be attached.");
		}

		// Built before the dispatcher, not after: a handler that resolves IPluginAssetUploader from the
		// invocation's own scope (ActionsCapabilityHandler.GetActionIconContentAsync is the one so far)
		// must see the same instance UploadAssetAsync uses, or an upload made through the handler would
		// never reach this fixture's real asset receiver/cache - see PluginTestSession.Dispatcher's
		// identical reasoning for IHostInvoker.
		_assetUploader = new PluginAssetUploader(_pluginConnectionState,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		_dispatcher = PluginTestSession.Dispatcher(handlers,
			_pluginConnectionState,
			TimeProvider.System,
			_pluginHostInvoker,
			_assetUploader);

		var session = PluginTestSession.Create(_sessionId, negotiatedVersion: 1, Time.GetUtcNow());
		_connection = new PluginSessionConnection(_link,
			session,
			_dispatcher,
			_pluginConnectionState,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker: _pluginHostInvoker,
			hostStateCache: null,
			_assetUploader,
			_pluginHostAssets);

		_pluginConnectionState.ActiveConnection = _connection;

		_connectionRun = _connection.RunAsync(resumeSessionId: null,
			instanceId: "contract-fixture",
			CancellationToken.None);

		await WaitForAsync(() => _pluginConnectionState.IsReady);

		if (beforeRegister is not null)
		{
			await beforeRegister();
		}

		var registered = await Registrar.RegisterAsync(PluginId, CancellationToken.None);
		if (!registered)
		{
			throw new InvalidOperationException("The registrar rejected this contract fixture's plugin.");
		}

		return IntegrationRegistry.Integrations.Single(integration
			=> string.Equals(integration.Id, PluginId, StringComparison.Ordinal));
	}

	protected Task<JsonElement?> InvokeRawAsync(string kind,
		string localId,
		string operation,
		object? arguments = null,
		CancellationToken cancellationToken = default)
		=> Invoker.InvokeAsync(PluginId,
			new CapabilityInvokeRequest
				{ Kind = kind, LocalId = localId, Operation = operation, Arguments = arguments },
			cancellationToken);

	protected Task<string> UploadAssetAsync(string kind,
		string mimeType,
		byte[] data,
		CancellationToken cancellationToken = default)
		=> _assetUploader.UploadAsync(kind, mimeType, data, cancellationToken);

	protected Task SendStateUpdateFromPluginAsync(string kind,
		string? localId = null,
		CancellationToken cancellationToken = default)
		=> _connection!.SendAsync(new ProtocolEnvelope
			{
				Type = MessageTypes.StateUpdate,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(new StateUpdatePayload { Kind = kind, LocalId = localId },
					PluginProtocolJson.Options)
			},
			cancellationToken).AsTask();

	protected Task SendLogPublishFromPluginAsync(IReadOnlyList<LogEventDto> events,
		int? dropped = null,
		CancellationToken cancellationToken = default)
		=> _connection!.SendAsync(new ProtocolEnvelope
			{
				Type = MessageTypes.LogPublish,
				Id = Guid.CreateVersion7().ToString(),
				Payload = JsonSerializer.SerializeToElement(
					new LogPublishPayload { Events = events, Dropped = dropped },
					PluginProtocolJson.Options)
			},
			cancellationToken).AsTask();

	protected void Disconnect() => SessionRegistry.Detach(_sessionId, Time.GetUtcNow());

	private static Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
		=> WaitForAsync(condition, "The plugin session never became ready.", timeout);

	protected static async Task WaitForAsync(Func<bool> condition, string failureMessage, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail(failureMessage);
	}
}

internal sealed class InMemorySnapshotStore : IRemotePluginSnapshotStore
{
	private readonly Dictionary<string, RemotePluginCapabilitySnapshot> _byPluginId = new(StringComparer.Ordinal);

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
