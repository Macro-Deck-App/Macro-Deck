using MacroDeckHost.Infrastructure.Backups.Storage;
using MacroDeckHost.Infrastructure.Backups.Retention;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Backups.Retention;
using MacroDeckHost.Application.Backups;
using System.Text.Json.Serialization;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeckHost.Logging;
using MacroDeck.Plugin.Packaging;
using MacroDeck.Plugin.Protocol;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Infrastructure.Connect;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Devices.Surfaces.InProcess;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Infrastructure.ClientTargets;
using MacroDeckHost.Infrastructure.Applications;
using MacroDeckHost.Infrastructure.Autostart;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Deck;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Icons.AppIcons;
using MacroDeckHost.Infrastructure.Lifecycle;
using MacroDeckHost.Infrastructure.HostLocking;
using MacroDeckHost.Infrastructure.MusicPlayer;
using MacroDeckHost.Infrastructure.Network.Discovery;
using MacroDeckHost.Infrastructure.Network.Tls;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Infrastructure.Migration;
using MacroDeckHost.Infrastructure.Migration.MacroDeck2;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Infrastructure.Rendering;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.OptionsSources;
using MacroDeckHost.Infrastructure.Triggers;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Infrastructure.Secrets;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Integrations;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Jobs;
using MacroDeckHost.Infrastructure.Plugins.Trust;
using MacroDeckHost.Auth;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Plugins;
using MacroDeckHost.Ui;
using MacroDeckHost.Widgets.ActionButton;
using MacroDeckHost.Widgets.Clock;
using MacroDeckHost.Widgets.HistoryGraph;
using MacroDeckHost.Widgets.MusicPlayer;
using MacroDeckHost.Widgets.DeveloperPreviews;
using MacroDeckHost.Widgets.Preview;
using MacroDeckHost.Widgets.Slider;
using MacroDeckHost.Widgets.Weather;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Extensions;
using Mediator;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Plugins.Pairing;

namespace MacroDeckHost;

public class Startup
{
	public void ConfigureServices(IServiceCollection services)
	{
		var paths = new MacroDeckPaths();
		services.AddSingleton<IMacroDeckPaths>(paths);
		services.AddHttpClient();

		services.Configure<JsonOptions>(options =>
		{
			options.SerializerOptions.DefaultIgnoreCondition
				= JsonIgnoreCondition.WhenWritingNull;
		});
		services.AddEndpointsApiExplorer();
		services.AddCors(options =>
		{
			// Deliberately without AllowCredentials: the auth cookies must never be sent
			// cross-origin; API calls authenticate via the Authorization header instead.
			options.AddPolicy("AllowAny",
				builder =>
				{
					builder.SetIsOriginAllowed(_ => true)
						.AllowAnyMethod()
						.AllowAnyHeader();
				});
		});
		if (KeyRingStartupState.IsLocked)
		{
			// FileSigningKeyProvider regenerates and overwrites keys/auth-signing.key whenever it cannot
			// unprotect it, so leaving it registered would destroy the real key on the way past a lock
			// that is meant to be temporary.
			services.AddSingleton<ISigningKeyProvider, EphemeralSigningKeyProvider>();
		}
		else
		{
			services.AddSingleton<ISigningKeyProvider, FileSigningKeyProvider>();
		}

		services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
		services.AddAuthentication(AuthDefaults.PolicySchemeName)
			.AddPolicyScheme(AuthDefaults.PolicySchemeName,
				"Loopback or JWT",
				options =>
				{
					options.ForwardDefaultSelector = context => LoopbackConnection.IsTrusted(context)
						? AuthDefaults.LoopbackScheme
						: JwtBearerDefaults.AuthenticationScheme;
				})
			.AddScheme<AuthenticationSchemeOptions, LoopbackAuthenticationHandler>(AuthDefaults.LoopbackScheme, null)
			.AddScheme<AuthenticationSchemeOptions, PluginSessionAuthenticationHandler>(PluginAuthSchemes.PluginSession,
				null)
			.AddJwtBearer();

		var adminPolicy = new AuthorizationPolicyBuilder()
			.RequireAuthenticatedUser()
			.RequireClaim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope)
			.Build();
		services.AddAuthorizationBuilder()
			.AddPolicy(AuthPolicies.Admin, adminPolicy)
			.AddPolicy(AuthPolicies.ClientAccess,
				policy =>
				{
					policy.RequireAuthenticatedUser()
						.RequireClaim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope, AuthDefaults.ClientScope);
				})
			.SetDefaultPolicy(adminPolicy)
			.SetFallbackPolicy(adminPolicy);
		services.AddProblemDetails();
		services.AddControllers();
		services.AddUiTransport(typeof(GetVersionRequestMessageHandler).Assembly);

		// Overrides AddUiTransport's default scoped registration: this handler tracks the last
		// announced version and the in-flight download across calls, and a scoped instance would
		// forget both between requests.
		services
			.AddSingleton<IUiTransportMessageHandler<ReportUpdateStateRequest, ReportUpdateStateResponse>,
				ReportUpdateStateRequestMessageHandler>();

		services.AddSingleton<UiSessionRegistry>();
		services.AddSingleton<IUiResourceStore, UiResourceStore>();
		services.AddSingleton<RemoteUiProviderRegistry>();
		services.AddSingleton<RemoteIconProviderActionRegistry>();
		services.AddSingleton(provider => new UiProviderRegistry(provider.GetRequiredService<IIntegrationRegistry>(),
			provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton(provider => new ConfigFlowUiProviderRegistry(provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton(provider => new ActionConfigUiProviderRegistry(
			provider.GetRequiredService<IIntegrationRegistry>(),
			provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IWeatherStateNotifier, WeatherStateNotifier>();
		services.AddSingleton<IWidgetSampleTextResolver, WidgetSampleTextResolver>();
		services.AddSingleton<IBuiltInWidgetUiProvider, WeatherWidgetUiProvider>();
		services.AddSingleton<IBuiltInWidgetUiProvider, ClockWidgetUiProvider>();
		services.AddSingleton<IWidgetIconSourceRegistry, WidgetIconSourceRegistry>();
		services.AddSingleton<IWidgetIconResources, WidgetIconResources>();
		services.AddSingleton<IWidgetIconProviderResources, WidgetIconProviderResources>();
		services.AddScoped<IWidgetIconService, WidgetIconService>();
		services.AddSingleton<IBuiltInWidgetUiProvider, SliderWidgetUiProvider>();
		services.AddSingleton<IWidgetTriggerService, WidgetTriggerService>();
		services.AddSingleton<IBuiltInWidgetUiProvider, ActionButtonWidgetUiProvider>();
		services.AddSingleton<IBuiltInWidgetUiProvider, MusicPlayerWidgetUiProvider>();
		services.AddSingleton<IBuiltInWidgetUiProvider, HistoryGraphWidgetUiProvider>();
		services.AddSingleton(provider => new WidgetUiProviderRegistry(provider.GetRequiredService<IFolderCache>(),
			provider.GetRequiredService<IEnumerable<IBuiltInWidgetUiProvider>>(),
			provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IUiPreviewSource, WidgetUiPreviewSource>();
		services.AddSingleton(provider => new UiPreviewProviderRegistry(
			provider.GetRequiredService<IEnumerable<IUiPreviewSource>>(),
			provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IUiPreviewSessionOpener, UiPreviewSessionOpener>();
		services.AddSingleton<IUiSessionProviderResolver, UiSessionProviderResolver>();
		services.AddSingleton(provider => new IntegrationUiProviderRegistry(
			provider.GetRequiredService<IEnumerable<IBuiltInIntegrationUiProvider>>(),
			provider.GetRequiredService<IUiSessionSink>,
			provider.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IBuiltInIntegrationUiProvider, WeatherDetailsUiProvider>();
		services.AddSingleton<IBuiltInIntegrationUiProvider, MusicPlayerPickerUiProvider>();
		services.AddSingleton<IBuiltInIntegrationUiProvider, MusicPlayerDevicePickerUiProvider>();
		services.AddSingleton<UiSessionBroker>();
		services.AddSingleton<IUiSessionBroker>(provider => provider.GetRequiredService<UiSessionBroker>());
		services.AddSingleton<IUiSessionSink>(provider => provider.GetRequiredService<UiSessionBroker>());
		services.AddSingleton<IConfigUiSessionOpener, ConfigUiSessionOpener>();
		services.AddSingleton<IWidgetUiSessionOpener, WidgetUiSessionOpener>();
		services.AddSingleton<IFolderUiSessionOpener, FolderUiSessionOpener>();
		services.AddSingleton<IModalInteractionCoordinator, ModalInteractionCoordinator>();
		services.AddSingleton<IUiInteractionsFactory, UiInteractionsFactory>();
		services.AddSingleton<IModalUiSessionOpener, ModalUiSessionOpener>();
		services.AddHostedService<UiSessionDrainBackgroundService>();
		services.AddHostedService<ModalSessionWatcher>();
		services.AddHostedService<LoopbackPortFileService>();
		services.AddMediator();

		services.AddHostedService<UiConnectionShutdownBackgroundService>();
		services.AddHostedService<CachingInitializeBackgroundService>();
		services.AddHostedService<PublicTlsCertificateRenewalBackgroundService>();
		services.AddHostedService<DeviceLayoutConstraintWarmupBackgroundService>();
		services.AddHostedService<AdbBackgroundService>();
		services.AddHostedService<IntegrationStartupBackgroundService>();
		services.AddHostedService<PluginDeviceSessionWatcher>();
		services.AddHostedService<IconPackInitializerBackgroundService>();
		services.AddHostedService<IconProcessingBackgroundService>();
		services.AddHostedService<IconMasterHashBackfillBackgroundService>();
		services.AddHostedService<LabelRenderBackgroundService>();
		services.AddHostedService<VariableBroadcastBackgroundService>();
		services.AddHostedService<WidgetStateEvalBackgroundService>();
		services.AddHostedService<WidgetIconProviderPollService>();
		services.AddHostedService<EventDispatchBackgroundService>();
		services.AddHostedService<ServerLifecycleEventBackgroundService>();
		services.AddHostedService<ScheduledEventBackgroundService>();
		services.AddHostedService<BackupScheduleBackgroundService>();
		services.AddHostedService<VariableInitializeBackgroundService>();
		services.AddHostedService<IntegrationVariablePollingBackgroundService>();
		services.AddHostedService<VariableBindingRestoreBackgroundService>();
		services.AddHostedService<VariableCatalogUpdateBackgroundService>();
		services.AddHostedService<MusicPlayerStateBroadcastBackgroundService>();
		services.AddHostedService<WeatherStateBroadcastBackgroundService>();
		services.AddHostedService<IntegrationIssueBroadcastBackgroundService>();
		services.AddHostedService<InstallationIdInitializeBackgroundService>();
		services.AddHostedService<KeyRingProtectionStartupService>();
		services.AddHostedService<PublicListenerUnavailableBackgroundService>();
		services.AddHostedService<AutostartRefreshBackgroundService>();
		services.AddHostedService<LogTailBackgroundService>();
		services.AddHostedService<UserNotificationBroadcastBackgroundService>();
		services.AddHostedService<DevicePresenceBackgroundService>();
		services.AddHostedService<ApplicationFocusBackgroundService>();
		services.AddHostedService<HostLockStateBackgroundService>();

		services.AddSingleton<StartupReadiness>();
		services.AddSingleton<LogStreamSubscriptionTracker>();
		services.AddSingleton<ILogFileReader, LogFileReader>();
		services.AddSingleton<IUserNotificationStore, UserNotificationStore>();
		services.AddSingleton<IPersistenceRecoveryReporter, PersistenceRecoveryNotifier>();
		services.AddScoped<INetworkRestartNotifier, NetworkRestartNotifier>();
		services.AddScoped<IPublicListenerUnavailableNotifier, PublicListenerUnavailableNotifier>();
		services.AddSingleton<IFontCatalog, SkiaFontCatalog>();
		services.AddSingleton<LabelRenderChannel>();
		services.AddSingleton<LabelSubscriptionTracker>();
		services.AddScoped<ILabelTextService, LabelTextService>();
		services.AddSingleton<WidgetStateEvalChannel>();
		services.AddSingleton<WidgetStateSubscriptionTracker>();
		services.AddSingleton<WidgetIconEvalChannel>();
		services.AddSingleton<IWidgetIconInvalidator, WidgetIconInvalidator>();
		services.AddSingleton<VariableBroadcastChannel>();
		services.AddSingleton<IVariableChangeNotifier, VariableChangeNotifier>();
		services.AddSingleton<VariableHistory>();
		services.AddSingleton<IVariableHistory>(provider => provider.GetRequiredService<VariableHistory>());
		services.AddSingleton<VariableInterestTracker>();
		services.AddSingleton<VariableBroadcaster>();
		services.AddSingleton<WidgetDerivedStateStore>();
		services.AddSingleton<WidgetOptimisticStateStore>();
		services.AddScoped<IWidgetStateService, WidgetStateService>();
		services.AddScoped<IWidgetStateReconciler, WidgetStateReconciler>();
		services.AddSingleton<IWidgetStatePublisher, WidgetStatePublisher>();
		services.AddSingleton<IWidgetRenderSignals, WidgetRenderSignals>();

		services.AddSingleton<IWidgetVariableIndex, WidgetVariableIndex>();
		services.AddSingleton<IEventSubscriptionIndex, EventSubscriptionIndex>();
		services.AddSingleton<EventSampleStore>();
		services.AddSingleton<IEventBus, EventBus>();
		services.AddSingleton<IEventRegistry, EventRegistry>();
		services.AddSingleton<IHostEventProvider, CoreEventProvider>();
		services.AddSingleton<IHostEventProvider, TimeEventProvider>();
		services.AddSingleton<MusicPlayerEventProvider>();
		services.AddSingleton<IHostEventProvider>(sp => sp.GetRequiredService<MusicPlayerEventProvider>());
		services.AddSingleton<DeviceConnectionTracker>();
		services.AddSingleton<ProviderDevicePresenceTracker>();
		services.AddSingleton<ILayoutRegistry, LayoutRegistry>();
		services.AddSingleton<LayoutProviderHost>();
		services.AddSingleton<IFolderViewRegistry, FolderViewRegistry>();
		services.AddSingleton<FolderViewProviderHost>();
		services.AddSingleton<WidgetTypeProviderHost>();
		services.AddSingleton<DeviceLayoutConstraintTracker>();
		services.AddSingleton<IPluginDeviceRegistry, PluginDeviceRegistry>();
		services.AddSingleton<DeviceProviderHost>();
		services.AddScoped<DeviceSurfaceBuilder>();
		services.AddSingleton<DeviceInteractionRouter>();
		services.AddSingleton<DeviceSurfaceProviderRegistry>();
		services.AddSingleton<RemoteDeviceSessionRegistry>();
		services.AddSingleton<RemoteDeviceProviderRegistry>();
		services.AddSingleton<RemoteVariableSubscriptions>();
		services.AddSingleton<IDeviceSurfaceProviderResolver, DeviceSurfaceProviderResolver>();
		services.AddSingleton<DeviceSurfaceService>();
		services.AddSingleton<IDeviceSurfaceService>(sp => sp.GetRequiredService<DeviceSurfaceService>());
		services.AddSingleton<IDeviceSurfaceRenderSink>(sp => sp.GetRequiredService<DeviceSurfaceService>());
		services.AddSingleton<Func<IDeviceSurfaceService>>(sp => sp.GetRequiredService<IDeviceSurfaceService>);
		services.AddSingleton<EventPreviewSamples>();
		services.AddScoped<IEventSubscriptionMatcher, EventSubscriptionMatcher>();
		services.AddScoped<IEventTriggerRunner, EventTriggerRunner>();

		services.AddSingleton<VariableRegistry>();
		services.AddSingleton<IUserVariableStore, JsonUserVariableStore>();
		services.AddSingleton<IVariableBindingStore, JsonVariableBindingStore>();
		services.AddSingleton<VariableBindingLookup>();
		services.AddSingleton<VariableNameFactory>();
		services.AddSingleton<VariableCatalogProviders>();
		services.AddSingleton<VariableUpdateChannel>();
		services.AddSingleton<VariableCatalogInvalidationSignal>();
		services.AddSingleton<IVariableSubscriptionCoordinator, VariableSubscriptionCoordinator>();
		services.AddScoped<IVariableBindingService, VariableBindingService>();

		services.AddScoped<IFolderService, FolderService>();
		services.AddScoped<IProfileService, ProfileService>();
		services.AddScoped<IScriptService, ScriptService>();
		services.AddScoped<IAutomationService, AutomationService>();
		services.AddScoped<IWidgetService, WidgetService>();
		services.AddScoped<IWidgetAppearanceService, WidgetAppearanceService>();
		services.AddScoped<IFlowExecutor, FlowExecutor>();
		services.AddSingleton<IActionExecutionCoordinator, ActionExecutionCoordinator>();
		services.AddScoped<IScriptRunner, ScriptRunner>();
		services.AddScoped<IVariableService, VariableService>();
		services.AddScoped<IActionButtonStateService, ActionButtonStateService>();
		services.AddScoped<IVariableTemplateRenderer, VariableTemplateRenderer>();
		services.AddScoped<IActionConditionEvaluator, ActionConditionEvaluator>();

		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<LoginThrottle>();
		services.AddSingleton<FailedLoginNotificationTracker>();
		services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
		services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
		services.AddScoped<IDeviceRepository, DeviceRepository>();
		services.AddScoped<IDeviceService, DeviceService>();
		// Singleton beside the scoped auth service: a one-time device credential is minted in one
		// request and spent in the next, so it cannot live on a per-request object (issue #727).
		services.AddSingleton<IDeviceEnrollmentStore, DeviceEnrollmentStore>();
		services.AddSingleton<PairingCodeStore>();
		services.AddScoped<IAuthService, AuthService>();

		// Plugin registration and developer tokens (issue #411). The registry, the launch-token store
		// and the session-token issuer are singletons - they hold live, in-process state that must
		// survive across requests; everything backed by the database is scoped, like the rest of the
		// app's repositories/services.
		services.AddSingleton<IPluginLaunchTokenService, PluginLaunchTokenService>();
		services.AddSingleton<IPluginSessionRegistry, PluginSessionRegistry>();
		services.AddSingleton<IPluginSessionTokenIssuer, JwtPluginSessionTokenIssuer>();
		// A singleton for the same reason as the registry above: a compatibility verdict, and the record
		// of which of its findings have already been logged, are live in-process state derived from the
		// handshake rather than anything persisted.
		services.AddSingleton<IPluginCompatibilityService, PluginCompatibilityService>();
		// Drops that verdict, and the rest of a plugin's in-memory state, whenever a plugin id is
		// retired - a singleton because every piece of state it forgets is one.
		services.AddSingleton<IPluginIdentityForgetter, PluginIdentityForgetter>();
		// The invoke/await half of the capability protocol (issue #413) - a singleton for the same
		// reason as the registry it wraps: pending invocations are live, in-process state.
		services.AddSingleton<IPluginCapabilityInvoker, PluginCapabilityInvoker>();
		// The core scope is registered eagerly in the factory itself, not by a hosted service, so it is
		// already present for the very first client request or plugin registration the host serves.
		services.AddSingleton<ILocalizationCatalogRegistry>(_ =>
		{
			var registry = new LocalizationCatalogRegistry();
			registry.Register(MacroDeckStrings.LocalizationCatalog);
			registry.Register(AppStrings.LocalizationCatalog);
			return registry;
		});
		services.AddSingleton<ILocalizationResolver, LocalizationResolver>();
		services.AddSingleton<IRemotePluginSnapshotStore, RemotePluginSnapshotStore>();
		services.AddSingleton<IRemotePluginConnectionState, PluginSessionConnectionState>();
		services.AddSingleton<RemotePluginSnapshotRefresher>();
		services.AddSingleton<IRemotePluginIntegrationRegistrar, RemotePluginIntegrationRegistrar>();
		services.AddSingleton<IPluginAssetCache, PluginAssetDiskCache>();
		services.AddSingleton<IPluginAssetReceiver, PluginAssetReceiver>();
		services.AddSingleton<IPluginHostAssetSender, PluginHostAssetSender>();
		services.AddSingleton<HostCallbackThrottle>();
		services.AddSingleton<IPluginCallbackRouter, PluginCallbackRouter>();
		// Mediator's source generator only runs over MacroDeckHost.Application, so a handler in this
		// assembly is registered by hand - as HostStatePusher is below. This one has to live here rather
		// than in Application: INotificationService is an Integrations type, which Application does not
		// reference. The factory is deliberately lazy - LinuxNotificationService's constructor blocks for
		// up to two seconds probing for notify-send, which must not sit in application startup.
		services.AddSingleton<INotificationService>(_ => NotificationServiceFactory.Create());
		// The same instance the factory hands to the built-in system integration, which is created by
		// reflection and never sees this container.
		services.AddSingleton<IShellNotificationBridge>(_ => ShellNotificationBridge.Instance);
		services.AddSingleton<INotificationHandler<PluginPairingRequestedNotification>,
			PluginPairingSystemNotificationHandler>();

		services.AddSingleton<HostStatePusher>();
		foreach (var handlerInterface in typeof(HostStatePusher).GetInterfaces()
			.Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
		{
			services.AddSingleton(handlerInterface, sp => sp.GetRequiredService<HostStatePusher>());
		}

		services.AddScoped<IPluginRegistrationRepository, PluginRegistrationRepository>();
		services.AddScoped<IPluginAccessTokenRepository, PluginAccessTokenRepository>();
		services.AddScoped<IPluginTokenService, PluginTokenService>();
		services.AddScoped<IPluginRegistrationService, PluginRegistrationService>();
		services.AddScoped<IPluginSessionService, PluginSessionService>();
		services.AddScoped<IPluginIdentityReconciler, PluginIdentityReconciler>();
		services.AddSingleton(PluginPairingOptions.Default);
		// A singleton for the same reason as the session registry above: live pairing requests are
		// in-process, deliberately never-persisted state. IPluginPairingService is scoped instead - it
		// depends on the scoped IPluginRegistrationService, which talks to the database.
		services.AddSingleton<IPluginPairingRequestStore, PluginPairingRequestStore>();
		services.AddScoped<IPluginPairingService, PluginPairingService>();
		services.AddKeyedSingleton<LoginThrottle>("plugin",
			(sp, _) => new LoginThrottle(sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<PluginWebSocketEndpoint>();
		// log.publish ingestion (issue #414 stage 2): the rate limiter is a singleton keyed by plugin id
		// on purpose - see its remarks on why a per-connection bucket would reopen a reconnect bypass.
		services.AddSingleton<IPluginLogRateLimiter, PluginLogRateLimiter>();
		services.AddSingleton<IPluginLogIngestor, PluginLogIngestor>();

		// Managed plugin supervisor (issue #412): discovery, manifest validation and the process
		// lifecycle are all singletons for the same reason as above - they hold live, in-process state.
		// The manifest and artifact readers come from MacroDeck.Plugin.Packaging (#416), which also
		// backs the standalone plugin-packing tooling; registering both together here keeps the two
		// readers - manifest now, artifact a few lines down - from drifting into separate lifetimes.
		services.AddMacroDeckPluginPackaging();
		services.AddSingleton(PluginSupervisorOptions.Default);
		services.AddSingleton<IPluginInstallationCatalog, PluginInstallationCatalog>();
		services.AddSingleton<IPluginRuntimeStateStore, PluginRuntimeStateStore>();
		services.AddSingleton<IPluginProcessJournal, PluginProcessJournal>();
		services.AddSingleton<IProcessTable, ProcessTable>();
		services.AddSingleton<IPluginOrphanReaper, PluginOrphanReaper>();
		services.AddSingleton<IPluginProcessJobFactory, PluginProcessJobFactory>();
		services.AddSingleton<IPluginProcessLauncher, PluginProcessLauncher>();
		services.AddSingleton<IPluginHealthProbe, PluginHealthProbe>();
		services.AddHttpClient(PluginHealthProbeHttpClient.Name, client => { })
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false
			});
		services.AddSingleton<IDotnetMuxerLocator, DotnetMuxerLocator>();
		services.AddSingleton<IPluginSupervisor, PluginSupervisor>();
		services.AddHostedService<PluginSupervisorBackgroundService>();

		// Ordered outermost largest so each layer's cap is strictly inside the next: the supervisor's
		// plugin budget, then this host timeout, then the bootstrapper's own graceful stop timeout.
		services.Configure<HostOptions>(options =>
			options.ShutdownTimeout = PluginShutdownBudgets.HostShutdownTimeout);

		// Plugin installation lifecycle (issue #415): the artifact format, safe staging, atomic
		// activation and rollback. Singletons because the installer serialises concurrent installs of the
		// same plugin id in memory and the cache holds its own index.
		services.AddSingleton(PluginInstallerOptions.Default);
		services.AddSingleton<IPluginArtifactAcquirer, PluginArtifactAcquirer>();
		services.AddSingleton<IPluginArtifactCache, PluginArtifactCache>();
		services.AddSingleton<IPluginDependencyResolver, PluginDependencyResolver>();
		services.AddSingleton<IPluginInstaller, PluginInstaller>();

		// Host-side plugin signature enforcement (issue #610). The evaluator and the revocation source are
		// singletons - neither holds request-scoped state - but the trust record repository and baseline
		// marker sit on the scoped DbContext, so callers that live longer than a request (the installer,
		// the supervisor) reach them through IServiceScopeFactory instead of the constructor.
		services.AddSingleton(PluginTrustOptions.Default);
		services.AddSingleton<IPluginRevocationSource, NoRevocationDataSource>();
		services.AddSingleton<IPluginTrustEvaluator, PluginTrustEvaluator>();
		services.AddScoped<IPluginTrustRecordRepository, PluginTrustRecordRepository>();
		services.AddScoped<IPluginTrustBaseline, PluginTrustBaseline>();
		services.AddHostedService<PluginTrustBaselineBackgroundService>();
		services.AddHttpClient(PluginArtifactAcquirer.HttpClientName, client => { })
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false
			});

		// Extension store (issue #517): the registry fetch/verify pipeline, install pipeline, media
		// cache and update detector already exist under Application/Store and Infrastructure/Store -
		// this wires them into DI. Singletons throughout: the catalog and operation tracker hold their
		// own in-memory snapshots, and StoreInstallExecutor reaches the scoped
		// IIconPackRestoreService/IProfilePortabilityService through IServiceScopeFactory rather than
		// taking either directly.
		services.AddSingleton(StoreRegistryOptions.Default);
		// The client names must match the internal StoreHttp.RegistryClientName/ArtifactClientName
		// constants in MacroDeckHost.Infrastructure - that type is internal to its own assembly, so the
		// literal is duplicated here rather than referenced.
		services.AddHttpClient("store-registry", client => { })
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false
			});
		services.AddHttpClient("store-artifact", client => { })
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false
			});
		services.AddSingleton<IStoreCatalog, StoreCatalog>();
		services.AddSingleton<IStoreCatalogQueryService, StoreCatalogQueryService>();
		services.AddSingleton<StoreRegistryReader>();
		services.AddSingleton<IStoreRegistryStateStore, JsonStoreRegistryStateStore>();
		services.AddSingleton<IStoreInstallationStore, JsonStoreInstallationStore>();
		services.AddSingleton<IStoreOperationStore, JsonStoreOperationStore>();
		services.AddSingleton<IStoreOperationTracker, StoreOperationTracker>();
		services.AddSingleton<StoreOperationChannel>();
		services.AddSingleton<StoreOperationCancellation>();
		services.AddSingleton<StoreInstallConsent>();
		services.AddSingleton<IStoreArtifactDownloader, StoreArtifactDownloader>();
		services.AddSingleton<IStoreUpdateState, StoreUpdateState>();
		services.AddSingleton<IStoreUpdateDetector, StoreUpdateDetector>();
		services.AddSingleton<IStoreRegistryRefresher, StoreRegistryRefresher>();
		services.AddSingleton<IStoreInstallCoordinator, StoreInstallCoordinator>();
		services.AddSingleton<IStoreInstallExecutor, StoreInstallExecutor>();
		services.AddSingleton<IStoreInstallationReconciler, StoreInstallationReconciler>();
		services.AddScoped<IStoreUninstallService, StoreUninstallService>();
		services.AddHostedService<StoreRegistryRefreshBackgroundService>();
		services.AddHostedService<StoreOperationBackgroundService>();
		services.AddHostedService<StoreOperationBroadcastBackgroundService>();

		services.AddHttpClient(ConnectIdentityClient.HttpClientName, client => { })
			.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));
		services.AddSingleton<IConnectIdentityClient, ConnectIdentityClient>();
		services.AddSingleton<IConnectCredentialStore, SecretServiceConnectCredentialStore>();
		services.AddSingleton<IConnectSuspensionFloor, ConnectSuspensionFloor>();
		services.AddSingleton<ConnectTokenPersister>();
		services.AddSingleton(sp => new ConnectSignInFlow(sp.GetRequiredService<IConnectIdentityClient>(),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IConnectSignInFlow>(sp => sp.GetRequiredService<ConnectSignInFlow>());
		services.AddSingleton(sp => new ConnectSessionService(sp.GetRequiredService<IConnectIdentityClient>(),
			sp.GetRequiredService<IConnectCredentialStore>(),
			sp.GetRequiredService<ConnectTokenPersister>(),
			sp.GetRequiredService<ConnectSignInFlow>(),
			sp.GetRequiredService<IConnectSuspensionFloor>(),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<Serilog.ILogger>()));
		services.AddSingleton<IConnectSessionService>(sp => sp.GetRequiredService<ConnectSessionService>());
		services.AddSingleton<IConnectAvatarCache, ConnectAvatarCache>();
		services.AddHostedService<ConnectSessionBackgroundService>();
		services.AddHostedService<Connect.ConnectSessionNotifier>();

		services.AddScoped<ISecretRepository, SecretRepository>();
		services.AddScoped<ISecretService, SecretService>();
		services.AddScoped<IWidgetSecretCloner, WidgetSecretCloner>();
		services.AddScoped<IWidgetVariableCloner, WidgetVariableCloner>();
		services.AddScoped<IWidgetSecretScrubber, WidgetSecretScrubber>();
		services.AddScoped<IIntegrationConfigEntryRepository, IntegrationConfigEntryRepository>();
		services.AddSingleton<IBuildEnvironment, BuildEnvironment>();
		services.AddScoped<IAppPreferenceRepository, AppPreferenceRepository>();
		services.AddScoped<IAppPreferenceService, AppPreferenceService>();
		services.AddSingleton(AutostartRegistrarFactory.Create());
		services.AddSingleton<IAutostartService, AutostartService>();
		services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
		services.AddSingleton<IFolderRevealService, FolderRevealService>();
		services.TryAddSingleton<IHostListenerState>(_ => new HostListenerState(ResolvedPublicEndpoints.Value, false));
		services.AddAdbManager();
		// Device provisioners run on top of the adb manager registered above (issue #727).
		services.AddWebClientTargets();
		services.AddScoped<IIntegrationConfigStore, IntegrationConfigStore>();
		services.AddSingleton<IOAuthCallbackCoordinator, OAuthCallbackCoordinator>();
		services.AddSingleton<IConfigFlowManager, ConfigFlowManager>();
		services.AddSingleton<IIntegrationConfigMutationAdapter, ObsConfigurationMutationAdapter>();
		services.AddSingleton<IIntegrationConfigMutationCoordinator, IntegrationConfigMutationCoordinator>();
		services.AddSingleton<IntegrationInitializer>();
		services.AddSingleton<IIntegrationLifecycle, IntegrationLifecycle>();
		services.AddSingleton<IProfileStore, JsonProfileStore>();
		services.AddSingleton<ProfileCache>();
		services.AddSingleton<IProfileCache>(sp => sp.GetRequiredService<ProfileCache>());
		services.AddSingleton<IFolderCache, FolderCache>();
		services.AddSingleton<IScriptStore, JsonScriptStore>();
		services.AddSingleton<ScriptCache>();
		services.AddSingleton<IScriptCache>(sp => sp.GetRequiredService<ScriptCache>());
		services.AddSingleton<IScriptApi, ScriptApi>();
		services.AddSingleton<IWidgetDataWriteLock, WidgetDataWriteLock>();
		services.AddSingleton<IWidgetApi, WidgetApi>();
		services.AddSingleton<IWidgetDataSchemaProvider, WidgetDataSchemaProvider>();
		services.AddSingleton<IWidgetTypeRegistry, WidgetTypeRegistry>();
		services.AddSingleton<IUserVariableApi, UserVariableWriter>();
		services.AddSingleton<IAutomationStore, JsonAutomationStore>();
		services.AddSingleton<AutomationCache>();
		services.AddSingleton<IAutomationCache>(sp => sp.GetRequiredService<AutomationCache>());
		services.AddSingleton<IProfileRegistry, ProfileRegistry>();
		services.AddSingleton<IIntegrationStateStore, JsonIntegrationStateStore>();
		services.AddSingleton<IIntegrationRegistry, IntegrationRegistry>();
		services.AddSingleton<IIntegrationIssueService, IntegrationIssueService>();
		services.AddSingleton<IIntegrationIssueBroadcastTrigger, IntegrationIssueBroadcastTrigger>();
		services.AddSingleton<IIntegrationHostIssueStore, IntegrationHostIssueStore>();
		services.AddSingleton<IVariablePollingInvalidationSignal, VariablePollingInvalidationSignal>();
		services.AddSingleton<IVariableRefreshSignal, VariableRefreshSignal>();
		services.AddSingleton<IDeckNavigator, DeckNavigator>();
		services.AddSingleton<IDeviceDeckNavigator>(sp => (DeckNavigator)sp.GetRequiredService<IDeckNavigator>());
		services.AddSingleton<IApplicationFocusCoordinator, ApplicationFocusCoordinator>();
		services.AddSingleton<IApplicationFocusWatcher, FocusedApplicationWatcher>();
		services.AddSingleton<HostLockState>();
		services.AddSingleton<IHostLockState>(sp => sp.GetRequiredService<HostLockState>());
		services.AddSingleton<IRunningApplicationCatalog, RunningApplicationCatalog>();
		services.AddSingleton<IMusicPlayerRegistry, MusicPlayerRegistry>();
		services.AddSingleton<IMusicPlayerStateCache, MusicPlayerStateCache>();
		services.AddSingleton<IMusicPlayerPollNudge, MusicPlayerPollNudge>();
		services.AddSingleton<IMusicPlayerInstancesSnapshot, MusicPlayerInstancesSnapshot>();
		services.AddSingleton<IMusicPlayerClientSync, MusicPlayerClientSync>();
		services.AddSingleton<IMusicPlayerStateNotifier, MusicPlayerStateNotifier>();
		services.AddSingleton<IWeatherRegistry, WeatherRegistry>();
		services.AddSingleton<IWeatherBroadcastTrigger, WeatherBroadcastTrigger>();
		services.AddSingleton<IArtworkProcessor, ImageSharpArtworkProcessor>();
		services.AddSingleton<IArtworkPaletteExtractor, ImageSharpArtworkPaletteExtractor>();
		services.AddSingleton<IMusicPlayerArtworkService, MusicPlayerArtworkService>();
		services.AddSingleton<IActionInteractions, ActionInteractions>();
		services.AddSingleton<IIconPackStore, JsonIconPackStore>();
		services.AddSingleton<IIconImportBatchStore, JsonIconImportBatchStore>();
		services.AddSingleton<IconPackCache>();
		services.AddSingleton<IIconPackCache>(sp => sp.GetRequiredService<IconPackCache>());
		services.AddSingleton<IWidgetIconSource, IconPackWidgetIconSource>();
		services.AddSingleton<IIconStorage, FileSystemIconStorage>();
		services.AddSingleton<IIconProcessor, ImageSharpIconProcessor>();
		services.AddSingleton<IAppIconExtractor, AppIconExtractor>();
		services.AddSingleton<IApplicationPathResolver, ApplicationPathResolver>();
		services.AddSingleton<IIconImageFallbackStore, ImageSharpIconFallbackStore>();
		services.AddSingleton<IconProcessingChannel>();
		services.AddSingleton<IconImportBatchTracker>();
		services.AddSingleton<IconImportBatchFinalizer>();
		services.AddSingleton<IconImportCancellationRegistry>();
		services.AddSingleton<IconImportCoalescer>();
		services.AddScoped<IIconImportService, IconImportService>();
		services.AddScoped<IIconPackService, IconPackService>();
		services.AddScoped<IIconService, IconService>();
		services.AddScoped<IIconPackExportService, IconPackExportService>();
		services.AddScoped<IIconPackRestoreService, IconPackRestoreService>();
		services.AddSingleton<IIconPackOwner, StoreIconPackOwner>();
		services.AddSingleton<IIconPackOwnerRegistry, IconPackOwnerRegistry>();
		services.AddScoped<IPortableAssetManager, PortableAssetManager>();
		services.AddScoped<IPortableArchiveInspector, PortableArchiveInspector>();
		services.AddScoped<IProfilePortabilityService, ProfilePortabilityService>();
		services.AddScoped<IFolderPortabilityService, FolderPortabilityService>();
		services.AddScoped<IWidgetPortabilityService, WidgetPortabilityService>();
		services.AddSingleton<IMigrationActionRegistry, MigrationActionRegistry>();
		services.AddScoped<IMigrationSource, MacroDeck2MigrationSource>();
		services.AddScoped<IMigrationService, MigrationService>();
		services.AddSingleton(_ =>
		{
			var session = new HostSession();
			if (HostStartupFacts.RestoreApplied)
			{
				session.MarkRestoreApplied();
			}

			return session;
		});
		services.AddSingleton<IBackupOperationGate, BackupOperationGate>();
		services.AddSingleton<IBackupProgressReporter, BackupProgressReporter>();
		services.AddSingleton<IBackupSnapshotSource, BackupSnapshotSource>();
		services.AddSingleton<IBackupArchiveWriter, BackupArchiveWriter>();
		services.AddSingleton<IBackupArchiveReader, BackupArchiveReader>();
		services.AddSingleton<IBackupStorageProvider, LocalBackupStorageProvider>();
		services.AddSingleton<IBackupStorageRegistry, BackupStorageRegistry>();
		services.AddSingleton<IBackupCatalog, BackupCatalog>();
		services.AddSingleton<IBackupRetentionPolicy, KeepLatestBackupRetentionPolicy>();
		services.AddSingleton<IBackupRetentionService, BackupRetentionService>();
		services.AddSingleton<IBackupService, BackupService>();
		services.AddSingleton<IRestoreService, RestoreService>();
		services.AddSingleton<PreUpdateBackupCoordinator>();
		services.AddSingleton<IPreUpdateBackupCoordinator>(sp => sp.GetRequiredService<PreUpdateBackupCoordinator>());
		services.AddSingleton<IRestoreLock>(sp => sp.GetRequiredService<PreUpdateBackupCoordinator>());

		// Scoped because it reads and writes through the scoped DbContext. The singletons above reach it
		// through IServiceScopeFactory rather than holding it.
		services.AddScoped<IBackupRecoveryKeyService, BackupRecoveryKeyService>();

		services.AddSingleton<IHostOptionsSource, ProcessesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, AudioDevicesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, VariablesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, UserVariablesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, MusicPlayerInstancesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, ProfilesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, FoldersOptionsSource>();
		services.AddSingleton<IHostOptionsSource, IntegrationsOptionsSource>();
		services.AddSingleton<IHostOptionsSource, DevicesOptionsSource>();
		services.AddSingleton<IHostOptionsSource, WidgetsOptionsSource>();
		services.AddSingleton<IHostOptionsSource, FontsOptionsSource>();
		services.AddSingleton<IHostOptionsSource, AdbDevicesOptionsSource>();

		services.AddDbContext<DatabaseContext>();

		services.AddPublicTlsCertificateStore();
		services.AddServiceAdvertisement();

		// AddDataProtection registers its key manager and hosted service with TryAdd, so they resolve
		// even though Program.cs's provider wins IDataProtectionProvider - and they would point at the
		// real key ring. Locked mode therefore has to redirect this registration too, or Data Protection
		// quietly mints a replacement key over a ring it merely could not read.
		var keyRingBuilder = services.AddDataProtection()
			.PersistKeysToFileSystem(new DirectoryInfo(KeyRingStartupState.ScratchKeysDirectory ?? paths.KeysDirectory))
			.SetApplicationName("MacroDeck");
		KeyRingDataProtection.Configure(keyRingBuilder,
			KeyRingStartupState.KekHolder,
			KeyRingStartupState.Plan.Mode);

		services.AddSingleton<IKeyRingProtectionService>(provider => new KeyRingProtectionService(
			provider.GetRequiredService<IMacroDeckPaths>(),
			KeyRingStartupState.Store,
			KeyRingStartupState.Identity,
			KeyRingStartupState.KekHolder,
			KeyRingStartupState.Plan,
			KeyRingStartupState.Portable,
			provider.GetRequiredService<TimeProvider>(),
			provider.GetRequiredService<Serilog.ILogger>()));

		if (KeyRingStartupState.IsLocked)
		{
			FreezeBackgroundWork(services);
		}
	}

	/// <summary>
	/// A locked host serves the unlock gate and nothing else. Every background service but the loopback
	/// port file reads the database, a secret, a plugin or an integration - all of which are unreadable
	/// until the key encryption key is back, and several of which would write something in the attempt.
	/// </summary>
	private static void FreezeBackgroundWork(IServiceCollection services)
	{
		foreach (var descriptor in services
			.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
				descriptor.ImplementationType != typeof(LoopbackPortFileService))
			.ToList())
		{
			services.Remove(descriptor);
		}
	}

	public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
	{
		app.UseExceptionHandler();

		// Outermost after the exception handler, so the status code it observes is the one the client
		// actually receives: silencing the framework's request-pipeline categories otherwise leaves a
		// request that fails without throwing - a 401, 404 or 409 - with no trace in the log at all.
		app.UseRequestOutcomeLogging();

		// Immediately after the outcome logging so a refusal is still recorded, and before anything that
		// would touch a secret store the host cannot read.
		app.UseLockedHostGate();

		// api/plugin-pairing is excluded alongside the ProtocolConstants.All plugin paths even though it
		// does not start with /api/plugins: LoopbackConnection.IsTrusted, which every action on that
		// controller requires, needs no credentials, so without this exclusion any website the developer
		// happens to have open could cross-origin read pending pairing requests - leaking plugin ids and
		// executable paths - and POST an approval.
		app.UseWhen(context => !ProtocolConstants.All.Any(path => context.Request.Path.StartsWithSegments(path)) &&
				!context.Request.Path.StartsWithSegments("/api/plugin-pairing") &&
				// api/client-targets is excluded for exactly the reason api/plugin-pairing is: every
				// action on it is gated on LoopbackConnection.IsTrusted and needs no credentials, so
				// without this any page the user has open could drive a device attached to this machine.
				!context.Request.Path.StartsWithSegments("/api/client-targets") &&
				!context.Request.Path.StartsWithSegments("/api/ui-websocket/tickets"),
			branch => branch.UseCors("AllowAny"));

		app.UseSpaShellNoCache();

		// API responses are live state, so they must not be storable either. Without any
		// Cache-Control a cache may keep and reuse them heuristically, which is how a kiosk
		// browser kept rendering a deck built from a stale GET /api/folders (widgets pinned by a
		// newer release missing) until its cache was cleared by hand.
		app.UseApiNoStore();
		app.UseDefaultFiles();
		app.UseStaticFiles(SpaCachingApplicationBuilderExtensions.CreateSpaStaticFileOptions());
		app.UseWebSockets();
		app.UseRouting();
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseEndpoints(endpoints =>
		{
			endpoints.MapControllers();
			// Neither socket is mapped while the key ring is locked. ProtocolConstants.WebSocketPath is
			// /plugins/ws, outside /api, so the locked gate above never sees it - and a plugin that
			// connected would be told to act on a secret store the host cannot read.
			if (!KeyRingStartupState.IsLocked)
			{
				endpoints.MapUiWebSocket();
				endpoints.MapPluginWebSocket();
			}

			endpoints.MapFallbackToFile("admin/{**path}", "admin/index.html").AllowAnonymous();

			// One route per packaged device target (issue #727). Registered from what is on disk rather
			// than from a list in the host, so adding a target is a build change and nothing else.
			foreach (var targetId in WebClientTargets.Discover(env.WebRootPath))
			{
				endpoints
					.MapFallbackToFile($"{WebClientTargets.RootDirectoryName}/{targetId}/{{**path}}",
						$"{WebClientTargets.RootDirectoryName}/{targetId}/index.html")
					.AllowAnonymous();
			}

			if (SpaPlaceholder.IsClientMissing(env.WebRootPath))
			{
				endpoints.MapFallback(SpaPlaceholder.Handle).AllowAnonymous();
			}
			else
			{
				endpoints.MapFallbackToFile("{**path}", "index.html").AllowAnonymous();
			}
		});
	}
}
