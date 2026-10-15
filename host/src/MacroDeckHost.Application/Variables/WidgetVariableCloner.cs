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

	public Task<IReadOnlyList<WidgetVariableSnapshot>> Snapshot(Guid widgetId)
	{
		return SnapshotScope(VariableScope.Widget, widgetId.ToString());
	}

	public Task Restore(Guid widgetId, IReadOnlyList<WidgetVariableSnapshot> variables)
	{
		return RestoreScope(VariableScope.Widget, widgetId.ToString(), variables);
	}

	public async Task<IReadOnlyList<WidgetVariableSnapshot>> SnapshotScope(VariableScope scope, string scopeRefId)
	{
		var variables = await _variableService.GetByScope(scope, scopeRefId);
		return variables
			.Where(variable => variable.Classification == VariableClassification.User)
			.Select(variable => new WidgetVariableSnapshot(variable.Name,
				variable.Type,
				variable.Value,
				variable.DecimalPlaces,
				variable.FileSource,
				variable.TemplateSource))
			.ToList();
	}

	public async Task RestoreScope(
		VariableScope scope,
		string scopeRefId,
		IReadOnlyList<WidgetVariableSnapshot> variables)
	{
		foreach (var variable in variables)
		{
			try
			{
				var existing = await _variableService.GetByScope(scope, scopeRefId);
				var collision = existing.FirstOrDefault(v =>
					v.Name == variable.Name && v.Classification == VariableClassification.Widget);
				if (collision is not null)
				{
					// The imported user variable deliberately wins over host-derived widget state, so the
					// imported widget ends up matching the source instead of keeping whatever the
					// newly-created widget happened to derive locally.
					await _variableService.RemoveWidgetVariable(scope, scopeRefId, variable.Name);
				}

				var result = await _variableService.CreateUserVariable(variable.Name,
					scope,
					scopeRefId,
					variable.Type,
					variable.Value,
					variable.DecimalPlaces,
					variable.FileSource,
					variable.TemplateSource);
				if (!result.Success)
				{
					_logger.Warning(
						"Skipping {Scope} variable '{VariableName}' for {ScopeRefId} during import: {Error}",
						scope,
						variable.Name,
						scopeRefId,
						result.ErrorMessage);
				}
			}
			catch (Exception ex)
			{
				_logger.Warning(ex,
					"Failed to restore {Scope} variable '{VariableName}' for {ScopeRefId}",
					scope,
					variable.Name,
					scopeRefId);
			}
		}
	}

	public async Task Clone(Guid sourceWidgetId, Guid targetWidgetId)
	{
		await Restore(targetWidgetId, await Snapshot(sourceWidgetId));
	}
}
