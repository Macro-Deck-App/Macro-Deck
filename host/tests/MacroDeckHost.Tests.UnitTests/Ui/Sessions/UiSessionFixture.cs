using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

internal abstract class UiSessionFixture
{
	protected const string ProviderId = "com.example.ui";

	protected const string DeviceA = "device-a";

	protected const string DeviceB = "device-b";

	/// <summary>Long enough that a real relay would have finished, short enough that a suite of these
	/// stays quick. Used only to prove that nothing arrives.</summary>
	private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(200);

	protected ManualTimeProvider Time { get; private set; } = null!;

	protected RecordingUiSessionTransport Transport { get; private set; } = null!;

	protected UiSessionRegistry Registry { get; private set; } = null!;

	protected StubUiSessionProviderResolver Resolver { get; private set; } = null!;

	protected StubIntegrationRegistry Integrations { get; private set; } = null!;

	protected ReplayablePluginSessionRegistry PluginSessions { get; private set; } = null!;

	protected UiSessionBroker Broker { get; private set; } = null!;

	protected RecordingCapabilityInvoker Invoker { get; private set; } = null!;

	[SetUp]
	public void UiSessionSetUp()
	{
		Time = new ManualTimeProvider();
		Transport = new RecordingUiSessionTransport();
		Registry = new UiSessionRegistry(Time);
		Resolver = new StubUiSessionProviderResolver();
		Integrations = new StubIntegrationRegistry();
		PluginSessions = new ReplayablePluginSessionRegistry(Time);

		Broker = new UiSessionBroker(Resolver,
			Transport,
			Registry,
			PluginSessions,
			Integrations,
			Time,
			Serilog.Core.Logger.None);

		Invoker = new RecordingCapabilityInvoker();
		Resolver.Fallback = new UiSessionProviderResolver(
			new RemoteUiProviderRegistry(new EmptyRemotePluginSnapshotStore(), Invoker),
			new UiProviderRegistry(Integrations, () => Broker, Serilog.Core.Logger.None),
			new ConfigFlowUiProviderRegistry(() => Broker, Serilog.Core.Logger.None),
			new ActionConfigUiProviderRegistry(Integrations, () => Broker, Serilog.Core.Logger.None),
			new WidgetUiProviderRegistry(new EmptyFolderCache(), [], () => Broker, Serilog.Core.Logger.None),
			new IntegrationUiProviderRegistry([], () => Broker, Serilog.Core.Logger.None),
			new UiPreviewProviderRegistry([], () => Broker, Serilog.Core.Logger.None));
	}

	[TearDown]
	public void UiSessionTearDown()
	{
		Broker.Dispose();
		Registry.Dispose();
	}

	protected StubUiSessionProvider AddProvider(string providerId = ProviderId, int treeRevision = 1)
	{
		var provider = new StubUiSessionProvider(providerId)
		{
			Sink = Broker, Snapshot = () => UiPayloads.Tree(treeRevision)
		};

		Resolver.Add(provider);
		return provider;
	}

	/// <summary>Registers an in-process integration that serves UI trees. Nothing else changes: the same
	/// broker, resolver and transport serve it through the same session API a plugin is served through.</summary>
	protected StubUiSession AddInProcessProvider(Func<UiTree> tree, string providerId = ProviderId)
	{
		var session = new StubUiSession { Tree = tree };
		Integrations.Add(new StubUiIntegration(providerId) { Session = session });
		return session;
	}

	protected static UiTree TreeAt(int revision, string sessionMode = UiSessionModes.Exclusive)
		=> new()
		{
			Revision = revision,
			Surface = Surface(sessionMode),
			Root = new UiNode { Id = "root", Type = "panel" }
		};

	protected static UiSurface Surface(string sessionMode = UiSessionModes.Exclusive,
		string kind = UiSurfaceKinds.Config)
		=> new() { Kind = kind, SessionMode = sessionMode };

	/// <summary>Opens a session and waits for the provider's first tree, so a test that is not about the
	/// open handshake starts from a session a client can meaningfully attach to.</summary>
	protected Task<string> OpenAsync(StubUiSessionProvider provider,
		string sessionMode = UiSessionModes.Exclusive,
		string principal = DeviceA)
		=> OpenAsync(provider.ProviderId, sessionMode, principal);

	protected async Task<string> OpenAsync(string providerId,
		string sessionMode = UiSessionModes.Exclusive,
		string principal = DeviceA)
	{
		var ticket = await Broker.OpenAsync(providerId, Surface(sessionMode), principal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, "The session did not open.");
		return ticket.SessionId;
	}

	protected UiAttachSessionResponse Attach(string sessionId, string connectionId, string principal = DeviceA)
		=> Broker.Attach(sessionId, connectionId, principal);

	/// <summary>Registers a live plugin session for a provider id, so a UI session opened afterwards is
	/// bound to it the way a plugin-served session is.</summary>
	protected async Task<string> RegisterPluginSessionAsync(string pluginId = ProviderId)
	{
		var sessionId = Guid.CreateVersion7().ToString("D");

		await PluginSessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
			DeclaredCapabilities = [],
			State = PluginSessionState.Connected,
			CreatedAt = Time.GetUtcNow()
		});

		return sessionId;
	}

	protected static async Task WaitForAsync(Func<bool> condition, string failureMessage)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

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

	/// <summary>Waits out the window in which a message could still arrive, for an assertion whose point
	/// is that none does.</summary>
	protected static Task SettleAsync() => Task.Delay(_settle);

	protected Task WaitForMessagesAsync(string connectionId, int count, string failureMessage)
		=> WaitForAsync(() => Transport.For(connectionId).Count >= count, failureMessage);

	protected IReadOnlyList<object> MessagesFor(string connectionId)
		=> [.. Transport.For(connectionId).Select(recorded => recorded.Message)];

	protected IReadOnlyList<T> MessagesFor<T>(string connectionId)
		where T : class
		=> [.. Transport.For(connectionId).Select(recorded => recorded.Message).OfType<T>()];

	protected sealed class EmptyFolderCache : IFolderCache
	{
		public List<FolderEntity> GetAllFolders() => [];
		public FolderEntity? GetFolderById(Guid id) => null;
		public Task InitializeCache() => Task.CompletedTask;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;
		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}
}
