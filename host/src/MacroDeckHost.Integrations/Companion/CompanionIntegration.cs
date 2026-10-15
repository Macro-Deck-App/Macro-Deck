using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Integrations.Companion.Configuration;
using MacroDeckHost.Integrations.Companion.Events;
using MacroDeckHost.Integrations.Companion.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion;

[MacroDeckIntegration]
public sealed partial class CompanionIntegration
	: IIntegration,
		IConfigFlowProvider,
		IConfigEntryLifecycle,
		ICompanionDeviceConfigEntries,
		IVariableProvider,
		IEventProvider,
		IIntegrationIconProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.companion";

	private static readonly byte[] _icon = LoadIcon();

	private readonly SemaphoreSlim _reloadGate = new(1, 1);
	private readonly Lock _announceGate = new();
	private readonly Dictionary<Guid, long> _announced = new();
	private readonly Func<int>? _suffixFactory;

	private ICompanionDeviceApi? _devices;
	private IVariableApi? _variables;
	private IUserVariableApi? _userVariables;
	private IIntegrationContext? _context;
	private IEventPublisher? _events;
	private CompanionRuntime[] _runtimes = [];

	public CompanionIntegration()
		: this(null)
	{
	}

	internal CompanionIntegration(Func<int>? suffixFactory)
	{
		_suffixFactory = suffixFactory;
		Actions = CompanionActions.Create(new CompanionTargetResolver(RuntimeSnapshot, () => _devices),
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

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new CompanionConfigFlow();
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		_variables = context.Variables;
		_userVariables = context.UserVariables;
		lock (_announceGate)
		{
			_events = context.Events;
		}

		Unsubscribe();
		var devices = context.CompanionDevices;
		devices.DeviceChanged += OnDeviceChanged;
		devices.DeviceReady += OnDeviceReady;
		Volatile.Write(ref _devices, devices);

		await ReloadConfigurationsAsync();
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		Unsubscribe();

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

		return ValueTask.FromResult(CompanionVariables.Read(Volatile.Read(ref _devices)?.FindDevice(runtime.Id)?.State,
			slot));
	}

	internal bool IsConnected(Guid entryId)
	{
		return Volatile.Read(ref _devices)?.FindDevice(entryId) is not null;
	}

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
				if (string.Equals(schema, CompanionConfigurationMetadata.SchemaVersion, StringComparison.Ordinal)
					&& ConfigurationIdentityJson.TryParse(identityJson, out var identity))
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

		_ = _variables?.RequestRefreshAsync(CancellationToken.None);
		if (Volatile.Read(ref _devices) is { } devices)
		{
			foreach (var device in devices.Devices)
			{
				Announce(device);
			}
		}
	}

	private void Unsubscribe()
	{
		if (Interlocked.Exchange(ref _devices, null) is { } devices)
		{
			devices.DeviceChanged -= OnDeviceChanged;
			devices.DeviceReady -= OnDeviceReady;
		}
	}

	private void OnDeviceReady(object? sender, CompanionDeviceEventArgs e)
	{
		if (e.Device is { } device)
		{
			Announce(device);
		}
	}

	private void Announce(CompanionDevice ready)
	{
		if (RuntimeSnapshot().FirstOrDefault(runtime => runtime.Id == ready.Id) is not { } runtime)
		{
			return;
		}

		IEventPublisher events;
		lock (_announceGate)
		{
			if (_events is null || _announced.GetValueOrDefault(ready.Id) == ready.ReadySequence)
			{
				return;
			}

			_announced[ready.Id] = ready.ReadySequence;
			events = _events;
		}

		events.Publish(CompanionEventDefinitions.DeviceReady,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["deviceId"] = ready.Id.ToString(),
				["deviceName"] = runtime.Title
			});
	}

	private void OnDeviceChanged(object? sender, CompanionDeviceEventArgs e)
	{
		_ = _variables?.RequestRefreshAsync();
	}

	private CompanionRuntime[] RuntimeSnapshot()
	{
		return Volatile.Read(ref _runtimes);
	}

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
