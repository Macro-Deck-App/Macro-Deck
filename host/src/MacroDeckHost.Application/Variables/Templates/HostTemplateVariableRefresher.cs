using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Variables.Templates;

public enum TemplateVariableRefreshStatus
{
	Refreshed,
	NotFound,
	NotTemplate,
	Unavailable
}

public static class HostTemplateVariableRefresher
{
	public static async Task<TemplateVariableRefreshStatus> RefreshAsync(
		IServiceScopeFactory scopeFactory,
		string name,
		UserVariableContext variableContext)
	{
		await using var scope = scopeFactory.CreateAsyncScope();
		return await RefreshAsync(scope.ServiceProvider.GetRequiredService<IVariableService>(),
			scope.ServiceProvider.GetRequiredService<VariableRegistry>(),
			scope.ServiceProvider.GetRequiredService<TemplateVariableSynchronizer>(),
			name,
			variableContext);
	}

	public static async Task<TemplateVariableRefreshStatus> RefreshAsync(
		IVariableService service,
		VariableRegistry registry,
		TemplateVariableSynchronizer templates,
		string name,
		UserVariableContext variableContext)
	{
		var canonical = VariableNameSanitizer.IsValid(name) ? name : VariableNameSanitizer.Sanitize(name);
		var entity = await service.Resolve(canonical, VariableScopeChain.For(variableContext));
		if (entity is null)
		{
			return TemplateVariableRefreshStatus.NotFound;
		}

		if (entity.TemplateSource is null)
		{
			return TemplateVariableRefreshStatus.NotTemplate;
		}

		await templates.ChangeAsync(entity.Id, null);

		var refreshed = registry.GetById(entity.Id);
		if (refreshed is null)
		{
			return TemplateVariableRefreshStatus.NotFound;
		}

		return registry.IsAvailable(refreshed.Id)
			? TemplateVariableRefreshStatus.Refreshed
			: TemplateVariableRefreshStatus.Unavailable;
	}
}
