using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public interface IAutomationService
{
	IReadOnlyList<AutomationEntity> GetAll();

	AutomationEntity? GetById(Guid id);

	Task<Result<AutomationEntity, AutomationError>> Create(
		string name,
		string? description,
		string? flows,
		bool enabled = true);

	Task<Result<AutomationEntity, AutomationError>> Update(
		Guid id,
		string? name,
		string? description,
		string? flows,
		bool? enabled);

	Task<Result<AutomationEntity, AutomationError>> Duplicate(Guid id);

	Task<Result<AutomationError>> Delete(Guid id);
}
