using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ScriptVariablesScriptDeletedHandler : INotificationHandler<ScriptDeletedNotification>
{
	private readonly IServiceScopeFactory _scopeFactory;

	public ScriptVariablesScriptDeletedHandler(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public async ValueTask Handle(ScriptDeletedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<IVariableService>()
			.DeleteByScopeInstance(VariableScope.Script, notification.ScriptId.ToString());
	}
}

public sealed class AutomationVariablesAutomationDeletedHandler : INotificationHandler<AutomationDeletedNotification>
{
	private readonly IServiceScopeFactory _scopeFactory;

	public AutomationVariablesAutomationDeletedHandler(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public async ValueTask Handle(AutomationDeletedNotification notification, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<IVariableService>()
			.DeleteByScopeInstance(VariableScope.Automation, notification.AutomationId.ToString());
	}
}
