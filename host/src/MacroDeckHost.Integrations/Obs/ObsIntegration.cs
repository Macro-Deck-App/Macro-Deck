using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Obs.Actions;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Obs;

[MacroDeckIntegration]
public sealed class ObsIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IMigrationProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.obs";

	private static readonly ILogger _logger = IntegrationLog.For<ObsIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly VariableApiAccessor _variableAccessor = new();
	private readonly Func<Guid, IObsClient> _clientFactory;
	private readonly SemaphoreSlim _reloadGate = new(1, 1);
	private readonly ObsTargetResolver _targetResolver;
	private readonly ObsVariableCatalog _dynamicVariables;

	private IIntegrationContext? _context;
	private IReadOnlyList<ObsRuntime> _runtimes = [];

	public ObsIntegration()
		: this(_ => new ObsClient())
	{
	}

	internal ObsIntegration(Func<Guid, IObsClient> clientFactory)
	{
		_clientFactory = clientFactory;
		_targetResolver = new ObsTargetResolver(RuntimeSnapshot);
		_dynamicVariables = new ObsVariableCatalog(RuntimeSnapshot);
		Actions = ObsActions.Create(_targetResolver, _variableAccessor);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "OBS Studio";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => ObsEventDefinitions.All;

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<VariableDefinition> Variables
		=> RuntimeSnapshot()
			.SelectMany(runtime => ObsVariables.Declare(runtime.Identity.Key, runtime.Id, runtime.Title))
			.ToList();

	public IReadOnlyList<VariableDefinition> DeclaredVariables
	{
		get
		{
			var provided = Variables;
			return provided.Count > 0 ? provided : ObsVariables.Templates;
		}
	}

	public bool VariablesDependOnConfiguration => true;

	internal IReadOnlyList<ObsRuntime> Runtimes => RuntimeSnapshot();

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new ObsConfigFlow();

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new ObsMacroDeck2Migration()];

	public bool SupportsCatalog => true;

	public bool SupportsPush => ObsVariableCatalog.SupportsPush;

	public bool SupportsSearch => ObsVariableCatalog.SupportsSearch;

	public string CatalogName => ObsVariableCatalog.CatalogName;

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

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_variableAccessor.Current = context.Variables;
		await ReloadConfigurationsAsync();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		await _reloadGate.WaitAsync();
		try
		{
			var previous = RuntimeSnapshot();
			Volatile.Write(ref _runtimes, Array.Empty<ObsRuntime>());
			foreach (var runtime in previous)
			{
				await runtime.Connection.DisposeAsync();
			}

			_context = null;
			_variableAccessor.Current = null;
			IsInitialized = false;
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	public void Dispose()
	{
		ShutdownAsync().GetAwaiter().GetResult();
		_reloadGate.Dispose();
	}

	// The eager set and the catalog share one id space only in the sense that they never collide: an
	// eager id is "entry-<guid:N>-<slot>", a catalog id starts with the runtime guid in "D" format. Trying
	// the eager half first and falling through is what lets one ReadAsync serve both halves.
	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var runtimes = RuntimeSnapshot();
		return ObsVariables.TrySplit(localId, runtimes, out var runtime, out var slot)
			? ValueTask.FromResult(VariableReading.Of(ObsVariables.Read(runtime.Connection.State, slot)))
			: _dynamicVariables.ReadAsync(localId, cancellationToken);
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> _dynamicVariables.SetValueAsync(localId, value, cancellationToken);

	public async Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var runtimes = RuntimeSnapshot();
		if (context.ParameterName == ObsTargetResolver.ConfigurationParameter)
		{
			return new DynamicOptionsResult
			{
				Options = runtimes.Select(runtime => new ActionParameterOption
				{
					Value = runtime.Id.ToString("D"),
					Label = runtime.Title
				}).ToList(),
				CacheSeconds = 0
			};
		}

		var connection = ResolveEventConnection(context.CurrentParameters, runtimes);
		var values = connection is null
			? []
			: context.ParameterName switch
			{
				"sceneName" or "previousSceneName" => await connection.GetSceneNamesAsync(),
				"inputName" => await connection.GetInputNamesAsync(),
				_ => []
			};

		return new DynamicOptionsResult
		{
			Options = values.Select(value => new ActionParameterOption { Value = value }).ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
	}

	internal async Task ReloadConfigurationsAsync(CancellationToken cancellationToken = default)
	{
		var context = _context;
		if (context is null)
		{
			return;
		}

		await _reloadGate.WaitAsync(cancellationToken);
		try
		{
			var previous = RuntimeSnapshot();
			var desired = await LoadDesiredAsync(context.Config, cancellationToken);
			var next = new List<ObsRuntime>(desired.Count);
			var retained = new HashSet<ObsConnection>();

			foreach (var item in desired)
			{
				var existing = previous.FirstOrDefault(runtime =>
					runtime.Id == item.Id && runtime.Settings == item.Settings);
				if (existing is not null)
				{
					retained.Add(existing.Connection);
					next.Add(new ObsRuntime(item.Id, item.Title, item.Identity, item.Settings, existing.Connection));
					continue;
				}

				var events = new ObsEventEmitter(context.Events, item.Id);
				var connection = new ObsConnection(_clientFactory(item.Id),
					item.Settings.Url,
					item.Settings.Password,
					events: events);
				connection.Start();
				next.Add(new ObsRuntime(item.Id, item.Title, item.Identity, item.Settings, connection));
			}

			Volatile.Write(ref _runtimes, next.ToArray());
			foreach (var runtime in previous)
			{
				if (!retained.Contains(runtime.Connection))
				{
					await runtime.Connection.DisposeAsync();
				}
			}
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	internal bool TryGetStatus(Guid entryId, out ObsConnectionStatus status)
	{
		var runtime = RuntimeSnapshot().FirstOrDefault(item => item.Id == entryId);
		status = runtime?.Connection.Status ?? ObsConnectionStatus.Disconnected;
		return runtime is not null;
	}

	private IReadOnlyList<ObsRuntime> RuntimeSnapshot() => Volatile.Read(ref _runtimes);

	private static ObsConnection? ResolveEventConnection(
		IReadOnlyDictionary<string, object?> parameters,
		IReadOnlyList<ObsRuntime> runtimes)
	{
		if (parameters.GetValueOrDefault(ObsTargetResolver.ConfigurationParameter) is not string text ||
			!Guid.TryParseExact(text, "D", out var id))
		{
			return null;
		}

		var runtime = runtimes.FirstOrDefault(item => item.Id == id);
		return runtime?.Connection.IsConnected == true ? runtime.Connection : null;
	}

	private static async Task<IReadOnlyList<DesiredRuntime>> LoadDesiredAsync(
		IIntegrationConfig config,
		CancellationToken cancellationToken)
	{
		var desired = new List<DesiredRuntime>();
		foreach (var entry in await config.GetEntriesAsync(cancellationToken))
		{
			var schema = await config.GetStringAsync(entry.Id, ObsConfigurationMetadata.SchemaKey, cancellationToken);
			var identityJson =
				await config.GetStringAsync(entry.Id, ObsConfigurationMetadata.VariableIdentityKey, cancellationToken);
			if (!string.Equals(schema, ObsConfigurationMetadata.SchemaVersion, StringComparison.Ordinal) ||
				!ObsConfigurationMetadata.TryParseIdentity(identityJson, out var identity))
			{
				continue;
			}

			var host = await config.GetStringAsync(entry.Id, ObsConfigKeys.Host, cancellationToken);
			var portRaw = await config.GetStringAsync(entry.Id, ObsConfigKeys.Port, cancellationToken);
			if (string.IsNullOrWhiteSpace(host) ||
				!int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
				port is < 1 or > 65535)
			{
				_logger.Warning("OBS config entry {EntryId} is incomplete; skipping", entry.Id);
				continue;
			}

			var password = await config.GetSecretAsync(entry.Id, ObsConfigKeys.Password, cancellationToken);
			desired.Add(new DesiredRuntime(entry.Id,
				entry.Title,
				identity,
				new ObsConfigurationSettings(host, port, password)));
		}

		return desired;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(ObsIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("obs-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private sealed record DesiredRuntime(
		Guid Id,
		string Title,
		ObsConfigurationIdentity Identity,
		ObsConfigurationSettings Settings);
}
