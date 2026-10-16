using MacroDeckHost.Application.Services;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Variables;

public sealed record HostVariableValue(string Value, bool Available);

public static class HostVariableReader
{
	public static async Task<HostVariableValue?> ReadAsync(
		IServiceScopeFactory scopeFactory,
		string name,
		UserVariableContext variableContext)
	{
		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);

		await using var scope = scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();
		var registry = scope.ServiceProvider.GetRequiredService<VariableRegistry>();

		var entity = await service.Resolve(canonical, VariableScopeChain.For(variableContext));

		return entity is null ? null : new HostVariableValue(entity.Value, registry.IsAvailable(entity.Id));
	}
}
