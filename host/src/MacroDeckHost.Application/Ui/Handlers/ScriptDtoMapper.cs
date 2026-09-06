using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class ScriptDtoMapper
{
	public static Script ToDto(ScriptEntity script) => new()
	{
		Id = script.Id.ToString(),
		Name = script.Name,
		Description = script.Description,
		Flows = script.Flows,
		Inputs = [.. script.Inputs],
		RunsOnWidget = script.RunsOnWidget,
		CreatedAt = script.CreatedAt,
		UpdatedAt = script.UpdatedAt
	};

	public static TransportError ToError(ScriptError error, LocalizedText message) => new()
	{
		Code = error.ToString(),
		Message = message
	};

	public static TransportError InvalidId() => new()
	{
		Code = nameof(ScriptError.ValidationError),
		Message = "Invalid script id"
	};
}
