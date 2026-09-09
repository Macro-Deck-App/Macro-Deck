using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Integrations.HomeAssistant.Actions;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant;

[MacroDeckIntegration]
public sealed class HomeAssistantIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IHomeAssistantBindingStoreConsumer,
		IMigrationProvider,
		IVariableRefreshSignalConsumer,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.homeassistant";

	internal const string AuthInvalidIssueId = "auth-invalid";
	internal const string CertificateIssueId = "certificate-untrusted";
	internal const string UnreachableIssueId = "unreachable";
	internal const string MissingEntitiesIssueId = "missing-entities";

	private const int NamedMissingEntities = 5;

	private static readonly ILogger _logger = IntegrationLog.For<HomeAssistantIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly HomeAssistantVariableAccessor _variableAccessor = new();
	private readonly HomeAssistantVariableCatalog _dynamicVariables;

	private HomeAssistantConnection? _connection;
	private IVariableRefreshSignal? _refreshSignal;
	private HomeAssistantEventEmitter? _events;
	private IIntegrationConfig? _config;
	private IVariableBindingStore? _bindingStore;

	public HomeAssistantIntegration()
	{
		Actions = HomeAssistantActions.Create(() => _connection, _variableAccessor);
		_dynamicVariables = new HomeAssistantVariableCatalog(() => _connection?.Catalog ?? HomeAssistantCatalog.Empty,
			entityIds => _connection?.UpdateWatchedEntities(entityIds));
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Home Assistant";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => HomeAssistantEventDefinitions.All;

	public bool AllowsMultipleConfigurations => false;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new HomeAssistantMacroDeck2Migration()];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("homeassistant_is_connected",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.HomeAssistant.Variables.IsConnected()
			},
		VariableDefinition.Eager("homeassistant_version", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.HomeAssistant.Variables.Version()
			},
		VariableDefinition.Eager("homeassistant_location_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.HomeAssistant.Variables.LocationName()
			},
		VariableDefinition.Eager("homeassistant_entity_count",
				VariableType.Numeric,
				refreshInterval: TimeSpan.FromSeconds(30))
			with
			{
				DisplayName = AppStrings.Integrations.HomeAssistant.Variables.EntityCount()
			}
	];

	public byte[] GetIcon() => _icon;

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public IConfigFlow CreateConfigFlow() => new HomeAssistantConfigFlow(() => new HomeAssistantClient());

	public bool SupportsCatalog => true;

	public bool SupportsPush => HomeAssistantVariableCatalog.SupportsPush;

	public bool SupportsSearch => HomeAssistantVariableCatalog.SupportsSearch;

	public string CatalogName => HomeAssistantVariableCatalog.CatalogName;

	public ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
		=> _dynamicVariables.DiscoverAsync(query, cancellationToken);

	public async ValueTask<VariableDefinition?> ResolveAsync(
		string localId,
		CancellationToken cancellationToken = default)
	{
		var eager = Variables.FirstOrDefault(v => string.Equals(v.ResolvedId, localId, StringComparison.Ordinal));
		return eager ?? await _dynamicVariables.ResolveAsync(localId, cancellationToken).ConfigureAwait(false);
	}

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken = default)
		=> _dynamicVariables.SubscribeAsync(localIds, cancellationToken);

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default)
		=> _dynamicVariables.OnAttachedAsync(sink, cancellationToken);

	public void UseBindingStore(IVariableBindingStore store) => _bindingStore = store;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;
		_events = new HomeAssistantEventEmitter(context.Events);
		_config = context.Config;

		// Must finish before ConnectFromConfig reads WatchedEntities: a successful migration clears that
		// key, and the migration's own bindings - not the config-flow leftovers - are what should back the
		// user's ha_* variables from here on.
		await RunWatchedEntityMigrationAsync(context.Config);

		await ConnectFromConfig(context);
		IsInitialized = true;
	}

	private async Task RunWatchedEntityMigrationAsync(IIntegrationConfig config)
	{
		if (_bindingStore is not { } store)
		{
			// No caller has provided a binding store (e.g. a unit test constructing this integration
			// directly, or a host build that has not wired IHomeAssistantBindingStoreConsumer yet). Nothing
			// to migrate into, so this simply defers to the next start that does provide one.
			return;
		}

		var migration = new HomeAssistantWatchedEntityMigration(config);
		if (await migration.HasRunAsync())
		{
			return;
		}

		// TryLoad, not Load: a transient read failure must not be mistaken for "no bindings" and overwrite
		// whatever is already on disk. Simply retrying on the next start is safe either way, since
		// HasRunAsync is never set below this point.
		if (!store.TryLoad(out var existing))
		{
			return;
		}

		var produced = await migration.BuildAsync(existing);
		if (produced.Count > 0 && !store.Save(existing.Concat(produced).ToList()))
		{
			// The write did not durably land - leave HasRunAsync false so the next start retries from
			// scratch rather than marking a migration complete that never actually persisted.
			return;
		}

		// Only after the bindings are durably saved: a crash before this point simply leaves HasRunAsync
		// false, and the next start re-runs BuildAsync, which skips everything already saved.
		await migration.MarkCompletedAsync();
	}

	public Task ShutdownAsync()
	{
		_connection?.Dispose();
		_connection = null;

		IsInitialized = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_connection?.Dispose();
		_connection = null;
	}

	// An eager id is "homeassistant-<slot>" and a catalog id is "entity/<entity_id>/<child>", so the two
	// halves of this provider never collide and anything the eager switch does not claim belongs to the
	// catalog.
	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var state = _connection?.State ?? HomeAssistantState.Disconnected;

		return localId switch
		{
			"homeassistant-is-connected" => ValueTask.FromResult(VariableReading.Of(state.IsConnected)),
			"homeassistant-version" => ValueTask.FromResult(VariableReading.Of(state.Version)),
			"homeassistant-location-name" => ValueTask.FromResult(VariableReading.Of(state.LocationName)),
			"homeassistant-entity-count" => ValueTask.FromResult(VariableReading.Of(state.EntityCount)),
			_ => _dynamicVariables.ReadAsync(localId, cancellationToken)
		};
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _connection;
		var domain = context.CurrentParameters.GetValueOrDefault("domain") as string;

		var result = (context.EventId, context.ParameterName) switch
		{
			(HomeAssistantEventIds.EntityStateChanged or HomeAssistantEventIds.Event, "entityId") =>
				HomeAssistantOptions.Entities(connection, context.Filter, domain),
			(HomeAssistantEventIds.EntityStateChanged, "domain") => HomeAssistantOptions.Domains(connection,
				context.Filter),
			(HomeAssistantEventIds.EntityStateChanged, "toState" or "fromState") => HomeAssistantOptions.Values(
				KnownStates(connection, domain),
				context.Filter),
			(HomeAssistantEventIds.Event, "eventType") => HomeAssistantOptions.EventTypes(connection, context.Filter),
			_ => HomeAssistantOptions.Values([], context.Filter)
		};

		return Task.FromResult(result);
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		var issues = new List<IntegrationIssue>();

		if (connection is null)
		{
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
		}

		if (connection.AuthInvalid)
		{
			issues.Add(new IntegrationIssue
			{
				Id = AuthInvalidIssueId,
				Title = AppStrings.Integrations.HomeAssistant.Issues.AuthInvalidTitle(),
				Description = AppStrings.Integrations.HomeAssistant.Issues.AuthInvalidDescription(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.HomeAssistant.Issues.OpenSetupAction()
			});
		}

		if (connection.CertificateUntrusted)
		{
			issues.Add(new IntegrationIssue
			{
				Id = CertificateIssueId,
				Title = AppStrings.Integrations.HomeAssistant.Issues.CertificateUntrustedTitle(),
				Description = AppStrings.Integrations.HomeAssistant.Issues.CertificateUntrustedDescription(
					host: connection.Uri.Host),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = AppStrings.Integrations.HomeAssistant.Issues.OpenSetupAction()
			});
		}

		if (connection.Unreachable)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnreachableIssueId,
				Title = AppStrings.Integrations.HomeAssistant.Issues.UnreachableTitle(),
				Description = AppStrings.Integrations.HomeAssistant.Issues.UnreachableDescription(
					uri: connection.Uri.ToString()),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.HomeAssistant.Issues.OpenSetupAction()
			});
		}

		if (connection.MissingEntities is { Count: > 0 } missing)
		{
			issues.Add(new IntegrationIssue
			{
				Id = MissingEntitiesIssueId,
				Title = AppStrings.Integrations.HomeAssistant.Issues.MissingEntitiesTitle(count: missing.Count),
				Description = AppStrings.Integrations.HomeAssistant.Issues.MissingEntitiesDescription(
					entities: Describe(missing)),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = AppStrings.Integrations.HomeAssistant.Issues.OpenSetupAction()
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId switch
		{
			AuthInvalidIssueId or CertificateIssueId or UnreachableIssueId or MissingEntitiesIssueId =>
				IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow),
			_ => IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue())
		});

	private static IReadOnlyList<string> KnownStates(HomeAssistantConnection? connection, string? domain)
	{
		var states = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var entity in (connection?.Catalog ?? HomeAssistantCatalog.Empty).Entities.Values)
		{
			if (entity.State.Length > 0 &&
				(domain is not { Length: > 0 } || string.Equals(entity.Domain, domain, StringComparison.Ordinal)))
			{
				states.Add(entity.State);
			}
		}

		return [.. states];
	}

	private static string Describe(IReadOnlyList<string> missing)
	{
		var named = string.Join(", ", missing.Take(NamedMissingEntities));
		return missing.Count > NamedMissingEntities
			? $"{named} and {missing.Count - NamedMissingEntities} more"
			: named;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(HomeAssistantIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("home-assistant-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private async Task ConnectFromConfig(IIntegrationContext context)
	{
		_connection?.Dispose();
		_connection = null;

		var entries = await context.Config.GetEntriesAsync();
		var entry = entries.Count > 0 ? entries[0] : null;
		if (entry is null)
		{
			_logger.Information("Home Assistant not configured; skipping connection");
			return;
		}

		var baseUrl = await context.Config.GetStringAsync(entry.Id, HomeAssistantConfigKeys.BaseUrl);
		var token = (await context.Config.GetSecretAsync(entry.Id, HomeAssistantConfigKeys.Token))?.Trim();
		var uri = HomeAssistantEndpoint.TryBuild(baseUrl);

		if (uri is null || string.IsNullOrEmpty(token))
		{
			_logger.Warning("Home Assistant config entry {EntryId} is incomplete; skipping", entry.Id);
			return;
		}

		var watchedEntities = HomeAssistantConfigKeys.ParseEntities(
			await context.Config.GetStringAsync(entry.Id, HomeAssistantConfigKeys.WatchedEntities));

		_connection = new HomeAssistantConnection(() => new HomeAssistantClient(),
			uri,
			token,
			watchedEntities,
			_events,
			_dynamicVariables,
			onVariablesChanged: () => _refreshSignal?.RequestEagerRefresh(IntegrationId));
		_connection.Start();
	}
}
