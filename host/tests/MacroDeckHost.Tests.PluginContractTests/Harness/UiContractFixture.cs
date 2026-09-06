using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>
/// A real host session broker in front of the contract fixture's plugin link, wired both ways: the host
/// reaches the plugin over <c>capability.invoke</c>, and the plugin reaches the host over
/// <c>host.invoke</c> through a real <see cref="PluginCallbackRouter" />.
/// </summary>
internal abstract class UiContractFixture : CapabilityContractFixture
{
	protected const string OwnerPrincipal = "device-a";

	private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(200);

	protected RecordingUiTransport UiTransport { get; private set; } = null!;

	protected UiSessionRegistry UiRegistry { get; private set; } = null!;

	protected UiSessionBroker Broker { get; private set; } = null!;

	/// <summary>The same coordinator the callback router registers a plugin's modal in, so a test can open
	/// one the way <c>show-modal</c> does rather than against a second instance.</summary>
	protected ModalInteractionCoordinator Modals { get; private set; } = null!;

	[SetUp]
	public void UiSetUp()
	{
		UiTransport = new RecordingUiTransport();
		UiRegistry = new UiSessionRegistry(Time);

		var resolver = new UiSessionProviderResolver(new RemoteUiProviderRegistry(SnapshotStore, Invoker),
			new UiProviderRegistry(IntegrationRegistry, () => Broker, Serilog.Core.Logger.None),
			new ConfigFlowUiProviderRegistry(() => Broker, Serilog.Core.Logger.None),
			new ActionConfigUiProviderRegistry(IntegrationRegistry, () => Broker, Serilog.Core.Logger.None),
			new WidgetUiProviderRegistry(new EmptyFolderCache(), [], () => Broker, Serilog.Core.Logger.None),
			new IntegrationUiProviderRegistry([], () => Broker, Serilog.Core.Logger.None),
			new UiPreviewProviderRegistry([], () => Broker, Serilog.Core.Logger.None));

		Broker = new UiSessionBroker(resolver,
			UiTransport,
			UiRegistry,
			SessionRegistry,
			IntegrationRegistry,
			Time,
			Serilog.Core.Logger.None);

		Modals = new ModalInteractionCoordinator(TimeProvider.System);

		var services = new ServiceCollection();
		services.AddSingleton<IIntegrationConfigStore>(new CallbackFakeConfigStore());
		services.AddSingleton<ISecretService>(new CallbackFakeSecretService());
		services.AddSingleton<IVariableService>(new CallbackFakeVariableService());
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var router = new PluginCallbackRouter(SessionRegistry,
			Invoker,
			scopeFactory,
			new CallbackFakeNotificationStore(),
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			Broker,
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			Modals,
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None);

		HostInvokeHandler = (correlationId, payload, cancellationToken)
			=> router.RouteAsync(PluginId, correlationId, payload, cancellationToken);
	}

	[TearDown]
	public void UiTearDown()
	{
		Broker.Dispose();
		UiRegistry.Dispose();
	}

	protected UiAttachSessionResponse Attach(string sessionId, string connectionId)
		=> Broker.Attach(sessionId, connectionId, OwnerPrincipal);

	protected static Task SettleAsync() => Task.Delay(_settle);

	protected static Task WaitForUiAsync(Func<bool> condition, string failureMessage)
		=> WaitForAsync(condition, failureMessage);

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
