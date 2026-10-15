using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Localization;
using DomainScope = MacroDeckHost.Domain.Enums.VariableScope;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateVariableRequestMessageHandler
	: IUiTransportMessageHandler<CreateVariableRequest, CreateVariableResponse>
{
	private readonly IVariableService _service;
	private readonly VariableRegistry _registry;
	private readonly SharedVariables _sharedVariables;
	private readonly IWidgetAppearanceService _widgets;
	private readonly IScriptService _scripts;
	private readonly IAutomationService _automations;

	public CreateVariableRequestMessageHandler(
		IVariableService service,
		VariableRegistry registry,
		SharedVariables sharedVariables,
		IWidgetAppearanceService widgets,
		IScriptService scripts,
		IAutomationService automations)
	{
		_service = service;
		_registry = registry;
		_sharedVariables = sharedVariables;
		_widgets = widgets;
		_scripts = scripts;
		_automations = automations;
	}

	public async ValueTask<CreateVariableResponse> Handle(
		CreateVariableRequest request,
		CancellationToken cancellationToken)
	{
		var scope = VariableDtoMapper.ScopeFromWire(request.Scope);
		var type = VariableDtoMapper.TypeFromWire(request.Type);

		if (scope is null || type is null)
		{
			return new CreateVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "ValidationError", Message = AppStrings.Errors.Variables.InvalidScopeOrType() }
			};
		}

		if (!OwnerExists(scope.Value, request.ScopeRefId))
		{
			return new CreateVariableResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "UnknownScopeOwner", Message = AppStrings.Errors.Variables.UnknownScopeOwner() }
			};
		}

		var initialValue = VariableDtoMapper.ParseInputValue(type.Value, request.InitialValue);

		var result = await _service.CreateUserVariable(request.Name,
			scope.Value,
			request.ScopeRefId,
			type.Value,
			initialValue,
			request.DecimalPlaces,
			VariableDtoMapper.FileSourceFromWire(request.FileSource),
			VariableDtoMapper.TemplateSourceFromWire(request.TemplateSource));

		if (!result.Success || result.Data is null)
		{
			return new CreateVariableResponse
			{
				Success = false,
				Error = VariableDtoMapper.ToError(result.Error!.Value, result.ErrorMessage)
			};
		}

		return new CreateVariableResponse
		{
			Success = true,
			Variable = VariableDtoMapper.ToDto(result.Data,
				_registry.IsAvailable(result.Data.Id),
				null,
				_sharedVariables.IsShared(result.Data))
		};
	}

	private bool OwnerExists(DomainScope scope, string? scopeRefId)
	{
		if (string.IsNullOrWhiteSpace(scopeRefId))
		{
			return true;
		}

		return scope switch
		{
			DomainScope.Widget => _widgets.Exists(scopeRefId),
			DomainScope.Script => Guid.TryParse(scopeRefId, out var scriptId) && _scripts.GetById(scriptId) is not null,
			DomainScope.Automation => Guid.TryParse(scopeRefId, out var automationId)
				&& _automations.GetById(automationId) is not null,
			_ => true
		};
	}
}
