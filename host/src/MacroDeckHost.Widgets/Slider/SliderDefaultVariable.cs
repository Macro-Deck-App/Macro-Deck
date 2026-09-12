using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.Slider;

internal static class SliderDefaultVariable
{
	public const string Name = "slider_value";

	// The default name never falls through to a global of the same name.
	public static VariableEntity? Find(VariableRegistry registry, Guid? widgetId, string? pickedName)
	{
		var widgetScoped = widgetId is { } id
			? registry.FindByName(VariableScope.Widget, id.ToString(), pickedName ?? Name)
			: null;

		return widgetScoped ??
			(pickedName is null ? null : registry.FindByName(VariableScope.Global, null, pickedName));
	}

	// A user variable, not a host-managed widget one: only user variables take a drag and every Set Variable operation.
	public static async Task EnsureAsync(
		Guid widgetId,
		double initialValue,
		VariableRegistry registry,
		IServiceScopeFactory scopeFactory)
	{
		if (registry.FindByName(VariableScope.Widget, widgetId.ToString(), Name) is not null)
		{
			return;
		}

		await using var scope = scopeFactory.CreateAsyncScope();

		await scope.ServiceProvider.GetRequiredService<IVariableService>()
			.CreateUserVariable(Name,
				VariableScope.Widget,
				widgetId.ToString(),
				VariableType.Numeric,
				(decimal)initialValue,
				null)
			.ConfigureAwait(false);
	}
}
