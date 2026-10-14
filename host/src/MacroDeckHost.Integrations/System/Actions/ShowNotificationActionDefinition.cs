using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class ShowNotificationActionDefinition : IActionDefinition
{
	internal const string TitleParameter = "title";
	internal const string MessageParameter = "message";
	internal const string SeverityParameter = "severity";
	internal const string PresentationParameter = "presentation";
	internal const string TimeoutParameter = "timeout";
	internal const string OkActionId = "ok";

	private readonly NotificationServiceAccessor _notifications;

	public ShowNotificationActionDefinition(NotificationServiceAccessor notifications)
	{
		_notifications = notifications;
	}

	public string Id => "show-notification";

	public LocalizedText Name => AppStrings.Integrations.System.Actions.ShowNotification.Name();

	public LocalizedText Description => AppStrings.Integrations.System.Actions.ShowNotification.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(TitleParameter,
			AppStrings.Integrations.System.Actions.ShowNotification.TitleLabel(),
			required: true,
			maxLength: 200),
		ActionParameter.MultilineText(MessageParameter,
			AppStrings.Integrations.System.Actions.ShowNotification.MessageLabel(),
			maxLength: 1000),
		ActionParameter.Choice(SeverityParameter,
			[
				new ActionParameterOption
					{ Value = "info", Label = AppStrings.Integrations.System.Actions.ShowNotification.SeverityInfo() },
				new ActionParameterOption
				{
					Value = "success", Label = AppStrings.Integrations.System.Actions.ShowNotification.SeveritySuccess()
				},
				new ActionParameterOption
				{
					Value = "warning", Label = AppStrings.Integrations.System.Actions.ShowNotification.SeverityWarning()
				},
				new ActionParameterOption
					{ Value = "error", Label = AppStrings.Integrations.System.Actions.ShowNotification.SeverityError() }
			],
			AppStrings.Integrations.System.Actions.ShowNotification.SeverityLabel(),
			defaultValue: "info"),
		ActionParameter.Choice(PresentationParameter,
			[
				new ActionParameterOption
				{
					Value = "toast", Label = AppStrings.Integrations.System.Actions.ShowNotification.PresentationToast()
				},
				new ActionParameterOption
				{
					Value = "toast-inbox",
					Label = AppStrings.Integrations.System.Actions.ShowNotification.PresentationToastInbox()
				},
				new ActionParameterOption
				{
					Value = "dialog",
					Label = AppStrings.Integrations.System.Actions.ShowNotification.PresentationDialog()
				},
				new ActionParameterOption
				{
					Value = "dialog-inbox",
					Label = AppStrings.Integrations.System.Actions.ShowNotification.PresentationDialogInbox()
				},
				new ActionParameterOption
				{
					Value = "inbox", Label = AppStrings.Integrations.System.Actions.ShowNotification.PresentationInbox()
				}
			],
			AppStrings.Integrations.System.Actions.ShowNotification.PresentationLabel(),
			defaultValue: "toast"),
		ActionParameter.Duration(TimeoutParameter,
			AppStrings.Integrations.System.Actions.ShowNotification.TimeoutLabel(),
			AppStrings.Integrations.System.Actions.ShowNotification.TimeoutDescription(),
			1000,
			3_600_000)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_notifications);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly NotificationServiceAccessor _notifications;

		public Executor(NotificationServiceAccessor notifications)
		{
			_notifications = notifications;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_notifications.Current is not { } service)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Actions.ShowNotification.NotInitialized());
			}

			var title = SystemActionValues.ReadString(context.Parameters, TitleParameter);
			if (string.IsNullOrWhiteSpace(title))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.ShowNotification.Failed());
			}

			var message = SystemActionValues.ReadString(context.Parameters, MessageParameter);
			var presentation = SystemActionValues.ReadString(context.Parameters, PresentationParameter);
			var timeout = SystemActionValues.ReadDouble(context.Parameters, TimeoutParameter, 0);
			var popup = presentation switch
			{
				"dialog" or "dialog-inbox" => NotificationPopup.Dialog,
				"inbox" => NotificationPopup.None,
				_ => NotificationPopup.Toast
			};
			var duration = timeout > 0 ? TimeSpan.FromMilliseconds(timeout) : (TimeSpan?)null;

			try
			{
				await service.ShowAsync(new NotificationOptions
					{
						Title = title,
						Message = string.IsNullOrWhiteSpace(message) ? null : message,
						Severity = SystemActionValues.ReadString(context.Parameters, SeverityParameter) switch
						{
							"success" => NotificationSeverity.Success,
							"warning" => NotificationSeverity.Warning,
							"error" => NotificationSeverity.Error,
							_ => NotificationSeverity.Info
						},
						Popup = popup,
						Inbox = presentation is "toast-inbox" or "dialog-inbox" or "inbox",
						Duration = duration,
						Actions = popup == NotificationPopup.Dialog && duration is null
							?
							[
								new NotificationAction
								{
									Id = OkActionId,
									Label = AppStrings.Feedback.Notification.Ok(),
									Style = NotificationActionStyle.Primary
								}
							]
							: []
					},
					context.CancellationToken);
			}
			catch (NotificationException)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Actions.ShowNotification.Failed());
			}

			return ActionResult.Success();
		}
	}
}
