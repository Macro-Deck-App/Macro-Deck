using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Variables;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkHistoryWindow = MacroDeck.Sdk.Variables.IVariableHistoryWindow;

namespace MacroDeckHost.Infrastructure.Variables;

public sealed class IntegrationVariableApi : IVariableApi
{
	private readonly string _integrationId;
	private readonly IVariableService _service;
	private readonly IVariableRefreshSignal? _refresh;
	private readonly IVariablePollingInvalidationSignal? _declarations;
	private readonly OwnedVariableHistory? _history;
	private readonly VariableBindingAppender? _bindings;
	private readonly IMcpVariableWriteGuard? _mcpWriteGuard;

	public IntegrationVariableApi(
		string integrationId,
		IVariableService service,
		IVariableRefreshSignal? refresh = null,
		IVariablePollingInvalidationSignal? declarations = null,
		OwnedVariableHistory? history = null,
		VariableBindingAppender? bindings = null,
		IMcpVariableWriteGuard? mcpWriteGuard = null)
	{
		_mcpWriteGuard = mcpWriteGuard;
		_integrationId = integrationId;
		_service = service;
		_refresh = refresh;
		_declarations = declarations;
		_history = history;
		_bindings = bindings;
	}

	public Task<SdkHistoryWindow> OpenHistoryAsync(
		string name,
		int capacity,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return _history is null
			? Task.FromException<SdkHistoryWindow>(new VariableHistoryException(VariableHistoryErrorCode.Unsupported,
				"This context keeps no variable history."))
			: Task.FromResult(_history.Open(_integrationId, name, capacity));
	}

	public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_refresh?.RequestEagerRefresh(_integrationId);
		return Task.CompletedTask;
	}

	public Task RequestDefinitionRefreshAsync(
		IReadOnlyCollection<string> definitionIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitionIds);
		cancellationToken.ThrowIfCancellationRequested();
		foreach (var definitionId in definitionIds)
		{
			_refresh?.RequestDefinitionRefresh(_integrationId, definitionId);
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<VariableBindingOutcome>> BindAsync(
		IReadOnlyCollection<VariableBindingRequest> requests,
		CancellationToken cancellationToken = default)
	{
		return _bindings is null
			? Task.FromException<IReadOnlyList<VariableBindingOutcome>>(
				new NotSupportedException("This context cannot record variable bindings."))
			: _bindings.AppendAsync(_integrationId, requests, cancellationToken);
	}

	public Task NotifyDeclarationsChangedAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_declarations?.MarkStale(_integrationId);
		return Task.CompletedTask;
	}

	public async Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		var entities = await _service.GetByOwnerIntegration(_integrationId);
		return entities.Select(ToHandle).ToList();
	}

	public async Task<VariableHandle?> GetByNameAsync(string name)
	{
		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		var resolved = await _service.Resolve(canonical, VariableScope.Global, null);
		if (resolved is null
			|| resolved.Classification != VariableClassification.Integration
			|| resolved.OwnerIntegrationId != _integrationId)
		{
			return null;
		}

		return ToHandle(resolved);
	}

	public Task<VariableHandle> CreateAsync(
		string name,
		SdkVariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		return Create(name, type, initialValue, decimalPlaces, definitionId, null);
	}

	private async Task<VariableHandle> Create(
		string name,
		SdkVariableType type,
		object? initialValue,
		int? decimalPlaces,
		string? definitionId,
		VariableDeclaration? declaration)
	{
		var result = await _service.CreateIntegrationVariable(_integrationId,
			name,
			VariableScope.Global,
			null,
			SdkVariableTypeMapper.ToDomain(type),
			initialValue,
			decimalPlaces,
			definitionId,
			declaration);

		if (!result.Success || result.Data is null)
		{
			throw new InvalidOperationException(
				$"Failed to create variable '{name}' for integration '{_integrationId}': "
				+ $"{result.Error} ({result.ErrorMessage})");
		}

		return ToHandle(result.Data);
	}

	public Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null)
	{
		return Create(declaration.Name ?? string.Empty,
			declaration.Type,
			initialValue,
			declaration.DecimalPlaces,
			declaration.Id,
			VariableDeclarationFactory.From(declaration));
	}

	public async Task SetValueAsync(Guid variableId, object? value)
	{
		if (_mcpWriteGuard is not null
			&& await _service.GetById(variableId) is { } target
			&& _mcpWriteGuard.Refuses(_integrationId, target.Name))
		{
			throw new InvalidOperationException(McpVariableWriteGuard.RefusalMessage);
		}

		var result = await _service.ReportIntegrationVariableValue(_integrationId, variableId, value);
		if (!result.Success)
		{
			throw new InvalidOperationException(
				$"Failed to set variable '{variableId}' for integration '{_integrationId}': "
				+ $"{result.Error} ({result.ErrorMessage})");
		}
	}

	public async Task DeleteAsync(Guid variableId)
	{
		var result = await _service.DeleteIntegrationVariable(_integrationId, variableId);
		if (!result.Success
			&& result.Error != VariableError.NotFound
			&& result.Error != VariableError.NotOwnedByIntegration)
		{
			throw new InvalidOperationException(
				$"Failed to delete variable '{variableId}' for integration '{_integrationId}': "
				+ $"{result.Error} ({result.ErrorMessage})");
		}
	}

	private static SdkVariableType MapType(DomainVariableType type)
	{
		return type switch
		{
			DomainVariableType.Text => SdkVariableType.Text,
			DomainVariableType.Numeric => SdkVariableType.Numeric,
			DomainVariableType.Boolean => SdkVariableType.Boolean,
			DomainVariableType.Color => SdkVariableType.Color,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown variable type")
		};
	}

	private static VariableHandle ToHandle(VariableEntity entity)
	{
		var typed = VariableValueSerializer.Deserialize(entity.Type, entity.Value);
		return new VariableHandle(entity.Id, entity.Name, MapType(entity.Type), typed, entity.DecimalPlaces)
		{
			DefinitionId = entity.DefinitionId
		};
	}
}
