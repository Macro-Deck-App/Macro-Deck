using System.Diagnostics;
using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Plugin.Testing.Internal;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Drives a plugin's capability handlers directly, in process, with no socket at all - the fast path
/// for a test that does not need to prove the wire itself works, only that the plugin's own code does.
///
/// <para>
/// Built from the same <see cref="PluginHostBuilder" /> a plugin author already writes, with two
/// substitutions applied through the same public <c>ConfigureServices</c> override every author already
/// has: <see cref="TimeProvider" /> becomes <see cref="Clock" />, and <c>IIntegrationContext</c> becomes
/// <see cref="Context" />, a <see cref="FakeIntegrationContext" />. Neither substitution can be
/// declined - a harness with a real <c>IIntegrationContext</c> would have no host on the other end of
/// it to answer.
/// </para>
///
/// <para>
/// Skips everything <c>CapabilityDispatcher</c> normally does around a handler - concurrency limiting,
/// idempotency replay - except the invocation deadline, which <see cref="InvokeAsync" /> enforces itself
/// with a linked <see cref="CancellationTokenSource" />. The wire path enforces the same deadline inside
/// the dispatcher instead; both produce the same observable <see cref="CapabilityInvocationOutcome" />; only
/// the mechanism differs.
/// </para>
/// </summary>
public sealed class PluginTestHarness : IAsyncDisposable, ICapabilityInvoker
{
	private readonly PluginApplication _application;
	private readonly PluginTestManifest? _ownedManifest;
	private readonly Logger _collectingLogger;

	private PluginTestHarness(
		PluginApplication application,
		FakeIntegrationContext context,
		ManualTimeProvider clock,
		PluginLogCollector logs,
		Logger collectingLogger,
		PluginTestManifest? ownedManifest)
	{
		_application = application;
		_ownedManifest = ownedManifest;
		_collectingLogger = collectingLogger;
		Context = context;
		Clock = clock;
		Logs = logs;

		Actions = new ActionsTestClient(this);
		Variables = new VariablesTestClient(this);
		Events = new EventsTestClient(this);
		Icons = new IconsTestClient(this);
		ConfigFlow = new ConfigFlowTestClient(this);
		MusicPlayer = new MusicPlayerTestClient(this);
		Weather = new WeatherTestClient(this);
		VirtualProfiles = new VirtualProfilesTestClient(this);
		DeviceProvider = new DeviceProviderTestClient(this);
		LayoutProvider = new LayoutProviderTestClient(this);
		FolderViewProvider = new FolderViewProviderTestClient(this);
		WidgetTypeProvider = new WidgetTypeProviderTestClient(this);
		Issues = new IssuesTestClient(this);
	}

	/// <summary>The plugin's own service provider - the escape hatch for anything else this type does not surface.</summary>
	public IServiceProvider Services => _application.Services;

	/// <summary>The fake <c>IIntegrationContext</c> every integration in this plugin was built with.</summary>
	public FakeIntegrationContext Context { get; }

	/// <summary>The clock every <see cref="TimeProvider" /> in this plugin was built with.</summary>
	public ManualTimeProvider Clock { get; }

	/// <summary>Everything this plugin has logged so far - captured directly from its logging pipeline, not over a wire.</summary>
	public PluginLogCollector Logs { get; }

	/// <summary>Everything every registered capability handler currently declares.</summary>
	public IReadOnlyList<DeclaredCapability> Declared
		=> [.. Services.GetServices<ICapabilityHandler>().SelectMany(handler => handler.DeclareCapabilities())];

	/// <summary>The <c>actions</c> capability.</summary>
	public ActionsTestClient Actions { get; }

	/// <summary>The <c>variables</c> capability.</summary>
	public VariablesTestClient Variables { get; }

	/// <summary>The <c>events</c> capability.</summary>
	public EventsTestClient Events { get; }

	/// <summary>The <c>icons</c> capability.</summary>
	public IconsTestClient Icons { get; }

	/// <summary>The <c>config-flow</c> capability.</summary>
	public ConfigFlowTestClient ConfigFlow { get; }

	/// <summary>The <c>music-player</c> capability.</summary>
	public MusicPlayerTestClient MusicPlayer { get; }

	/// <summary>The <c>weather</c> capability.</summary>
	public WeatherTestClient Weather { get; }

	/// <summary>The <c>virtual-profiles</c> capability.</summary>
	public VirtualProfilesTestClient VirtualProfiles { get; }

	/// <summary>The <c>device-provider</c> capability.</summary>
	public DeviceProviderTestClient DeviceProvider { get; }

	/// <summary>The <c>layout-provider</c> capability.</summary>
	public LayoutProviderTestClient LayoutProvider { get; }

	/// <summary>The <c>folder-view-provider</c> capability.</summary>
	public FolderViewProviderTestClient FolderViewProvider { get; }

	/// <summary>The <c>widget-type-provider</c> capability.</summary>
	public WidgetTypeProviderTestClient WidgetTypeProvider { get; }

	/// <summary>The <c>issues</c> capability.</summary>
	public IssuesTestClient Issues { get; }

	/// <summary>
	/// Builds a plugin with <paramref name="configure" />, substituting <see cref="Clock" /> and
	/// <see cref="Context" /> in, and returns it built but not started - no hosted service runs, so call
	/// <see cref="InitializeIntegrationsAsync" /> explicitly before invoking anything that expects an
	/// integration to already be initialized.
	/// </summary>
	/// <param name="configure">Configures the plugin, as passed to <c>MacroDeckPlugin.CreatePlugin()</c>.</param>
	/// <param name="manifest">
	/// The plugin's identity. Defaults to a fresh <see cref="PluginTestManifest" /> when omitted - owned
	/// and disposed with the returned harness - so the common case needs no manifest of its own. Pass one
	/// explicitly only when the test is about a specific id, name, version, description or icon; that
	/// instance is then the caller's to dispose.
	/// </param>
	/// <exception cref="PluginConfigurationException">The plugin as configured cannot run. See <see cref="ProblemsOf" /> to inspect this without throwing.</exception>
	public static PluginTestHarness Create(Action<PluginHostBuilder> configure, PluginTestManifest? manifest = null)
	{
		ArgumentNullException.ThrowIfNull(configure);

		var ownedManifest = manifest is null ? new PluginTestManifest() : null;

		var builder = MacroDeckPlugin.CreatePlugin();
		(manifest ?? ownedManifest!).Apply(builder);
		configure(builder);

		var clock = new ManualTimeProvider();
		var context = new FakeIntegrationContext();
		var logs = new PluginLogCollector();

		// Nothing is meant to reach a console from a test run; the framework's own MEL pipeline is the
		// only thing still writing to one now that the plugin logs through Serilog.
		builder.Logging.ClearProviders();

		// The plugin's Serilog pipeline, replaced wholesale by one that writes into the collector: the
		// harness has no wire for log.publish to travel over, so the events have to be picked up where
		// they are written. Registered below (after the author's own callbacks) so it wins over the
		// default the SDK registers, and over anything UseMacroDeckLogging would have installed.
		var collectingLogger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new CollectingLogSink(logs, clock))
			.CreateLogger();

		// Appended after the author's own ConfigureServices calls (which configure(builder) already
		// ran), so - per PluginHostBuilder.Build's documented resolution order - these two win over
		// whatever the plugin itself registered for the same service.
		builder.ConfigureServices((_, services) =>
		{
			services.AddSingleton<TimeProvider>(clock);
			services.AddSingleton<IIntegrationContext>(context);
			services.AddSingleton<Serilog.ILogger>(collectingLogger);
		});

		PluginApplication application;
		try
		{
			application = builder.Build();
		}
		catch
		{
			// Building failed - there is no returned harness for a caller to dispose, so a manifest owned
			// by this call, and the logger built above, would otherwise leak.
			collectingLogger.Dispose();
			ownedManifest?.Dispose();
			throw;
		}

		return new PluginTestHarness(application, context, clock, logs, collectingLogger, ownedManifest);
	}

	/// <summary>
	/// Every configuration problem <paramref name="configure" /> would produce, without throwing and
	/// without leaving a built application behind. Empty when the configuration is valid.
	/// </summary>
	/// <param name="configure">Configures the plugin, as passed to <c>MacroDeckPlugin.CreatePlugin()</c>.</param>
	/// <param name="manifest">
	/// The plugin's identity. Defaults to a fresh <see cref="PluginTestManifest" /> when omitted, disposed
	/// before this method returns either way. Pass one explicitly to prove a specific manifest value - a
	/// deliberately invalid id, for instance - produces the problem it should.
	/// </param>
	public static IReadOnlyList<string> ProblemsOf(
		Action<PluginHostBuilder> configure,
		PluginTestManifest? manifest = null)
	{
		ArgumentNullException.ThrowIfNull(configure);

		var ownedManifest = manifest is null ? new PluginTestManifest() : null;

		try
		{
			var builder = MacroDeckPlugin.CreatePlugin();
			(manifest ?? ownedManifest!).Apply(builder);
			configure(builder);

			try
			{
				using var application = builder.Build();
				return [];
			}
			catch (PluginConfigurationException exception)
			{
				return exception.Problems;
			}
		}
		finally
		{
			ownedManifest?.Dispose();
		}
	}

	/// <summary>
	/// Runs <c>InitializeAsync</c> on every registered integration, with <see cref="Context" />. Failures
	/// propagate rather than being logged and skipped - unlike <c>IntegrationLifecycleHostedService</c>'s
	/// isolation between integrations, a harness exists to surface exactly this kind of failure to the test.
	/// </summary>
	public async Task InitializeIntegrationsAsync()
	{
		foreach (var integration in Services.GetServices<IPluginIntegration>())
		{
			await integration.InitializeAsync(Context).ConfigureAwait(false);

			// Mirrors IntegrationLifecycleHostedService's own attach branch, gate included: attach a
			// push-capable catalog to Context.VariableValues so a test that publishes from the provider
			// can observe the pushes on that fake, with no wire and no separate attach step of its own.
			if (integration is IVariableProvider { SupportsCatalog: true, SupportsPush: true } catalog)
			{
				await catalog.OnAttachedAsync(Context.VariableValues).ConfigureAwait(false);
			}
		}
	}

	/// <inheritdoc cref="ICapabilityInvoker.InvokeAsync" />
	public async Task<CapabilityInvocationOutcome> InvokeAsync(
		string kind,
		string localId,
		string operation,
		object? arguments = null,
		CapabilityInvokeOptions? options = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(kind);
		ArgumentException.ThrowIfNullOrEmpty(localId);
		ArgumentException.ThrowIfNullOrEmpty(operation);

		if (options?.IdempotencyKey is { Length: > ProtocolLimits.MaxIdempotencyKeyLength })
		{
			throw new ArgumentException(
				$"The idempotency key exceeds {ProtocolLimits.MaxIdempotencyKeyLength} characters.",
				nameof(options));
		}

		// Mirrors VariablesCapabilityHandler.SubscribeAsync's own bookkeeping so Context.VariableValues
		// (the fake IVariableSink) drops a push the same way the real sink would - see
		// FakeVariableSink.PublishAsync.
		if (string.Equals(kind, CapabilityKinds.Variables, StringComparison.Ordinal) &&
			string.Equals(operation, CapabilityOperations.Variables.Subscribe, StringComparison.Ordinal) &&
			arguments is VariableSubscribeArguments subscribeArguments)
		{
			Context.VariableValues.SetSubscribed(subscribeArguments.Ids);
		}

		var correlationId = Guid.CreateVersion7().ToString();
		var stopwatch = Stopwatch.StartNew();

		var handler = Services.GetServices<ICapabilityHandler>()
			.FirstOrDefault(candidate => string.Equals(candidate.Kind, kind, StringComparison.Ordinal));

		if (handler is null)
		{
			return CapabilityInvocationOutcome.Failure(correlationId,
				new ProtocolError
				{
					Code = ProtocolErrorCodes.CapabilityUnsupported,
					Message = $"This plugin does not implement the '{kind}' capability.",
					Retryable = false
				},
				stopwatch.Elapsed);
		}

		var now = DateTimeOffset.UtcNow;
		var deadline = options?.Deadline ??
			(options?.Timeout is { } configuredTimeout ? now + configuredTimeout : null);

		await using var scope = Services.CreateAsyncScope();

		using var timeoutSource = new CancellationTokenSource();
		using var linked
			= CancellationTokenSource.CreateLinkedTokenSource(options?.CancellationToken ?? default,
				timeoutSource.Token);

		if (deadline is { } effectiveDeadline)
		{
			var remaining = effectiveDeadline - now;
			timeoutSource.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
		}

		var invocation = new CapabilityInvocation
		{
			Kind = kind,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = correlationId,
			IdempotencyKey = options?.IdempotencyKey,
			Deadline = deadline,
			Services = scope.ServiceProvider
		};

		CapabilityInvocationResult result;

		try
		{
			result = await handler.InvokeAsync(invocation, linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested &&
			!(options?.CancellationToken.IsCancellationRequested ?? false))
		{
			return CapabilityInvocationOutcome.Failure(correlationId,
				new ProtocolError
				{
					Code = ProtocolErrorCodes.Timeout,
					Message = ProtocolErrorMessages.For(ProtocolErrorCodes.Timeout),
					Retryable = false
				},
				stopwatch.Elapsed);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Mirrors ICapabilityHandler.InvokeAsync's own documented contract: a thrown exception is
			// caught and reported as an internal error rather than tearing anything down.
			return CapabilityInvocationOutcome.Failure(correlationId,
				new ProtocolError
				{
					Code = ProtocolErrorCodes.InternalError,
					Message = ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError),
					Retryable = false
				},
				stopwatch.Elapsed);
		}

		return result.Error is { } error
			? CapabilityInvocationOutcome.Failure(correlationId, error, stopwatch.Elapsed)
			: CapabilityInvocationOutcome.Success(correlationId, result.Data, stopwatch.Elapsed);
	}

	/// <summary>Disposes the underlying plugin application and the manifest this harness owns, if any.</summary>
	public async ValueTask DisposeAsync()
	{
		await _application.DisposeAsync();
		await _collectingLogger.DisposeAsync();
		_ownedManifest?.Dispose();
	}
}
