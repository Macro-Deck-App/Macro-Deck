using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Integrations.Scripts;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Delegation;

internal sealed class DelegateRemote : IDisposable
{
	private static readonly TimeSpan InitialProbeDelay = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan MaxProbeDelay = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan ScriptRefreshInterval = TimeSpan.FromSeconds(60);
	internal static readonly TimeSpan SharedVariableInterval = TimeSpan.FromSeconds(2);
	internal static readonly TimeSpan SharingUnsupportedRecheck = TimeSpan.FromMinutes(5);
	private const int MaxConcurrentRuns = 4;

	private readonly IDelegateClient _client;
	private readonly IIntegrationConfig? _config;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly DelegateSession _session;
	private readonly SemaphoreSlim _inFlight = new(MaxConcurrentRuns, MaxConcurrentRuns);
	private readonly SemaphoreSlim _configWrite = new(1, 1);

	private volatile IReadOnlyDictionary<string, DelegateScriptSummary> _scripts;
	private DateTimeOffset _lastScriptRefresh = DateTimeOffset.MinValue;
	private CancellationTokenSource? _loopCts;
	private Task? _loopTask;
	private Task? _sharedLoopTask;

	private volatile IReadOnlyDictionary<string, DelegateSharedVariable> _shared;
	private volatile bool _sharedFresh;
	private DateTimeOffset _sharingUnsupportedUntil = DateTimeOffset.MinValue;

	public DelegateRemote(
		DelegateInstance instance,
		string password,
		IDelegateClient client,
		IIntegrationConfig? config,
		TimeProvider time,
		ILogger logger,
		IReadOnlyDictionary<string, DelegateScriptSummary>? initialScripts = null,
		bool importSharedVariables = false,
		IReadOnlyList<DelegateSharedVariable>? knownSharedVariables = null)
	{
		Instance = instance;
		_client = client;
		_config = config;
		_time = time;
		_logger = logger;
		_session = new DelegateSession(client, instance.BaseUrl, instance.Username, password, time);
		_scripts = initialScripts ?? new Dictionary<string, DelegateScriptSummary>(StringComparer.Ordinal);
		ImportsSharedVariables = importSharedVariables;
		_shared = importSharedVariables
			? (knownSharedVariables ?? []).ToDictionary(v => v.Name, StringComparer.Ordinal)
			: new Dictionary<string, DelegateSharedVariable>(StringComparer.Ordinal);

		SelfDelegationDetected = DelegateEndpoint.IsThisMachine(instance.BaseUrl, instance.MachineName);
	}

	public DelegateInstance Instance { get; }

	public bool ImportsSharedVariables { get; }

	public IReadOnlyCollection<DelegateSharedVariable> SharedVariables => _shared.Values.ToList();

	public event Action<DelegateRemote>? SharedVariablesChanged;

	public bool IsReachable { get; private set; }

	public DateTimeOffset? UnreachableSince { get; private set; }

	public bool SelfDelegationDetected { get; }

	public bool CredentialsRejected => _session.CredentialsRejected;

	public DateTimeOffset? ThrottledUntil => _session.ThrottledUntil;

	public IReadOnlyList<ActionParameterOption> ScriptOptions()
		=> _scripts.Select(pair => new ActionParameterOption
			{
				Value = pair.Key,
				Label = pair.Value.Name,
				Metadata = ScriptInputParameters.Metadata(pair.Value.Inputs, pair.Value.RunsOnWidget)
			})
			.ToList();

	public void Start()
	{
		_loopCts = new CancellationTokenSource();
		_loopTask = RunProbeLoopAsync(_loopCts.Token);
		if (ImportsSharedVariables)
		{
			_sharedLoopTask = RunSharedVariableLoopAsync(_loopCts.Token);
		}
	}

	public async Task StopAsync()
	{
		if (_loopCts is null)
		{
			return;
		}

		await _loopCts.CancelAsync();
		try
		{
			await Task.WhenAll(new[] { _loopTask, _sharedLoopTask }.OfType<Task>());
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_loopCts.Dispose();
			_loopCts = null;
			_loopTask = null;
			_sharedLoopTask = null;
		}
	}

	public void Dispose()
	{
		_loopCts?.Cancel();
		_loopCts?.Dispose();
		_loopCts = null;
		_client.Dispose();
		_inFlight.Dispose();
		_configWrite.Dispose();
		_session.Dispose();
	}

	public async Task<ActionResult> RunScriptAsync(
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		if (!await _inFlight.WaitAsync(0, cancellationToken))
		{
			return ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				AppStrings.Integrations.Delegation.Errors.TooManyRunning(label: Instance.Label));
		}

		try
		{
			if (!IsReachable)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Delegation.Errors.CouldNotBeReached(label: Instance.Label));
			}

			using var timeoutCts = new CancellationTokenSource(timeout, _time);
			using var linked =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

			string token;
			try
			{
				token = await _session.GetTokenAsync(linked.Token);
			}
			catch (Exception ex) when (MapFailure(ex, cancellationToken) is { } mapped)
			{
				return mapped;
			}

			return await RunWithTokenAsync(token, scriptId, clientId, callDepth, inputs, linked, cancellationToken);
		}
		finally
		{
			_inFlight.Release();
		}
	}

	private async Task<ActionResult> RunWithTokenAsync(
		string token,
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		CancellationTokenSource linked,
		CancellationToken originalCt)
	{
		try
		{
			return await ExecuteRun(token, scriptId, clientId, callDepth, inputs, linked.Token);
		}
		catch (DelegateUnauthorizedException)
		{
			string retryToken;
			try
			{
				retryToken = await _session.RenewAfterUnauthorizedAsync(linked.Token);
			}
			catch (Exception renewEx) when (MapFailure(renewEx, originalCt) is { } mapped)
			{
				return mapped;
			}

			try
			{
				return await ExecuteRun(retryToken, scriptId, clientId, callDepth, inputs, linked.Token);
			}
			catch (Exception retryEx) when (MapFailure(retryEx, originalCt) is { } mapped)
			{
				return mapped;
			}
		}
		catch (Exception ex) when (MapFailure(ex, originalCt) is { } mapped)
		{
			return mapped;
		}
	}

	private async Task<ActionResult> ExecuteRun(
		string token,
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		CancellationToken cancellationToken)
	{
		var result = await _client.RunScriptAsync(Instance.BaseUrl,
			token,
			scriptId,
			clientId,
			callDepth,
			inputs,
			cancellationToken);

		if (!result.Success)
		{
			return ActionResult.Failed(ActionErrorCodes.ProviderError,
				result.Error is { Length: > 0 } error
					? AppStrings.Integrations.Delegation.Errors.ScriptFailedWithError(label: Instance.Label,
						error: error)
					: AppStrings.Integrations.Delegation.Errors.ScriptFailed(label: Instance.Label));
		}

		// The remote reports which inputs it applied. A remote that does not know about inputs omits the
		// field entirely, which reads as "applied none" - it has already run the script on its defaults, so
		// reporting success would quietly hide that the values were dropped.
		if (inputs is { Count: > 0 })
		{
			var applied = result.AppliedInputs ?? [];
			var ignored = inputs.Keys.Where(name => !applied.Contains(name, StringComparer.Ordinal)).ToList();
			if (ignored.Count > 0)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderRejected,
					AppStrings.Integrations.Delegation.Errors.InputsNotApplied(label: Instance.Label,
						script: ScriptLabel(scriptId),
						inputs: string.Join(", ", ignored)));
			}
		}

		return ActionResult.Success();
	}

	public DelegateSharedVariable? ReadShared(string name)
		=> ImportsSharedVariables &&
			IsReachable &&
			_sharedFresh &&
			_shared.TryGetValue(name, out var variable) &&
			variable is { Present: true, Available: true }
				? variable
				: null;

	public async Task<VariableWriteResult> WriteSharedAsync(string name,
		string? value,
		CancellationToken cancellationToken)
	{
		if (!ImportsSharedVariables || !_shared.ContainsKey(name))
		{
			return VariableWriteResult.NotFound();
		}

		if (!IsReachable)
		{
			return VariableWriteResult.Unavailable(
				AppStrings.Integrations.Delegation.Errors.CouldNotBeReached(label: Instance.Label));
		}

		DelegateWriteResult result;
		try
		{
			result = await WithTokenAsync(
				token => _client.SetSharedVariableAsync(Instance.BaseUrl, token, name, value, cancellationToken),
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Delegate shared variable write failed for {Instance}", Instance.Label);
			return VariableWriteResult.Unavailable(
				AppStrings.Integrations.Delegation.Errors.CouldNotBeReached(label: Instance.Label));
		}

		if (result.Success)
		{
			return VariableWriteResult.Applied();
		}

		_logger.Debug("{Instance} refused a shared variable write: {Code}", Instance.Label, result.ErrorCode);
		return result.ErrorCode switch
		{
			"HOST_LOCKED" => VariableWriteResult.Failed(
				AppStrings.Integrations.Delegation.Errors.RemoteLocked(label: Instance.Label)),
			"InvalidValue" => VariableWriteResult.InvalidValue(
				AppStrings.Integrations.Delegation.Errors.RemoteRejectedChange(label: Instance.Label)),
			"NotWritable" => VariableWriteResult.NotWritable(
				AppStrings.Integrations.Delegation.Errors.RemoteRejectedChange(label: Instance.Label)),
			_ => VariableWriteResult.Failed(
				AppStrings.Integrations.Delegation.Errors.RemoteRejectedChange(label: Instance.Label))
		};
	}

	private async Task<T> WithTokenAsync<T>(Func<string, Task<T>> call, CancellationToken cancellationToken)
	{
		var token = await _session.GetTokenAsync(cancellationToken);
		try
		{
			return await call(token);
		}
		catch (DelegateUnauthorizedException)
		{
			return await call(await _session.RenewAfterUnauthorizedAsync(cancellationToken));
		}
	}

	private async Task RunSharedVariableLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			if (IsReachable && _time.GetUtcNow() >= _sharingUnsupportedUntil)
			{
				await RefreshSharedVariablesAsync(cancellationToken);
			}
			else if (!IsReachable)
			{
				_sharedFresh = false;
			}

			try
			{
				await Task.Delay(SharedVariableInterval, _time, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	internal async Task RefreshSharedVariablesAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<DelegateSharedVariable> fetched;
		try
		{
			fetched = await WithTokenAsync(
				token => _client.GetSharedVariablesAsync(Instance.BaseUrl, token, cancellationToken),
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (DelegateSharingUnsupportedException)
		{
			_sharingUnsupportedUntil = _time.GetUtcNow() + SharingUnsupportedRecheck;
			fetched = [];
		}
		catch (Exception ex)
		{
			_sharedFresh = false;
			_logger.Debug(ex, "Delegate shared variable refresh failed for {Instance}", Instance.Label);
			return;
		}

		var previous = _shared;
		var next = new Dictionary<string, DelegateSharedVariable>(StringComparer.Ordinal);
		foreach (var variable in fetched)
		{
			next[variable.Name] = variable;
		}

		_shared = next;
		_sharedFresh = true;

		if (SameShape(previous, next))
		{
			return;
		}

		try
		{
			await StoreAsync(DelegateConfigKeys.SharedVariables,
				JsonSerializer.Serialize(next.Values.Select(v => v with { Value = string.Empty }), DelegateJson.Options),
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Delegate could not store the shared variables of {Instance}", Instance.Label);
		}

		SharedVariablesChanged?.Invoke(this);
	}

	private async Task StoreAsync(string key, string value, CancellationToken cancellationToken)
	{
		if (_config is null)
		{
			return;
		}

		await _configWrite.WaitAsync(cancellationToken);
		try
		{
			await _config.SetStringAsync(Instance.EntryId, key, value, cancellationToken);
		}
		finally
		{
			_configWrite.Release();
		}
	}

	private static bool SameShape(
		IReadOnlyDictionary<string, DelegateSharedVariable> previous,
		Dictionary<string, DelegateSharedVariable> next)
		=> previous.Count == next.Count &&
			next.All(pair => previous.TryGetValue(pair.Key, out var old) && ShapeOf(old) == ShapeOf(pair.Value));

	private static DelegateSharedVariable ShapeOf(DelegateSharedVariable variable)
		=> variable with
		{
			Value = string.Empty,
			Present = false,
			Available = false,
			Min = null,
			Max = null,
			Step = null
		};

	internal static IReadOnlyList<DelegateSharedVariable> ParseSharedVariables(string? stored)
	{
		if (string.IsNullOrWhiteSpace(stored))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<List<DelegateSharedVariable>>(stored, DelegateJson.Options)?
				.Where(v => !string.IsNullOrEmpty(v.Name))
				.Select(v => v with { Present = false, Available = false })
				.ToList() ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private string ScriptLabel(string scriptId)
		=> _scripts.TryGetValue(scriptId, out var summary) ? summary.Name : scriptId;

	private ActionResult? MapFailure(Exception ex, CancellationToken originalCt) => ex switch
	{
		OperationCanceledException when !originalCt.IsCancellationRequested =>
			ActionResult.Failed(ActionErrorCodes.Timeout,
				AppStrings.Integrations.Delegation.Errors.RunTimedOut(label: Instance.Label)),
		OperationCanceledException => null,
		DelegateCredentialsRejectedException or DelegateUnauthorizedException =>
			ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				AppStrings.Integrations.Delegation.Errors.SignInRejected(label: Instance.Label)),
		DelegateThrottledException =>
			ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				AppStrings.Integrations.Delegation.Errors.LockedOut(label: Instance.Label)),
		DelegateRateLimitedException =>
			ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				AppStrings.Integrations.Delegation.Errors.LockedOut(label: Instance.Label)),
		DelegateNotFoundException =>
			ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Delegation.Errors.ScriptNoLongerExists(label: Instance.Label)),
		DelegateForbiddenException =>
			ActionResult.Failed(ActionErrorCodes.PermissionDenied,
				AppStrings.Integrations.Delegation.Errors.CannotRunScripts(label: Instance.Label)),
		DelegateDepthExceededException =>
			ActionResult.Failed(ActionErrorCodes.ProviderRejected,
				AppStrings.Integrations.Delegation.Errors.DelegateCycle(label: Instance.Label)),
		DelegateServerErrorException =>
			ActionResult.Failed(ActionErrorCodes.ProviderError,
				AppStrings.Integrations.Delegation.Errors.UnexpectedError(label: Instance.Label)),
		DelegateUnreachableException or DelegateTlsException or DelegateNotMacroDeckException =>
			ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.Delegation.Errors.CouldNotBeReached(label: Instance.Label)),
		_ => null
	};

	private async Task RunProbeLoopAsync(CancellationToken cancellationToken)
	{
		var delay = InitialProbeDelay;
		while (!cancellationToken.IsCancellationRequested)
		{
			await ProbeOnceAsync(cancellationToken);
			delay = IsReachable
				? InitialProbeDelay
				: TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxProbeDelay.TotalSeconds));

			try
			{
				await Task.Delay(delay, _time, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task ProbeOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _client.ProbeAsync(Instance.BaseUrl, cancellationToken);
			IsReachable = true;
			UnreachableSince = null;
			await MaybeRefreshScriptsAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			IsReachable = false;

			UnreachableSince ??= _time.GetUtcNow();

			_logger.Debug(ex, "Delegate probe failed for {Instance}", Instance.Label);
		}
	}

	private async Task MaybeRefreshScriptsAsync(CancellationToken cancellationToken)
	{
		var now = _time.GetUtcNow();
		if (_lastScriptRefresh != DateTimeOffset.MinValue && now - _lastScriptRefresh < ScriptRefreshInterval)
		{
			return;
		}

		try
		{
			var token = await _session.GetTokenAsync(cancellationToken);
			var scripts = await _client.GetScriptsAsync(Instance.BaseUrl, token, cancellationToken);
			var map = new Dictionary<string, DelegateScriptSummary>(StringComparer.Ordinal);
			foreach (var script in scripts)
			{
				map[script.Id] = script;
			}

			_scripts = map;
			_lastScriptRefresh = now;

			await StoreAsync(DelegateConfigKeys.RemoteScripts,
				JsonSerializer.Serialize(map, DelegateJson.Options),
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Delegate script refresh failed for {Instance}", Instance.Label);
		}
	}

	internal static IReadOnlyDictionary<string, DelegateScriptSummary> ParseScripts(string? stored)
	{
		var parsed = new Dictionary<string, DelegateScriptSummary>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(stored))
		{
			return parsed;
		}

		try
		{
			using var document = JsonDocument.Parse(stored);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return parsed;
			}

			foreach (var property in document.RootElement.EnumerateObject())
			{
				// Entries written before scripts could declare inputs are a bare id -> name map.
				if (property.Value.ValueKind == JsonValueKind.String)
				{
					parsed[property.Name] =
						new DelegateScriptSummary(property.Name, property.Value.GetString() ?? property.Name, []);
					continue;
				}

				var summary = property.Value.Deserialize<DelegateScriptSummary>(DelegateJson.Options);
				parsed[property.Name] = new DelegateScriptSummary(property.Name,
					summary?.Name ?? property.Name,
					summary?.Inputs ?? [],
					summary?.RunsOnWidget ?? false);
			}
		}
		catch (JsonException)
		{
			return new Dictionary<string, DelegateScriptSummary>(StringComparer.Ordinal);
		}

		return parsed;
	}

	internal static DateTimeOffset? ParseTimestamp(string? value)
		=> DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;
}
