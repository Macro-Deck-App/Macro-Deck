using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class SharedVariableDeletedHandler : INotificationHandler<VariableDeletedNotification>
{
	private readonly SharedVariables _sharedVariables;

	public SharedVariableDeletedHandler(SharedVariables sharedVariables) => _sharedVariables = sharedVariables;

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);

		if (notification.Variable.Classification == VariableClassification.User)
		{
			_sharedVariables.Forget(notification.Variable);
		}

		return ValueTask.CompletedTask;
	}
}
