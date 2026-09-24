using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Variables;

public sealed record HostVariableValue(string Value, bool Available);

public static class HostVariableReader
{
	public static async Task<HostVariableValue?> ReadAsync(
		IServiceScopeFactory scopeFactory,
		string name,
		string? ownerWidgetId)
	{
		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);

		await using var scope = scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();
		var registry = scope.ServiceProvider.GetRequiredService<VariableRegistry>();

		var entity = string.IsNullOrWhiteSpace(ownerWidgetId)
			? await service.Resolve(canonical, VariableScope.Global, null)
			: await service.Resolve(canonical, VariableScope.Widget, ownerWidgetId);

		return entity is null ? null : new HostVariableValue(entity.Value, registry.IsAvailable(entity.Id));
	}
}
