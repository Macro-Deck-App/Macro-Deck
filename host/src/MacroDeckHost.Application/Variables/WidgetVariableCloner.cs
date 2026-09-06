using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Variables;

public sealed class WidgetVariableCloner : IWidgetVariableCloner
{
	private readonly IVariableService _variableService;
	private readonly ILogger _logger;

	public WidgetVariableCloner(IVariableService variableService, ILogger logger)
	{
		_variableService = variableService;
		_logger = logger;
	}

	public async Task<IReadOnlyList<WidgetVariableSnapshot>> Snapshot(Guid widgetId)
	{
		var scopeRefId = widgetId.ToString();
		var variables = await _variableService.GetByScope(VariableScope.Widget, scopeRefId);
		return variables
			.Where(variable => variable.Classification == VariableClassification.User)
			.Select(variable => new WidgetVariableSnapshot(variable.Name,
				variable.Type,
				variable.Value,
				variable.DecimalPlaces))
			.ToList();
	}

	public async Task Restore(Guid widgetId, IReadOnlyList<WidgetVariableSnapshot> variables)
	{
		var scopeRefId = widgetId.ToString();
		foreach (var variable in variables)
		{
			try
			{
				var existing = await _variableService.GetByScope(VariableScope.Widget, scopeRefId);
				var collision = existing.FirstOrDefault(v =>
					v.Name == variable.Name && v.Classification == VariableClassification.Widget);
				if (collision is not null)
				{
					// The imported user variable deliberately wins over host-derived widget state, so the
					// imported widget ends up matching the source instead of keeping whatever the
					// newly-created widget happened to derive locally.
					await _variableService.RemoveWidgetVariable(VariableScope.Widget, scopeRefId, variable.Name);
				}

				var result = await _variableService.CreateUserVariable(variable.Name,
					VariableScope.Widget,
					scopeRefId,
					variable.Type,
					variable.Value,
					variable.DecimalPlaces);
				if (!result.Success)
				{
					_logger.Warning(
						"Skipping widget variable '{VariableName}' for widget {WidgetId} during import: {Error}",
						variable.Name,
						widgetId,
						result.ErrorMessage);
				}
			}
			catch (Exception ex)
			{
				_logger.Warning(ex,
					"Failed to restore widget variable '{VariableName}' for widget {WidgetId}",
					variable.Name,
					widgetId);
			}
		}
	}

	public async Task Clone(Guid sourceWidgetId, Guid targetWidgetId)
		=> await Restore(targetWidgetId, await Snapshot(sourceWidgetId));
}
