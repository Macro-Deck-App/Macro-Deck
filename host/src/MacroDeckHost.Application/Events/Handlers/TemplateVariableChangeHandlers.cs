using MacroDeckHost.Application.Variables.Templates;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class TemplateVariableCreatedHandler : INotificationHandler<VariableCreatedNotification>
{
	private readonly TemplateVariableSynchronizer _templates;

	public TemplateVariableCreatedHandler(TemplateVariableSynchronizer templates) => _templates = templates;

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
	{
		_templates.NoteChanged(notification.Variable);
		return ValueTask.CompletedTask;
	}
}

public sealed class TemplateVariableUpdatedHandler : INotificationHandler<VariableUpdatedNotification>
{
	private readonly TemplateVariableSynchronizer _templates;

	public TemplateVariableUpdatedHandler(TemplateVariableSynchronizer templates) => _templates = templates;

	public ValueTask Handle(VariableUpdatedNotification notification, CancellationToken cancellationToken)
	{
		_templates.NoteChanged(notification.Variable);
		return ValueTask.CompletedTask;
	}
}

public sealed class TemplateVariableDeletedHandler : INotificationHandler<VariableDeletedNotification>
{
	private readonly TemplateVariableSynchronizer _templates;

	public TemplateVariableDeletedHandler(TemplateVariableSynchronizer templates) => _templates = templates;

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		_templates.NoteDeleted(notification.Variable);
		return ValueTask.CompletedTask;
	}
}
