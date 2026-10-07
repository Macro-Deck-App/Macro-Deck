using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Jellyfin;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Jellyfin.Actions;
using MacroDeckHost.Integrations.Jellyfin.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin;

namespace MacroDeckHost.Integrations.Jellyfin;

[MacroDeckIntegration]
public sealed class JellyfinIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IMusicPlayerProvider,
		IIntegrationIconProvider,
		IIntegrationIssueProvider,
		IWidgetTypeProvider,
		IVariableRefreshSignalConsumer,
		IVariablePollingInvalidationConsumer,
		IJellyfinSessionSource,
		IDisposable
{
	public const string IntegrationId = JellyfinWidgetTypes.OwnerId;

	internal const string VariableKeyConfigKey = "jellyfinVariableKey";

	private const string AuthIssueId = "authentication-failed";
	private const string UnreachableIssueId = "unreachable";

	private static readonly ILogger _logger = IntegrationLog.For<JellyfinIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly Func<JellyfinServerSettings, IJellyfinClient> _clientFactory;
	private readonly TimeProvider _time;
	private readonly JellyfinConnectionTimings? _timings;
	private readonly SemaphoreSlim _reloadGate = new(1, 1);
	private readonly Lock _deviceKeys = new();

	private IIntegrationContext? _context;
	private IVariableRefreshSignal? _refreshSignal;
	private IVariablePollingInvalidationSignal? _pollingInvalidation;
	private JellyfinRuntime[] _runtimes = [];

	public JellyfinIntegration()
		: this(settings => new JellyfinClient(settings))
	{
	}

	internal JellyfinIntegration(
		Func<JellyfinServerSettings, IJellyfinClient> clientFactory,
		TimeProvider? time = null,
		JellyfinConnectionTimings? timings = null)
	{
		_clientFactory = clientFactory;
		_time = time ?? TimeProvider.System;
		_timings = timings;
		Actions =
		[
			.. MusicPlayerActions.Common(ResolvePlayer, GetInstances),
			.. JellyfinActions.Create(ResolveJellyfinPlayer, GetInstances)
		];
	}

	public event EventHandler? SessionsChanged;

	public string Id => IntegrationId;

	public LocalizedText Name => "Jellyfin";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => JellyfinEventDefinitions.All;

	public bool AllowsMultipleConfigurations => true;

	public bool VariablesDependOnConfiguration => true;

	internal IReadOnlyList<JellyfinRuntime> Runtimes => Volatile.Read(ref _runtimes);

	public IReadOnlyList<VariableDefinition> Variables
	{
		get
		{
			var runtimes = Runtimes;
			if (runtimes.Count == 0)
			{
				return [];
			}

			var variables = new List<VariableDefinition> { JellyfinVariables.Total() };
			foreach (var runtime in runtimes)
			{
				variables.AddRange(JellyfinVariables.DeclareServer(runtime.VariableKey, runtime.EntryId, runtime.Title));
				foreach (var device in runtime.Devices.Devices)
				{
					variables.AddRange(JellyfinVariables.DeclareDevice(runtime.VariableKey,
						device.Key,
						runtime.EntryId,
						device.LocalId,
						runtime.Title));
				}
			}

			return variables;
		}
	}

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> Variables is { Count: > 0 } provided ? provided : JellyfinVariables.Templates;

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new JellyfinConfigFlow(_clientFactory);

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public void UseVariablePollingInvalidation(IVariablePollingInvalidationSignal signal)
		=> _pollingInvalidation = signal;

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		await ReloadConfigurationsAsync().ConfigureAwait(false);
		IsInitialized = true;
	}

	public Task ShutdownAsync()
	{
		foreach (var runtime in Interlocked.Exchange(ref _runtimes, []))
		{
			Detach(runtime);
		}

		IsInitialized = false;
		SessionsChanged?.Invoke(this, EventArgs.Empty);
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		foreach (var runtime in Interlocked.Exchange(ref _runtimes, []))
		{
			Detach(runtime);
		}

		_reloadGate.Dispose();
	}

	internal async Task ReloadConfigurationsAsync(CancellationToken cancellationToken = default)
	{
		var context = _context;
		if (context is null)
		{
			return;
		}

		await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var previous = Runtimes;
			var desired = await LoadDesiredAsync(context.Config, cancellationToken).ConfigureAwait(false);
			var next = new List<JellyfinRuntime>(desired.Count);
			var retained = new HashSet<JellyfinRuntime>();

			foreach (var item in desired)
			{
				var existing = previous.FirstOrDefault(runtime => runtime.EntryId == item.EntryId &&
					runtime.Title == item.Title &&
					runtime.VariableKey == item.VariableKey &&
					runtime.Settings == item.Settings &&
					runtime.Token == item.Token);
				if (existing is not null)
				{
					retained.Add(existing);
					next.Add(existing);
					continue;
				}

				var devices = new JellyfinDeviceRegistry();
				devices.Load(item.DevicesJson);
				var client = _clientFactory(new JellyfinServerSettings(item.BaseUri, item.Token, item.Settings.DeviceId));
				var connection = new JellyfinConnection(client, item.Settings.DeviceId, _time, _timings);
				var runtime = new JellyfinRuntime(item.EntryId,
					item.Title,
					item.VariableKey,
					item.Settings,
					connection,
					devices,
					item.Token,
					_time);
				connection.StateChanged += (_, args) => OnStateChanged(runtime, args);
				next.Add(runtime);
			}

			Volatile.Write(ref _runtimes, [.. next]);
			foreach (var runtime in previous.Where(runtime => !retained.Contains(runtime)))
			{
				Detach(runtime);
			}

			foreach (var runtime in next.Where(runtime => !retained.Contains(runtime)))
			{
				runtime.Connection.Start();
			}

			_pollingInvalidation?.MarkStale(IntegrationId);
			SessionsChanged?.Invoke(this, EventArgs.Empty);
		}
		finally
		{
			_reloadGate.Release();
		}
	}

	internal bool TryGetStatus(Guid entryId, out JellyfinConnectionStatus status)
	{
		var runtime = Runtimes.FirstOrDefault(item => item.EntryId == entryId);
		status = runtime?.Connection.State.Status ?? JellyfinConnectionStatus.Disconnected;
		return runtime is not null;
	}

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
	{
		var instances = new List<MusicPlayerInstance>();
		foreach (var runtime in Runtimes)
		{
			instances.Add(new MusicPlayerInstance(runtime.InstanceId, runtime.Title));
			instances.AddRange(runtime.Devices.Devices.Where(device => device.IsControllable).Select(device =>
				new MusicPlayerInstance(JellyfinRuntime.DeviceInstanceId(runtime.EntryId, device.LocalId),
					$"{runtime.Title} · {DeviceLabel(runtime, device)}")));
		}

		return instances;
	}

	private static string DeviceLabel(JellyfinRuntime runtime, JellyfinKnownDevice device)
	{
		var namesakes = runtime.Devices.Devices
			.Where(candidate => string.Equals(candidate.Name, device.Name, StringComparison.OrdinalIgnoreCase))
			.OrderBy(candidate => candidate.Key, StringComparer.Ordinal)
			.ToList();
		return namesakes.Count < 2
			? device.Name
			: $"{device.Name} ({namesakes.FindIndex(candidate => candidate.DeviceId == device.DeviceId) + 1})";
	}

	public IMusicPlayer? GetPlayer(string instanceId) => ResolveJellyfinPlayer(instanceId);

	private IMusicPlayer? ResolvePlayer(string? instanceId) => ResolveJellyfinPlayer(instanceId);

	private JellyfinMusicPlayer? ResolveJellyfinPlayer(string? instanceId)
	{
		var runtimes = Runtimes;
		if (string.IsNullOrEmpty(instanceId))
		{
			return runtimes
				.Select(runtime => (Runtime: runtime, Session: runtime.ActivePlayer.CurrentSession()))
				.OrderByDescending(pair => pair.Runtime.Connection.State.IsConnected)
				.ThenByDescending(pair => pair.Session?.IsActive == true)
				.ThenByDescending(pair => pair.Session?.LastActivity ?? DateTimeOffset.MinValue)
				.Select(pair => pair.Runtime.ActivePlayer)
				.FirstOrDefault();
		}

		var entryPart = instanceId.Length >= 32 ? instanceId[..32] : instanceId;
		if (!Guid.TryParseExact(entryPart, "N", out var entryId) ||
			runtimes.FirstOrDefault(runtime => runtime.EntryId == entryId) is not { } match)
		{
			return null;
		}

		if (instanceId.Length == 32)
		{
			return match.ActivePlayer;
		}

		return instanceId.Length > 33 && match.Devices.FindByLocalId(instanceId[33..]) is { } device
			? match.PlayerFor(device.DeviceId)
			: null;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var target = JellyfinVariables.Parse(localId);
		var runtimes = Runtimes;
		object? value = target.Kind switch
		{
			VariableTargetKind.Total => runtimes.Sum(runtime =>
				runtime.Connection.State.IsConnected ? runtime.Connection.State.ActiveSessions.Count : 0),
			VariableTargetKind.Server => runtimes.FirstOrDefault(runtime => runtime.EntryId == target.EntryId) is { } server
				? JellyfinVariables.ReadServer(server.Connection.State, target.Slot)
				: null,
			VariableTargetKind.Device => ReadDevice(runtimes, target),
			_ => null
		};

		if (target.Kind != VariableTargetKind.Device)
		{
			return ValueTask.FromResult(VariableReading.Of(value));
		}

		// Slider bounds ride on the reading: the seek range is the length of whatever plays right now.
		var duration = ResolveDevicePlayer(runtimes, target)?.CurrentSession()?.NowPlaying?.Duration;
		return ValueTask.FromResult(target.Slot switch
		{
			"volume" or "progress_percent" => VariableReading.Of(value, 0, 100, 1),
			"position" => VariableReading.Of(value, 0, duration is { } length ? (int)length.TotalSeconds : null, 1),
			_ => VariableReading.Of(value)
		});
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		var target = JellyfinVariables.Parse(localId);
		if (target.Kind != VariableTargetKind.Device || !JellyfinVariables.IsWritable(target.Slot))
		{
			return VariableWriteResult.NotWritable();
		}

		if (ResolveDevicePlayer(Runtimes, target) is not { } player)
		{
			return VariableWriteResult.NotFound();
		}

		try
		{
			switch (target.Slot)
			{
				case "volume":
					if (!MusicPlayerVariableWrites.TryReadNumber(value, out var volume))
					{
						return VariableWriteResult.InvalidValue();
					}

					await player.SetVolumeAsync((int)Math.Round(Math.Clamp(volume, 0, 100)), cancellationToken)
						.ConfigureAwait(false);
					break;
				case "position":
					if (!MusicPlayerVariableWrites.TryReadNumber(value, out var seconds))
					{
						return VariableWriteResult.InvalidValue();
					}

					await player.SeekAsync(TimeSpan.FromSeconds(Math.Max(0, seconds)), cancellationToken)
						.ConfigureAwait(false);
					break;
				case "progress_percent":
					if (!MusicPlayerVariableWrites.TryReadNumber(value, out var percent) ||
						player.CurrentSession()?.NowPlaying?.Duration is not { } length)
					{
						return VariableWriteResult.InvalidValue();
					}

					await player.SeekAsync(length * (Math.Clamp(percent, 0, 100) / 100), cancellationToken)
						.ConfigureAwait(false);
					break;
				case "is_playing" or "is_paused":
					if (!TryReadBoolean(value, out var flag))
					{
						return VariableWriteResult.InvalidValue();
					}

					await (flag == (target.Slot == "is_playing")
							? player.PlayAsync(cancellationToken)
							: player.PauseAsync(cancellationToken))
						.ConfigureAwait(false);
					break;
				case "is_muted":
					if (!TryReadBoolean(value, out var muted))
					{
						return VariableWriteResult.InvalidValue();
					}

					await player.GeneralCommandAsync(muted ? "Mute" : "Unmute", null, cancellationToken)
						.ConfigureAwait(false);
					break;
			}

			return VariableWriteResult.Applied();
		}
		catch (JellyfinCommandException ex)
		{
			return ex.Code is ActionErrorCodes.ProviderError
				? VariableWriteResult.Failed(ex.LocalizedMessage)
				: VariableWriteResult.Unavailable(ex.LocalizedMessage);
		}
	}

	private static JellyfinMusicPlayer? ResolveDevicePlayer(IReadOnlyList<JellyfinRuntime> runtimes, VariableTarget target)
	{
		var runtime = runtimes.FirstOrDefault(item => item.EntryId == target.EntryId);
		return runtime?.Devices.FindByLocalId(target.DeviceLocalId!) is { } device
			? runtime.PlayerFor(device.DeviceId)
			: null;
	}

	private static bool TryReadBoolean(object? value, out bool result)
	{
		switch (value)
		{
			case bool flag:
				result = flag;
				return true;
			case string text when bool.TryParse(text.Trim(), out var parsed):
				result = parsed;
				return true;
			case string text when text.Trim() is "1" or "0":
				result = text.Trim() == "1";
				return true;
			default:
				if (MusicPlayerVariableWrites.TryReadNumber(value, out var number))
				{
					result = number != 0;
					return true;
				}

				result = false;
				return false;
		}
	}

	private object? ReadDevice(IReadOnlyList<JellyfinRuntime> runtimes, VariableTarget target)
	{
		var runtime = runtimes.FirstOrDefault(item => item.EntryId == target.EntryId);
		var device = runtime?.Devices.FindByLocalId(target.DeviceLocalId!);
		if (runtime is null || device is null)
		{
			return null;
		}

		var state = runtime.Connection.State;
		var session = state.IsConnected ? runtime.PlayerFor(device.DeviceId).Resolve(state) : null;
		return JellyfinVariables.ReadDevice(session, device, target.Slot, _time.GetUtcNow());
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context, CancellationToken cancellationToken)
	{
		var runtimes = Runtimes;
		if (context.ParameterName == JellyfinEventPayload.Configuration)
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					.. runtimes.Select(runtime => new ActionParameterOption
						{ Value = runtime.EntryId.ToString("D"), Label = runtime.Title })
				],
				CacheSeconds = 0
			});
		}

		if (context.ParameterName == JellyfinEventPayload.DeviceId)
		{
			var server = context.CurrentParameters.GetValueOrDefault(JellyfinEventPayload.Configuration) as string;
			return Task.FromResult(new DynamicOptionsResult
			{
				Options =
				[
					.. runtimes
						.Where(runtime => string.IsNullOrEmpty(server) ||
							string.Equals(runtime.EntryId.ToString("D"), server, StringComparison.OrdinalIgnoreCase))
						.SelectMany(runtime => runtime.Devices.Devices.Select(device => new ActionParameterOption
						{
							Value = device.DeviceId,
							Label = string.IsNullOrEmpty(server)
								? $"{runtime.Title} · {DeviceLabel(runtime, device)}"
								: DeviceLabel(runtime, device)
						}))
				],
				CacheSeconds = 0
			});
		}

		return Task.FromResult(new DynamicOptionsResult { Options = [] });
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = new List<IntegrationIssue>();
		var runtimes = Runtimes;

		var rejected = runtimes.Where(runtime =>
			runtime.Connection.State.Status == JellyfinConnectionStatus.AuthenticationFailed).ToList();
		if (rejected.Count > 0)
		{
			issues.Add(new IntegrationIssue
			{
				Id = AuthIssueId,
				Title = Strings.Issues.AuthenticationFailedTitle(),
				Description = Strings.Issues.AuthenticationFailedDescription(
					servers: string.Join(", ", rejected.Select(runtime => runtime.Title))),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = Strings.Issues.OpenSetupAction()
			});
		}

		var unreachable = runtimes.Where(runtime =>
			runtime.Connection.State.Status == JellyfinConnectionStatus.Disconnected).ToList();
		if (unreachable.Count > 0)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnreachableIssueId,
				Title = Strings.Issues.UnreachableTitle(),
				Description = Strings.Issues.UnreachableDescription(
					servers: string.Join(", ", unreachable.Select(runtime => runtime.Title))),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId == AuthIssueId
			? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed());

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		foreach (var widgetType in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(widgetType, cancellationToken).ConfigureAwait(false);
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
		=>
		[
			new(JellyfinWidgetTypes.SessionsLocalId,
				Strings.Widget.Name(),
				Strings.Widget.Description(),
				DefaultData: JellyfinWidgetTypes.DefaultData,
				DataSchema: JellyfinWidgetTypes.DataSchema,
				HasConfiguration: true)
			{
				AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
				SupportsFlows = true
			}
		];

	public IReadOnlyList<JellyfinServerSummary> GetServers()
	{
		var now = _time.GetUtcNow();
		return
		[
			.. Runtimes.Select(runtime =>
			{
				var state = runtime.Connection.State;
				return new JellyfinServerSummary(runtime.EntryId.ToString("D"),
					runtime.Title,
					state.IsConnected,
					state.IsConnected ? [.. state.ActiveSessions.Select(session => Summarize(runtime, session, now))] : []);
			})
		];
	}

	private static JellyfinSessionSummary Summarize(JellyfinRuntime runtime, JellyfinSession session, DateTimeOffset now)
	{
		var item = session.NowPlaying!;
		var subtitle = item.IsEpisode ? item.SeriesName :
			item.IsAudio && item.Artists.Count > 0 ? string.Join(", ", item.Artists) :
			item.DisplayAlbum;
		return new JellyfinSessionSummary(session.Id,
			session.UserName,
			session.DeviceName,
			session.Client,
			item.Name,
			subtitle,
			session.IsPaused ? JellyfinSessionPlayback.Paused : JellyfinSessionPlayback.Playing,
			session.ProgressPercentAt(now))
		{
			IsAudio = item.IsAudio,
			LastActivity = session.LastActivity,
			ArtworkId = runtime.RegisterArtwork(item),
			ArtworkInstanceId = $"{IntegrationId}::{runtime.InstanceId}"
		};
	}

	private void OnStateChanged(JellyfinRuntime runtime, JellyfinStateChangedEventArgs args)
	{
		if (!Runtimes.Contains(runtime))
		{
			return;
		}

		bool devicesChanged;
		lock (_deviceKeys)
		{
			devicesChanged = args.Current.IsConnected &&
				runtime.Devices.Observe(args.Current.Sessions, _time.GetUtcNow(), key => IsDeviceKeyTaken(runtime, key));
		}

		if (devicesChanged)
		{
			_ = PersistDevicesAsync(runtime);
			_pollingInvalidation?.MarkStale(IntegrationId);
		}

		var context = _context;
		if (context is not null)
		{
			foreach (var item in JellyfinEventDiff.Compute(runtime.EntryId, runtime.Title, args.Previous, args.Current))
			{
				context.Events.Publish(item.Id, item.Payload);
			}
		}

		_refreshSignal?.RequestEagerRefresh(IntegrationId);
		SessionsChanged?.Invoke(this, EventArgs.Empty);
	}

	private bool IsDeviceKeyTaken(JellyfinRuntime runtime, string deviceKey)
	{
		var candidates = JellyfinVariables.DeviceSlotNames
			.Select(slot => JellyfinVariables.DeviceVariableName(runtime.VariableKey, deviceKey, slot))
			.ToHashSet(StringComparer.Ordinal);

		foreach (var other in Runtimes.Where(other => !ReferenceEquals(other, runtime)))
		{
			if (JellyfinVariables.ServerSlotNames.Any(slot =>
					candidates.Contains(JellyfinVariables.ServerVariableName(other.VariableKey, slot))) ||
				other.Devices.Devices.Any(device => JellyfinVariables.DeviceSlotNames.Any(slot =>
					candidates.Contains(JellyfinVariables.DeviceVariableName(other.VariableKey, device.Key, slot)))))
			{
				return true;
			}
		}

		return false;
	}

	private async Task PersistDevicesAsync(JellyfinRuntime runtime)
	{
		var context = _context;
		if (context is null)
		{
			return;
		}

		await runtime.PersistGate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (!Runtimes.Contains(runtime))
			{
				return;
			}

			await context.Config.SetStringAsync(runtime.EntryId, JellyfinConfigKeys.Devices, runtime.Devices.Serialize())
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not store the known Jellyfin devices of {Server}", runtime.Title);
		}
		finally
		{
			runtime.PersistGate.Release();
		}
	}

	private static void Detach(JellyfinRuntime runtime) => runtime.Dispose();

	private static async Task<IReadOnlyList<DesiredRuntime>> LoadDesiredAsync(
		IIntegrationConfig config,
		CancellationToken cancellationToken)
	{
		var desired = new List<DesiredRuntime>();
		var usedKeys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var entry in await config.GetEntriesAsync(cancellationToken).ConfigureAwait(false))
		{
			var url = await config.GetStringAsync(entry.Id, JellyfinConfigKeys.Url, cancellationToken).ConfigureAwait(false);
			var method = await config.GetStringAsync(entry.Id, JellyfinConfigKeys.AuthMethod, cancellationToken)
				.ConfigureAwait(false) ?? JellyfinConfigKeys.AuthApiKey;
			var token = method == JellyfinConfigKeys.AuthLogin
				? await config.GetSecretAsync(entry.Id, JellyfinConfigKeys.AccessToken, cancellationToken).ConfigureAwait(false)
				: await config.GetSecretAsync(entry.Id, JellyfinConfigKeys.ApiKey, cancellationToken).ConfigureAwait(false);
			var deviceId = await config.GetStringAsync(entry.Id, JellyfinConfigKeys.DeviceId, cancellationToken)
				.ConfigureAwait(false);

			if (!JellyfinConfigFlow.TryParseUrl(url ?? string.Empty, out var baseUri) || string.IsNullOrEmpty(token))
			{
				_logger.Warning("Jellyfin config entry {EntryId} is incomplete; skipping", entry.Id);
				continue;
			}

			if (string.IsNullOrEmpty(deviceId))
			{
				deviceId = Guid.NewGuid().ToString("N");
				await config.SetStringAsync(entry.Id, JellyfinConfigKeys.DeviceId, deviceId, cancellationToken)
					.ConfigureAwait(false);
			}

			var key = await config.GetStringAsync(entry.Id, VariableKeyConfigKey, cancellationToken).ConfigureAwait(false);
			if (string.IsNullOrEmpty(key) || !usedKeys.Add(key))
			{
				key = FallbackKey(entry.Title, usedKeys);
			}

			var username = await config.GetStringAsync(entry.Id, JellyfinConfigKeys.Username, cancellationToken)
				.ConfigureAwait(false);
			var devices = await config.GetStringAsync(entry.Id, JellyfinConfigKeys.Devices, cancellationToken)
				.ConfigureAwait(false);

			desired.Add(new DesiredRuntime(entry.Id,
				entry.Title,
				key,
				baseUri!,
				token,
				new JellyfinRuntimeSettings(baseUri!.ToString(), method, username, deviceId),
				devices));
		}

		return desired;
	}

	private static string FallbackKey(string title, HashSet<string> used)
	{
		var stem = JellyfinKeys.Stem(title, "server", JellyfinVariables.MaxServerKeyLength - 5);

		var key = stem;
		for (var suffix = 2; !used.Add(key); suffix++)
		{
			key = $"{stem}_{suffix}";
		}

		return key;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(JellyfinIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("jellyfin-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private sealed record DesiredRuntime(
		Guid EntryId,
		string Title,
		string VariableKey,
		Uri BaseUri,
		string Token,
		JellyfinRuntimeSettings Settings,
		string? DevicesJson);
}
