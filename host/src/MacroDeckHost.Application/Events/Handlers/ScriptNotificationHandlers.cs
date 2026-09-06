using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ScriptCreatedNotificationHandler : INotificationHandler<ScriptCreatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ScriptCreatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(ScriptCreatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new ScriptCreatedEvent { Script = ScriptDtoMapper.ToDto(notification.Script) };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}

public sealed class ScriptUpdatedNotificationHandler : INotificationHandler<ScriptUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ScriptUpdatedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(ScriptUpdatedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new ScriptUpdatedEvent { Script = ScriptDtoMapper.ToDto(notification.Script) };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}

public sealed class ScriptDeletedNotificationHandler : INotificationHandler<ScriptDeletedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ScriptDeletedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(ScriptDeletedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new ScriptDeletedEvent { Id = notification.ScriptId.ToString() };
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
