using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class AutomationCreatedNotificationHandler : INotificationHandler<AutomationCreatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public AutomationCreatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(AutomationCreatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new AutomationCreatedEvent { Automation = AutomationDtoMapper.ToDto(notification.Automation) };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}

public sealed class AutomationUpdatedNotificationHandler : INotificationHandler<AutomationUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public AutomationUpdatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(AutomationUpdatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new AutomationUpdatedEvent { Automation = AutomationDtoMapper.ToDto(notification.Automation) };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}

public sealed class AutomationDeletedNotificationHandler : INotificationHandler<AutomationDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public AutomationDeletedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(AutomationDeletedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new AutomationDeletedEvent { Id = notification.AutomationId.ToString() };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
