using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>Proxies <see cref="IVariableApi"/> over <c>host.invoke</c> against <see cref="HostApis.Variables"/>.
/// Every member is a request/response round trip - the host owns the variable store, so there is
/// nothing to cache here.</summary>
internal sealed class RemoteVariableApi(
	IHostInvoker invoker,
	IPluginCatalogNotifier? catalog = null,
	TimeProvider? time = null) : IVariableApi
{
	private readonly Lock _refreshGate = new();
	private Task? _refreshInFlight;
	private Task? _refreshQueued;
	private PendingRefresh? _queuedRefresh;

	public async Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);
		return result?.Deserialize<List<VariableHandle>>(PluginProtocolJson.Options) ?? [];
	}

	public async Task<VariableHandle?> GetByNameAsync(string name)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Get,
			new VariablesGetArguments { Name = name },
			CancellationToken.None);
		return result?.Deserialize<VariableHandle?>(PluginProtocolJson.Options);
	}

	public async Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		var result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Create,
			new VariablesCreateArguments
			{
				Name = name,
				Type = type.ToString(),
				InitialValue = initialValue is null
					? null
					: JsonSerializer.SerializeToElement(initialValue, PluginProtocolJson.Options),
				DecimalPlaces = decimalPlaces,
				DefinitionId = definitionId
			},
			CancellationToken.None);

		return result?.Deserialize<VariableHandle>(PluginProtocolJson.Options)
			?? throw new InvalidOperationException($"The host returned no variable for '{name}'.");
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		return invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Set,
			new VariablesSetArguments
			{
				VariableId = variableId,
				Value = value is null ? null : JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options)
			},
			CancellationToken.None);
	}

	public Task DeleteAsync(Guid variableId)
	{
		return invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Delete,
			new VariablesDeleteArguments { VariableId = variableId },
			CancellationToken.None);
	}

	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		return EnqueueRefresh(null, cancellationToken);
	}

	public Task RequestDefinitionRefreshAsync(
		IReadOnlyCollection<string> definitionIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitionIds);
		var ids = definitionIds.Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
		return ids.Count == 0 ? Task.CompletedTask : EnqueueRefresh(ids, cancellationToken);
	}

	public async Task<IReadOnlyList<VariableBindingOutcome>> BindAsync(
		IReadOnlyCollection<VariableBindingRequest> requests,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count == 0)
		{
			return [];
		}

		if (requests.Count > ProtocolLimits.MaxVariableBindingsPerCall)
		{
			throw new ArgumentException(
				$"One call records at most {ProtocolLimits.MaxVariableBindingsPerCall} bindings.",
				nameof(requests));
		}

		var bindings = new List<VariableBindingRequestDto>(requests.Count);
		foreach (var request in requests)
		{
			if (request is null || !VariableNames.IsValid(request.Name))
			{
				throw new ArgumentException($"'{request?.Name}' is not a canonical variable name.", nameof(requests));
			}

			bindings.Add(new VariableBindingRequestDto
			{
				LocalResourceId = request.LocalResourceId,
				Name = request.Name,
				Type = request.Type.ToString(),
				DecimalPlaces = request.DecimalPlaces,
				MigratedFrom = request.MigratedFrom
			});
		}

		JsonElement? result;
		try
		{
			result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
					HostOperations.Variables.Bind,
					new VariablesBindArguments { Bindings = bindings },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			throw new NotSupportedException("This Macro Deck cannot record variable bindings.", exception);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.InvalidPayload)
		{
			throw new ArgumentException(exception.Message, nameof(requests), exception);
		}
		catch (HostInvocationException exception)
		{
			throw new InvalidOperationException("The variable bindings could not be recorded.", exception);
		}

		var outcomes = result?.Deserialize<VariablesBindResult>(PluginProtocolJson.Options)?.Outcomes;
		if (outcomes is null || outcomes.Count != requests.Count)
		{
			throw new InvalidOperationException("The host answered no outcome for every binding.");
		}

		return outcomes
			.Select(outcome => outcome == VariableBindingOutcomes.AlreadyBound
				? VariableBindingOutcome.AlreadyBound
				: VariableBindingOutcome.Bound)
			.ToList();
	}

	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		catalog?.CatalogChanged(CapabilityKinds.Variables, reason: "declarations-changed");
		return Task.CompletedTask;
	}

	public async Task<IVariableHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

		JsonElement? result;
		try
		{
			result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
					HostOperations.Variables.HistoryOpen,
					new VariablesHistoryOpenArguments { Name = name, Capacity = capacity },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.Unsupported,
				"This Macro Deck keeps no variable history.",
				exception);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.InvalidPayload)
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.NotOwned,
				$"'{name}' is not one of this plugin's own variables.",
				exception);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnavailable)
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.LimitExceeded,
				"This plugin holds as many variable history windows as Macro Deck allows.",
				exception);
		}
		catch (HostInvocationException exception)
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.Failed,
				"The variable history window could not be opened.",
				exception);
		}

		var opened = result?.Deserialize<VariablesHistoryOpenResult>(PluginProtocolJson.Options);
		if (opened is not { WindowId.Length: > 0 })
		{
			throw new VariableHistoryException(VariableHistoryErrorCode.Failed,
				"The host answered no variable history window.");
		}

		return new RemoteVariableHistoryWindow(invoker, opened.WindowId, time ?? TimeProvider.System);
	}

	private Task EnqueueRefresh(HashSet<string>? definitionIds, CancellationToken cancellationToken)
	{
		Task pending;
		lock (_refreshGate)
		{
			if (_refreshQueued is not null)
			{
				_queuedRefresh!.Merge(definitionIds);
				pending = _refreshQueued;
			}
			else if (_refreshInFlight is not null)
			{
				// A request already on its way may be read before the change that prompted this one, so a
				// single follow-up is queued behind it and every later caller shares that follow-up.
				_queuedRefresh = new PendingRefresh(definitionIds);
				pending = _refreshQueued = SendRefreshAfterAsync(_refreshInFlight);
			}
			else
			{
				pending = _refreshInFlight = SendRefreshAsync(new PendingRefresh(definitionIds));
			}
		}

		return pending.WaitAsync(cancellationToken);
	}

	private async Task SendRefreshAfterAsync(Task inFlight)
	{
		await Task.Yield();
		await inFlight.ConfigureAwait(false);
		PendingRefresh request;
		lock (_refreshGate)
		{
			_refreshInFlight = _refreshQueued;
			_refreshQueued = null;
			request = _queuedRefresh!;
			_queuedRefresh = null;
		}

		await InvokeRefreshAsync(request).ConfigureAwait(false);
		ReleaseInFlight();
	}

	private async Task SendRefreshAsync(PendingRefresh request)
	{
		await Task.Yield();
		await InvokeRefreshAsync(request).ConfigureAwait(false);
		ReleaseInFlight();
	}

	private void ReleaseInFlight()
	{
		lock (_refreshGate)
		{
			if (_refreshQueued is null)
			{
				_refreshInFlight = null;
			}
		}
	}

	private async Task InvokeRefreshAsync(PendingRefresh request)
	{
		foreach (var arguments in request.ToArguments())
		{
			try
			{
				await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
						HostOperations.Variables.Refresh,
						arguments,
						CancellationToken.None)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				// Best effort by contract: a host that predates the operation answers CAPABILITY_UNSUPPORTED,
				// and a refused or undelivered request still leaves the variables read at their interval.
			}
		}
	}

	private sealed class PendingRefresh
	{
		private readonly HashSet<string> _definitionIds = new(StringComparer.Ordinal);
		private bool _everyDeclared;

		public PendingRefresh(HashSet<string>? definitionIds)
		{
			Merge(definitionIds);
		}

		public void Merge(HashSet<string>? definitionIds)
		{
			if (definitionIds is null)
			{
				_everyDeclared = true;
			}
			else
			{
				_definitionIds.UnionWith(definitionIds);
			}
		}

		// A request for every declared variable does not reach bound catalog resources on the host, so named
		// ids are still sent next to it, split into lists the protocol accepts.
		public IEnumerable<VariablesRefreshArguments?> ToArguments()
		{
			if (_everyDeclared)
			{
				yield return null;
			}

			foreach (var ids in _definitionIds
				.Order(StringComparer.Ordinal)
				.Chunk(ProtocolLimits.MaxVariableRefreshDefinitionIds))
			{
				yield return new VariablesRefreshArguments { DefinitionIds = [.. ids] };
			}
		}
	}
}
