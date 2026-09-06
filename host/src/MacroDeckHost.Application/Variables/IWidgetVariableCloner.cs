using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public sealed record WidgetVariableSnapshot(string Name, VariableType Type, string Value, int? DecimalPlaces);

public interface IWidgetVariableCloner
{
	Task<IReadOnlyList<WidgetVariableSnapshot>> Snapshot(Guid widgetId);

	Task Restore(Guid widgetId, IReadOnlyList<WidgetVariableSnapshot> variables);

	Task Clone(Guid sourceWidgetId, Guid targetWidgetId);
}
