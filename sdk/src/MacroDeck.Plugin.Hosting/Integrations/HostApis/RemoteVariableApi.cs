using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
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

		return result?.Deserialize<VariableHandle>(PluginProtocolJson.Options) ??
			throw new InvalidOperationException($"The host returned no variable for '{name}'.");
	}

	public Task SetValueAsync(Guid variableId, object? value)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Set,
			new VariablesSetArguments
			{
				VariableId = variableId,
				Value = value is null ? null : JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options)
			},
			CancellationToken.None);

	public Task DeleteAsync(Guid variableId)
		=> invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
			HostOperations.Variables.Delete,
			new VariablesDeleteArguments { VariableId = variableId },
			CancellationToken.None);

	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		Task pending;
		lock (_refreshGate)
		{
			if (_refreshQueued is not null)
			{
				pending = _refreshQueued;
			}
			else if (_refreshInFlight is not null)
			{
				// A request already on its way may be read before the change that prompted this one, so a
				// single follow-up is queued behind it and every later caller shares that follow-up.
				pending = _refreshQueued = SendRefreshAfterAsync(_refreshInFlight);
			}
			else
			{
				pending = _refreshInFlight = SendRefreshAsync();
			}
		}

		return pending.WaitAsync(cancellationToken);
	}

	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		catalog?.CatalogChanged(CapabilityKinds.Variables, reason: "declarations-changed");
		return Task.CompletedTask;
	}

	public async Task<IVariableHistoryWindow> OpenHistoryAsync(string name,
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

	private async Task SendRefreshAfterAsync(Task inFlight)
	{
		await Task.Yield();
		await inFlight.ConfigureAwait(false);
		lock (_refreshGate)
		{
			_refreshInFlight = _refreshQueued;
			_refreshQueued = null;
		}

		await InvokeRefreshAsync().ConfigureAwait(false);
		lock (_refreshGate)
		{
			_refreshInFlight = null;
		}
	}

	private async Task SendRefreshAsync()
	{
		await Task.Yield();
		await InvokeRefreshAsync().ConfigureAwait(false);
		lock (_refreshGate)
		{
			if (_refreshQueued is null)
			{
				_refreshInFlight = null;
			}
		}
	}

	private async Task InvokeRefreshAsync()
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Variables,
					HostOperations.Variables.Refresh,
					null,
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
