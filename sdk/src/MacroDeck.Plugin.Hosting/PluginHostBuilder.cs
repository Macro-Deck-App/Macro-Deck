using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Icons;
using MacroDeck.Plugin.Hosting.Capabilities.Localization;
using MacroDeck.Plugin.Hosting.Localization;
using MacroDeck.Plugin.Hosting.Configuration;
using MacroDeck.Plugin.Hosting.Credentials;
using MacroDeck.Plugin.Hosting.DependencyInjection;
using MacroDeck.Plugin.Hosting.Endpoints;
using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Hosting.Validation;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Identity;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.Widgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// Builds a plugin. Wraps <see cref="WebApplicationBuilder" /> rather than deriving from it - both it
/// and <see cref="WebApplication" /> are sealed - and exposes it, so anything the SDK does not put a
/// method on is still one property away.
/// </summary>
public sealed class PluginHostBuilder
{
	private readonly List<Action<PluginHostBuilderContext, IApplicationBuilder>> _configure = [];
	private readonly List<Action<PluginHostBuilderContext, IServiceCollection>> _configureServices = [];

	private PluginRegistrationMode? _mode;
	private ILocalizationCatalog? _localizationCatalog;

	internal PluginHostBuilder(string[] args)
	{
		WebApplicationBuilder = WebApplication.CreateBuilder(args);
		WebApplicationBuilder.Configuration.AddMacroDeckPluginEnvironmentVariables();

		// A plugin binds a loopback port by default: the listener exists to serve the SDK's own health
		// and diagnostics endpoints, and listening on every interface would publish a plugin's
		// internals to the network as a side effect of using the SDK. Applied only when nothing else
		// said where to listen, because UseUrls writes host configuration, which outranks
		// ASPNETCORE_URLS and would otherwise make that variable silently do nothing.
		if (string.IsNullOrEmpty(WebApplicationBuilder.Configuration[WebHostDefaults.ServerUrlsKey]))
		{
			WebApplicationBuilder.WebHost.UseUrls("http://127.0.0.1:0");
		}

		// On in every environment, not just development. A plugin's graph is small, so the cost is a
		// few milliseconds once, and the failure it catches - a scoped dependency captured by a
		// singleton - otherwise appears as corruption under load rather than as an error.
		WebApplicationBuilder.Host.UseDefaultServiceProvider((_, providerOptions) =>
		{
			providerOptions.ValidateOnBuild = true;
			providerOptions.ValidateScopes = true;
		});
	}

	/// <summary>The wrapped builder. Everything else here is sugar over it.</summary>
	public WebApplicationBuilder WebApplicationBuilder { get; }

	/// <summary>The plugin's services.</summary>
	public IServiceCollection Services => WebApplicationBuilder.Services;

	/// <summary>The plugin's configuration, already carrying the <c>MACRO_DECK_PLUGIN_*</c> variables.</summary>
	public ConfigurationManager Configuration => WebApplicationBuilder.Configuration;

	/// <summary>The plugin's logging.</summary>
	public ILoggingBuilder Logging => WebApplicationBuilder.Logging;

	/// <summary>The hosting environment.</summary>
	public IHostEnvironment Environment => WebApplicationBuilder.Environment;

	/// <summary>
	/// Forces a registration mode instead of inferring it from configuration. Rarely needed: a plugin
	/// launched by the host has an id and a secret in its environment and is recognised as managed.
	/// </summary>
	public PluginHostBuilder UseRegistrationMode(PluginRegistrationMode mode)
	{
		_mode = mode;
		return this;
	}

	/// <summary>
	/// Publishes the plugin's own localized strings, so the host can serve them to whichever client
	/// renders this plugin's UI. Pass the generated catalog:
	/// <c>builder.UseLocalization(Strings.LocalizationCatalog)</c>.
	/// </summary>
	/// <remarks>
	/// A plugin that calls this declares the <c>localization</c> capability; one that does not declares
	/// nothing and keeps working exactly as before. The catalog's scope must be this plugin's own -
	/// <c>plugin:&lt;plugin-id&gt;</c>, which the generator derives from <c>manifest.json</c> - and the
	/// host rejects a catalog claiming any other.
	/// </remarks>
	public PluginHostBuilder UseLocalization(ILocalizationCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);

		_localizationCatalog = catalog;
		PluginText.Use(catalog);
		return this;
	}

	/// <summary>
	/// Registers the plugin's integration: the actions it declares, plus a handler for every capability
	/// interface its type implements. Constructed by dependency injection as a singleton, so it can take
	/// <c>IHttpClientFactory</c>, <c>IOptions&lt;T&gt;</c> or <c>ILogger&lt;T&gt;</c> in its constructor.
	///
	/// <para>
	/// This is the one door. Registering an integration straight onto <see cref="Services" /> skips the
	/// capability wiring and shows up only as a capability missing from the host, which is what MDP2004
	/// catches.
	/// </para>
	/// </summary>
	public PluginHostBuilder RegisterIntegration<TIntegration>()
		where TIntegration : class, IPluginIntegration
		=> ConfigureServices((_, services) => services.AddMacroDeckIntegration<TIntegration>());

	/// <summary>
	/// Registers an integration built by <paramref name="factory" />, for when construction is not just
	/// dependency injection - a value read from configuration, a client the plugin already owns.
	/// </summary>
	public PluginHostBuilder RegisterIntegration<TIntegration>(Func<IServiceProvider, TIntegration> factory)
		where TIntegration : class, IPluginIntegration
	{
		ArgumentNullException.ThrowIfNull(factory);
		return ConfigureServices((_, services) => services.AddMacroDeckIntegration(factory));
	}

	/// <summary>
	/// Registers a handler for one capability kind - the seam every capability beyond the ones an
	/// integration's interfaces imply plugs into. The transport and the dispatcher never learn about a
	/// specific kind.
	/// </summary>
	public PluginHostBuilder RegisterCapabilityHandler<THandler>()
		where THandler : class, ICapabilityHandler
		=> ConfigureServices((_, services) => services.AddMacroDeckCapabilityHandler<THandler>());

	/// <summary>Registers services. Callbacks run in the order they were added.</summary>
	public PluginHostBuilder ConfigureServices(Action<PluginHostBuilderContext, IServiceCollection> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		_configureServices.Add(configure);
		return this;
	}

	/// <summary>
	/// Adds middleware. Callbacks run in the order they were added, after the SDK's reserved-path
	/// middleware, which is why nothing added here can answer a <c>/_macrodeck</c> request.
	/// </summary>
	public PluginHostBuilder Configure(Action<PluginHostBuilderContext, IApplicationBuilder> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		_configure.Add(configure);
		return this;
	}

	/// <summary>
	/// Uses an ASP.NET Core style startup class for both halves. <typeparamref name="TStartup" /> is
	/// constructed with <c>ActivatorUtilities</c>, so it can take <c>IConfiguration</c>,
	/// <c>IHostEnvironment</c> or <see cref="PluginMetadata" /> in its constructor.
	/// </summary>
	public PluginHostBuilder UseStartup<TStartup>()
		where TStartup : class, IPluginStartup
	{
		IPluginStartup? startup = null;

		// Built once, lazily, and shared by both callbacks: a startup class that held state between
		// its two methods would otherwise silently get two instances.
		IPluginStartup Resolve(PluginHostBuilderContext context) => startup ??= CreateStartup<TStartup>(context);

		ConfigureServices((context, services) => Resolve(context).ConfigureServices(services));
		Configure((context, app) => Resolve(context).Configure(app));

		return this;
	}

	/// <summary>
	/// Validates everything that can be validated locally, wires the SDK's own services, and produces
	/// the application.
	/// </summary>
	/// <exception cref="PluginConfigurationException">
	/// The plugin as configured cannot run. Carries every problem found, not just the first.
	/// </exception>
	public PluginApplication Build()
	{
		var problems = new List<string>();
		var metadata = BuildMetadata(problems);

		// Straight after the manifest, into the same list: a bad icon reads as one configuration problem
		// among the others rather than as an upload failure minutes later in the log.
		var iconSource = IconAssetSource.Create(metadata, Environment.ContentRootPath, problems);
		var context = new PluginHostBuilderContext(Configuration, Environment, metadata);

		AddSdkServices(metadata, iconSource);

		foreach (var configure in _configureServices)
		{
			configure(context, Services);
		}

		// Registered after the author's callbacks so the connection is the last hosted service to
		// start: an author's own background services are running before the socket opens.
		Services.AddHostedService<PluginConnectionHostedService>();

		WebApplication application;
		try
		{
			application = WebApplicationBuilder.Build();
		}
		catch (Exception exception) when (exception is InvalidOperationException or AggregateException)
		{
			// A graph that will not resolve is a configuration mistake like any other, and should read
			// like one rather than like an internal container failure.
			problems.Add($"The service graph is not valid: {exception.Message}");
			throw new PluginConfigurationException(problems);
		}

		try
		{
			application.UseRouting();
			MacroDeckEndpoints.Map(application);

			foreach (var configure in _configure)
			{
				configure(context, application);
			}

			problems.AddRange(ReservedPathInspector.FindConflicts(((IEndpointRouteBuilder)application).DataSources));

			problems.AddRange(ValidateCapabilities(application.Services, metadata));

			if (problems.Count > 0)
			{
				throw new PluginConfigurationException(problems);
			}
		}
		catch
		{
			// Building the application already created singletons and a root provider. Failing without
			// disposing them leaks them, which matters most in tests that build many plugins.
			application.DisposeAsync().AsTask().GetAwaiter().GetResult();
			throw;
		}

		return new PluginApplication(application, metadata);
	}

	/// <summary>
	/// Identity, description and icon all come from <c>manifest.json</c> at the content root now - not
	/// from builder calls, which used to let the two drift. The one exception is <c>id</c>: configuration
	/// (<c>MacroDeck:Plugin:Id</c>, fed by <c>MACRO_DECK_PLUGIN_ID</c>) is still consulted, but only as a
	/// cross-check against the manifest, or as a fallback when the manifest declares no id at all.
	///
	/// <para>
	/// Always returns a <see cref="PluginMetadata" />, even when problems were found - with empty strings
	/// where a value could not be determined - so <see cref="Build" /> keeps collecting every problem
	/// before throwing, rather than stopping at the first one.
	/// </para>
	/// </summary>
	private PluginMetadata BuildMetadata(List<string> problems)
	{
		var manifest = PluginManifestFileReader.Read(Environment.ContentRootPath, problems);
		if (manifest is null)
		{
			return new PluginMetadata { Id = string.Empty, Name = string.Empty, Version = string.Empty };
		}

		if (manifest.ManifestVersion is not { } manifestVersion ||
			manifestVersion != PluginManifestFileReader.SupportedManifestVersion)
		{
			var declared = manifest.ManifestVersion is { } value
				? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
				: "(none)";
			problems.Add($"The manifest declares manifestVersion {declared}; " +
				$"only {PluginManifestFileReader.SupportedManifestVersion} is understood.");
		}

		var configuredId = Configuration[$"{PluginHostOptions.SectionName}:Id"];
		string? id;
		if (!string.IsNullOrWhiteSpace(manifest.Id))
		{
			id = manifest.Id;

			// A packaging or launch mismatch the host would reject anyway - resolving it silently here
			// would leave the host and the Store disagreeing about which plugin is actually running.
			if (!string.IsNullOrWhiteSpace(configuredId) &&
				!string.Equals(configuredId, manifest.Id, StringComparison.Ordinal))
			{
				problems.Add($"The manifest declares id '{manifest.Id}', but configuration says '{configuredId}'.");
			}
		}
		else
		{
			id = configuredId;
		}

		if (string.IsNullOrWhiteSpace(id))
		{
			problems.Add(
				$"The manifest at '{Path.Combine(Environment.ContentRootPath, PluginManifestFileReader.FileName)}' " +
				"declares no id. Set \"id\" in manifest.json.");
		}
		else if (!MacroDeckId.TryValidateOwnerId(id, OwnerIdKind.Package, out var idError))
		{
			problems.Add($"The plugin id '{id}' is not usable: {idError}");
		}

		if (string.IsNullOrWhiteSpace(manifest.Name))
		{
			problems.Add(
				$"The manifest at '{Path.Combine(Environment.ContentRootPath, PluginManifestFileReader.FileName)}' " +
				"declares no name. Set \"name\" in manifest.json.");
		}
		else
		{
			// The host sends this name on every session (declaredName), and rejects a session whose
			// declaredName is too long or contains a control character - see
			// PluginSessionService.Create / ProtocolLimits.MaxDeclaredNameLength. A self-registering
			// plugin is never seen by the host's own manifest reader, so catching a name that would fail
			// the handshake has to happen here, at build time, instead of on every connection attempt.
			if (manifest.Name.Length > ProtocolLimits.MaxDeclaredNameLength)
			{
				problems.Add($"The manifest's name is {manifest.Name.Length} characters long; " +
					$"at most {ProtocolLimits.MaxDeclaredNameLength} are allowed.");
			}

			if (manifest.Name.Any(char.IsControl))
			{
				problems.Add("The manifest's name must not contain control characters.");
			}
		}

		if (string.IsNullOrWhiteSpace(manifest.Version))
		{
			problems.Add(
				$"The manifest at '{Path.Combine(Environment.ContentRootPath, PluginManifestFileReader.FileName)}' " +
				"declares no version. Set \"version\" in manifest.json.");
		}

		// Declared at all, not merely non-empty: "icon": "" is a shape violation the host rejects, so
		// skipping it here would let an artifact build clean and then fail to install.
		if (manifest.Icon is not null)
		{
			// Shape first, existence second: an unsafe path like "../icon.png" must be reported as unsafe
			// even when a file happens to exist there.
			if (!PluginManifestFileReader.IsSafeRelativeIconPath(manifest.Icon))
			{
				problems.Add($"The manifest's icon path '{manifest.Icon}' is not a safe relative path.");
			}
			else
			{
				// Deliberately stricter than the host's own manifest reader: this runs against a tree the
				// author controls at build time, while the host reader runs on every launch of an already
				// installed artifact, where a missing icon must never stop an otherwise-working plugin.
				var resolved = Path.Combine(Environment.ContentRootPath,
					manifest.Icon.Replace('/', Path.DirectorySeparatorChar));

				if (!File.Exists(resolved))
				{
					problems.Add($"The manifest's icon '{manifest.Icon}' does not exist under the content root.");
				}
			}
		}

		return new PluginMetadata
		{
			Id = id ?? string.Empty,
			Name = manifest.Name ?? string.Empty,
			Version = manifest.Version ?? string.Empty,
			Description = manifest.Description,
			// Verbatim, forward-slash relative form - never a resolved absolute path, which would leak
			// the author's build-machine layout into the /_macrodeck/info response.
			IconPath = manifest.Icon
		};
	}

	private void AddSdkServices(PluginMetadata metadata, IconAssetSource iconSource)
	{
		Services.AddOptions<PluginHostOptions>()
			.Bind(Configuration.GetSection(PluginHostOptions.SectionName))
			.ValidateDataAnnotations()
			.ValidateOnStart();

		Services.AddHttpClient();
		Services.AddHttpClient(PluginRegistrationClient.HttpClientName)
			.ConfigureHttpClient((provider, client) =>
			{
				client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<PluginHostOptions>>().Value.HostUrl);
				client.Timeout = ProtocolTimeouts.DefaultRequest;
			});

		Services.TryAddSingleton(metadata);
		Services.TryAddSingleton(TimeProvider.System);

		// The SDK logs through Serilog, and so does a plugin author's own code once
		// UseMacroDeckLogging has attached the host sink. Resolved from Log.Logger rather than captured,
		// because UseSerilog replaces it while the application is being built - a captured logger would
		// pin whatever was installed before that and miss the host sink entirely. TryAdd, so the
		// registration Serilog's own UseSerilog contributes wins when there is one.
		Services.TryAddSingleton<Serilog.ILogger>(_ => Log.Logger);
		Services.TryAddSingleton(provider
			=> new PluginRegistrationModeAccessor(ResolveMode(provider
				.GetRequiredService<IOptions<PluginHostOptions>>())));

		Services.TryAddSingleton<IPluginCredentialStore>(provider
			=> provider.GetRequiredService<PluginRegistrationModeAccessor>().Mode == PluginRegistrationMode.Managed
				? ActivatorUtilities.CreateInstance<EnvironmentPluginCredentialStore>(provider)
				: ActivatorUtilities.CreateInstance<FilePluginCredentialStore>(provider));

		Services.TryAddSingleton<PluginRegistrationClient>();
		Services.TryAddSingleton<PluginPairingClient>();
		Services.TryAddSingleton<PluginConnectionState>();
		Services.TryAddSingleton<CapabilityCatalog>();
		Services.TryAddSingleton<CapabilityDispatcher>();

		// The callback direction (#413 step 6): host.invoke/host.result, host.state and the
		// IIntegrationContext proxies built on top of them.
		Services.TryAddSingleton<IHostInvoker, HostInvoker>();
		Services.TryAddSingleton<HostStateCache>();

		// The asset pipeline (#413 step 9): the chunked upload the icons kind (and oversized
		// music-player artwork) push over. The IconAssetSource is registered unconditionally - even as
		// None - because IconAssetPublisherHostedService always takes one, and answers "no icon" by never
		// subscribing. The capability handler below is the part that is gated: a plugin whose manifest
		// names no icon must declare no icons capability at all, exactly as one with no icon provider did
		// before the icon moved into the manifest.
		Services.TryAddSingleton<IPluginAssetUploader, PluginAssetUploader>();
		Services.TryAddSingleton<IPluginHostAssetReceiver, PluginHostAssetReceiver>();
		Services.TryAddSingleton(iconSource);

		if (iconSource.HasIcon)
		{
			Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, IconsCapabilityHandler>());
		}

		// Same shape as the icon above: the source is always registered so the handler has one to read,
		// and the capability itself stays undeclared when there is nothing to serve.
		Services.TryAddSingleton(_localizationCatalog is null
			? PluginLocalizationSource.None
			: new PluginLocalizationSource(_localizationCatalog));

		if (_localizationCatalog is not null)
		{
			Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICapabilityHandler, LocalizationCapabilityHandler>());
		}

		Services.TryAddSingleton<RemoteVariableApi>();
		Services.TryAddSingleton<RemoteUserVariableApi>();
		Services.TryAddSingleton<RemoteIntegrationConfig>();
		Services.TryAddSingleton<RemoteDeckNavigator>();
		Services.TryAddSingleton<RemoteScriptApi>();
		Services.TryAddSingleton<RemoteWidgetApi>();
		Services.TryAddSingleton<RemoteEventPublisher>();
		Services.TryAddSingleton<RemoteUserNotifier>();
		Services.TryAddSingleton<IDeviceProviderContext, RemoteDeviceProviderContext>();
		Services.TryAddSingleton<ILayoutProviderContext, RemoteLayoutProviderContext>();
		Services.TryAddSingleton<IFolderViewProviderContext, RemoteFolderViewProviderContext>();
		Services.TryAddSingleton<IWidgetTypeProviderContext, RemoteWidgetTypeProviderContext>();
		Services.TryAddSingleton<ModalResultStore>();
		Services.TryAddSingleton<IIntegrationContext, RemoteIntegrationContext>();

		// The producer half of state.update (#413's remote weather-location bug fix): a plugin author
		// injects this directly, not through IIntegrationContext - unlike Events/Notifications, it is
		// not scoped to one integration's identity, so it does not belong on that per-integration proxy.
		Services.TryAddSingleton<IPluginCatalogNotifier, PluginCatalogNotifier>();

		Services.TryAddScoped<CapabilityInvocationContextHolder>();
		Services.TryAddScoped<ICapabilityInvocationContext>(provider
			=> provider.GetRequiredService<CapabilityInvocationContextHolder>().Current ??
			throw new InvalidOperationException(
				"There is no capability invocation in this scope. ICapabilityInvocationContext can only " +
				"be resolved inside a capability handler's invocation."));

		Services.AddSingleton<IStartupFilter, ReservedPathStartupFilter>();

		// Registered first, but StartAsync only subscribes to the connection's Connected event -
		// initialization itself is gated on that event, not on hosted-service start order (see
		// IntegrationLifecycleHostedService's remarks).
		Services.Insert(0, ServiceDescriptor.Singleton<IHostedService, IntegrationLifecycleHostedService>());

		// The icon publisher subscribes to the same Connected event as integration lifecycle, so its
		// order relative to that insert does not matter - both only start doing anything once a session
		// is open.
		Services.AddHostedService<IconAssetPublisherHostedService>();

		// Independent of the connection: a killed host is never seen over the socket at all, so this
		// cannot wait for one to exist.
		Services.AddHostedService<HostLivenessHostedService>();
	}

	private PluginRegistrationMode ResolveMode(IOptions<PluginHostOptions> options)
	{
		if (_mode is { } explicitly)
		{
			return explicitly;
		}

		if (options.Value.Mode is { } configured)
		{
			return configured;
		}

		// A plugin the host launched has both halves of a credential it never asked for. Anything else
		// has to obtain one itself.
		return !string.IsNullOrEmpty(options.Value.Id) && !string.IsNullOrEmpty(options.Value.Secret)
			? PluginRegistrationMode.Managed
			: PluginRegistrationMode.SelfRegistering;
	}

	/// <summary>
	/// Resolves and validates what the plugin will declare.
	///
	/// <para>
	/// Handlers and integrations are resolved here rather than trusted to resolve later, because
	/// <c>ValidateOnBuild</c> does not cover services only reachable through
	/// <c>IEnumerable&lt;T&gt;</c> - which is exactly how both are registered.
	/// </para>
	/// </summary>
	private static IEnumerable<string> ValidateCapabilities(IServiceProvider services, PluginMetadata metadata)
	{
		using var scope = services.CreateScope();

		try
		{
			_ = scope.ServiceProvider.GetServices<IPluginIntegration>().ToList();
		}
		catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
		{
			return [$"An integration could not be constructed: {exception.Message}"];
		}

		CapabilityCatalog catalog;
		try
		{
			catalog = scope.ServiceProvider.GetRequiredService<CapabilityCatalog>();
		}
		catch (InvalidOperationException exception)
		{
			return [$"A capability handler could not be constructed: {exception.Message}"];
		}

		return PluginCapabilityValidator
			.Validate(metadata.Id, catalog.Declare())
			.Select(conflict => conflict.ToString());
	}

	/// <summary>
	/// Just enough of a container to construct a startup class with the three things it may legitimately
	/// ask for. Disposed immediately: it exists for one <c>ActivatorUtilities</c> call.
	/// </summary>
	private static TStartup CreateStartup<TStartup>(PluginHostBuilderContext context)
		where TStartup : class
	{
		using var bootstrap = new ServiceCollection()
			.AddSingleton(context.Configuration)
			.AddSingleton(context.HostEnvironment)
			.AddSingleton(context.Metadata)
			.AddSingleton(context)
			.BuildServiceProvider();

		return ActivatorUtilities.CreateInstance<TStartup>(bootstrap);
	}
}
