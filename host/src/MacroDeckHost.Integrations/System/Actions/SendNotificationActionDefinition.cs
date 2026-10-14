using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class SendNotificationActionDefinition : IActionDefinition
{
	private readonly DesktopNotifierAccessor _notifications;

	public SendNotificationActionDefinition(DesktopNotifierAccessor notifications)
	{
		_notifications = notifications;
	}

	public string Id => "send-notification";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.SendNotification.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.SendNotification.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text("title",
			AppStrings.Integrations.System.Actions.SendNotification.TitleLabel(),
			required: true),
		ActionParameter.Text("message",
			AppStrings.Integrations.System.Actions.SendNotification.MessageLabel(),
			required: true)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_notifications);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly DesktopNotifierAccessor _notifications;

		public Executor(DesktopNotifierAccessor notifications)
		{
			_notifications = notifications;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_notifications.Current is not { } notifier)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Actions.ShowNotification.NotInitialized());
			}

			var title = SystemActionValues.ReadString(context.Parameters, "title");
			var message = SystemActionValues.ReadString(context.Parameters, "message");
			await notifier.ShowAsync(title, message, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
