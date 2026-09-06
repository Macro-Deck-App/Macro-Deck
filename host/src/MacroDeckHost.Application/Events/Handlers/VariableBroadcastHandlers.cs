using MacroDeckHost.Application.Variables;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableUpdatedBroadcastHandler : INotificationHandler<VariableUpdatedNotification>
{
	private readonly VariableBroadcastChannel _channel;
	private readonly IVariableChangeNotifier _notifier;

	public VariableUpdatedBroadcastHandler(VariableBroadcastChannel channel, IVariableChangeNotifier notifier)
	{
		_channel = channel;
		_notifier = notifier;
	}

	public ValueTask Handle(VariableUpdatedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);

		_channel.Enqueue(notification.Variable.Id);
		_notifier.Publish(notification.Variable.Name);

		return ValueTask.CompletedTask;
	}
}

public sealed class VariableCreatedBroadcastHandler : INotificationHandler<VariableCreatedNotification>
{
	private readonly VariableBroadcastChannel _channel;
	private readonly IVariableChangeNotifier _notifier;

	public VariableCreatedBroadcastHandler(VariableBroadcastChannel channel, IVariableChangeNotifier notifier)
	{
		_channel = channel;
		_notifier = notifier;
	}

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);

		_channel.Enqueue(notification.Variable.Id);
		_notifier.Publish(notification.Variable.Name);

		return ValueTask.CompletedTask;
	}
}

public sealed class VariableDeletedBroadcastHandler : INotificationHandler<VariableDeletedNotification>
{
	private readonly VariableBroadcastChannel _channel;
	private readonly IVariableChangeNotifier _notifier;

	public VariableDeletedBroadcastHandler(VariableBroadcastChannel channel, IVariableChangeNotifier notifier)
	{
		_channel = channel;
		_notifier = notifier;
	}

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);

		_channel.Enqueue(notification.Variable.Id);
		_notifier.Publish(notification.Variable.Name);

		return ValueTask.CompletedTask;
	}
}
