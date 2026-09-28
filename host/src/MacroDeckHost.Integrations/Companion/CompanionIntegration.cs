using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

[MacroDeckIntegration]
public sealed class CompanionIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IEventProvider,
		IIntegrationIconProvider,
		ICompanionGatewayConsumer,
		IVariableRefreshSignalConsumer,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.companion";

	private static readonly byte[] _icon = LoadIcon();

	private readonly SemaphoreSlim _reloadGate = new(1, 1);
	private readonly Lock _announceGate = new();
	private readonly Dictionary<Guid, long> _announced = new();

	private ICompanionGateway? _gateway;
	private IVariableRefreshSignal? _refreshSignal;
	private IVariableApi? _variables;
	private IUserVariableApi? _userVariables;
	private IIntegrationContext? _context;
	private IEventPublisher? _events;
	private CompanionRuntime[] _runtimes = [];

	public CompanionIntegration()
	{
		Actions = CompanionActions.Create(new CompanionTargetResolver(RuntimeSnapshot, () => _gateway),
			() => _variables,
			() => _userVariables);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.Companion.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<VariableDefinition> Variables
		=> RuntimeSnapshot()
			.SelectMany(runtime => CompanionVariables.Declare(runtime.Identity.Key, runtime.Id, runtime.Title))
			.ToList();

	public IReadOnlyList<VariableDefinition> DeclaredVariables
	{
		get
		{
			var provided = Variables;
			return provided.Count > 0 ? provided : CompanionVariables.Templates;
		}
	}

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<EventDefinition> EventDefinitions => CompanionEventDefinitions.All;

	internal IReadOnlyList<CompanionRuntime> Runtimes => RuntimeSnapshot();

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new CompanionConfigFlow(() => _gateway);

	public void UseGateway(ICompanionGateway gateway) => _gateway = gateway;

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_variables = context.Variables;
		_userVariables = context.UserVariables;
		lock (_announceGate)
		{
			_events = context.Events;
		}

		if (_gateway is { } gateway)
		{
			gateway.StateChanged -= OnStateChanged;
			gateway.StateChanged += OnStateChanged;
			gateway.DeviceReady -= OnDeviceReady;
			gateway.DeviceReady += OnDeviceReady;
		}

		await ReloadConfigurationsAsync();
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		if (_gateway is { } gateway)
		{
			gateway.StateChanged -= OnStateChanged;
			gateway.DeviceReady -= OnDeviceReady;
		}

		lock (_announceGate)
		{
			_events = null;
		}

		Volatile.Write(ref _runtimes, []);
		_context = null;
		_variables = null;
		_userVariables = null;
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		ShutdownAsync().GetAwaiter().GetResult();
		_reloadGate.Dispose();
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (!CompanionVariables.TrySplit(localId, RuntimeSnapshot(), out var runtime, out var slot))
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var state = _gateway is { } gateway && gateway.TryGetState(runtime.Id, out var current) ? current : null;
		return ValueTask.FromResult(CompanionVariables.Read(state, slot));
	}

	internal bool IsConnected(Guid entryId) => _gateway is { } gateway && gateway.TryGetState(entryId, out _);

	internal async Task ReloadConfigurationsAsync(CancellationToken cancellationToken = default)
	{
		if (_context is not { } context)
		{
			return;
		}

		await _reloadGate.WaitAsync(cancellationToken);
		try
		{
			var runtimes = new List<CompanionRuntime>();
			foreach (var entry in await context.Config.GetEntriesAsync(cancellationToken))
			{
				var schema = await context.Config.GetStringAsync(entry.Id,
					CompanionConfigurationMetadata.SchemaKey,
					cancellationToken);
				var identityJson = await context.Config.GetStringAsync(entry.Id,
					CompanionConfigurationMetadata.VariableIdentityKey,
					cancellationToken);
				if (string.Equals(schema, CompanionConfigurationMetadata.SchemaVersion, StringComparison.Ordinal) &&
					ObsConfigurationMetadata.TryParseIdentity(identityJson, out var identity))
				{
					runtimes.Add(new CompanionRuntime(entry.Id, entry.Title, identity));
				}
			}

			Volatile.Write(ref _runtimes, runtimes.ToArray());
		}
		finally
		{
			_reloadGate.Release();
		}

		_refreshSignal?.RequestEagerRefresh(IntegrationId);
		if (_gateway is { } gateway)
		{
			foreach (var ready in gateway.ReadyDevices())
			{
				Announce(ready);
			}
		}
	}

	private void OnDeviceReady(object? sender, CompanionDeviceReady ready) => Announce(ready);

	private void Announce(CompanionDeviceReady ready)
	{
		if (RuntimeSnapshot().FirstOrDefault(runtime => runtime.Id == ready.DeviceId) is not { } runtime)
		{
			return;
		}

		IEventPublisher events;
		lock (_announceGate)
		{
			if (_events is null || _announced.GetValueOrDefault(ready.DeviceId) == ready.Sequence)
			{
				return;
			}

			_announced[ready.DeviceId] = ready.Sequence;
			events = _events;
		}

		events.Publish(CompanionEventDefinitions.DeviceReady,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["deviceId"] = ready.DeviceId.ToString(),
				["deviceName"] = runtime.Title
			});
	}

	private void OnStateChanged(object? sender, Guid deviceId) => _refreshSignal?.RequestEagerRefresh(IntegrationId);

	private CompanionRuntime[] RuntimeSnapshot() => Volatile.Read(ref _runtimes);

	private static byte[] LoadIcon()
	{
		var assembly = typeof(CompanionIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("companion-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
