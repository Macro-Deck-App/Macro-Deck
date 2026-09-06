using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Variables;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Infrastructure.Variables;

public sealed class IntegrationVariableApi : IVariableApi
{
	private readonly string _integrationId;
	private readonly IVariableService _service;

	public IntegrationVariableApi(string integrationId, IVariableService service)
	{
		_integrationId = integrationId;
		_service = service;
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
		if (resolved is null ||
			resolved.Classification != VariableClassification.Integration ||
			resolved.OwnerIntegrationId != _integrationId)
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
		=> Create(name, type, initialValue, decimalPlaces, definitionId, null);

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
				$"Failed to create variable '{name}' for integration '{_integrationId}': " +
				$"{result.Error} ({result.ErrorMessage})");
		}

		return ToHandle(result.Data);
	}

	public Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null)
		=> Create(declaration.Name ?? string.Empty,
			declaration.Type,
			initialValue,
			declaration.DecimalPlaces,
			declaration.Id,
			VariableDeclarationFactory.From(declaration));

	public async Task SetValueAsync(Guid variableId, object? value)
	{
		var result = await _service.ReportIntegrationVariableValue(_integrationId, variableId, value);
		if (!result.Success)
		{
			throw new InvalidOperationException(
				$"Failed to set variable '{variableId}' for integration '{_integrationId}': " +
				$"{result.Error} ({result.ErrorMessage})");
		}
	}

	public async Task DeleteAsync(Guid variableId)
	{
		var result = await _service.DeleteIntegrationVariable(_integrationId, variableId);
		if (!result.Success &&
			result.Error != VariableError.NotFound &&
			result.Error != VariableError.NotOwnedByIntegration)
		{
			throw new InvalidOperationException(
				$"Failed to delete variable '{variableId}' for integration '{_integrationId}': " +
				$"{result.Error} ({result.ErrorMessage})");
		}
	}

	private static SdkVariableType MapType(DomainVariableType type) => type switch
	{
		DomainVariableType.Text => SdkVariableType.Text,
		DomainVariableType.Numeric => SdkVariableType.Numeric,
		DomainVariableType.Boolean => SdkVariableType.Boolean,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown variable type")
	};

	private static VariableHandle ToHandle(VariableEntity entity)
	{
		var typed = VariableValueSerializer.Deserialize(entity.Type, entity.Value);
		return new VariableHandle(entity.Id, entity.Name, MapType(entity.Type), typed, entity.DecimalPlaces)
		{
			DefinitionId = entity.DefinitionId
		};
	}
}
