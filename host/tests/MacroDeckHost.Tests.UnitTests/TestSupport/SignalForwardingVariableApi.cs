using MacroDeckHost.Application.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class SignalForwardingVariableApi(
	IVariableApi inner,
	string integrationId,
	IVariableRefreshSignal? refresh = null,
	IVariablePollingInvalidationSignal? declarations = null) : IVariableApi
{
	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		return inner.GetAllAsync();
	}

	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		return inner.GetByNameAsync(name);
	}

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		return inner.CreateAsync(name, type, initialValue, decimalPlaces, definitionId);
	}

	public Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null)
	{
		return inner.CreateAsync(declaration, initialValue);
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		return inner.SetValueAsync(variableId, value);
	}

	public Task DeleteAsync(Guid variableId)
	{
		return inner.DeleteAsync(variableId);
	}

	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		refresh?.RequestEagerRefresh(integrationId);
		return Task.CompletedTask;
	}

	public Task RequestDefinitionRefreshAsync(
		IReadOnlyCollection<string> definitionIds,
		CancellationToken cancellationToken = default)
	{
		foreach (var definitionId in definitionIds)
		{
			refresh?.RequestDefinitionRefresh(integrationId, definitionId);
		}

		return Task.CompletedTask;
	}

	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		declarations?.MarkStale(integrationId);
		return Task.CompletedTask;
	}
}
