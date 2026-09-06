using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class AutomationDtoMapper
{
	public static Automation ToDto(AutomationEntity automation) => new()
	{
		Id = automation.Id.ToString(),
		Name = automation.Name,
		Description = automation.Description,
		Enabled = automation.Enabled,
		Flows = automation.Flows,
		CreatedAt = automation.CreatedAt,
		UpdatedAt = automation.UpdatedAt
	};

	public static TransportError ToError(AutomationError error, string? message) => new()
	{
		Code = error.ToString(),
		Message = message ?? string.Empty
	};

	public static TransportError InvalidId() => new()
	{
		Code = nameof(AutomationError.ValidationError),
		Message = "Invalid automation id"
	};
}
