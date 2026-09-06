using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class SendNotificationActionDefinition : IActionDefinition
{
	private readonly INotificationService _notifications;

	public SendNotificationActionDefinition(INotificationService notifications)
	{
		_notifications = notifications;
	}

	public string Id => "send-notification";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.SendNotification.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.SendNotification.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("title",
			label: AppStrings.Integrations.System.Actions.SendNotification.TitleLabel(),
			required: true),
		ActionParameter.Text("message",
			label: AppStrings.Integrations.System.Actions.SendNotification.MessageLabel(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_notifications);

	private sealed class Executor : IActionExecutor
	{
		private readonly INotificationService _notifications;

		public Executor(INotificationService notifications)
		{
			_notifications = notifications;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var title = SystemActionValues.ReadString(context.Parameters, "title");
			var message = SystemActionValues.ReadString(context.Parameters, "message");
			await _notifications.ShowAsync(title, message, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
