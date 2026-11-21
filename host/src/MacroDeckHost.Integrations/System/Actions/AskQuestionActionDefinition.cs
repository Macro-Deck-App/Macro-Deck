using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.System.Actions;

internal static class AskQuestionKinds
{
	public const string Confirm = "confirm";
	public const string Choose = "choose";
	public const string Text = "text";
}

internal static class AskQuestionOutcomes
{
	public const string Answered = "answered";
	public const string Cancelled = "cancelled";
	public const string TimedOut = "timedOut";
	public const string NoClient = "noClient";
}

internal sealed class AskQuestionActionDefinition : IActionDefinition
{
	internal const string KindParameter = "kind";
	internal const string TitleParameter = "title";
	internal const string MessageParameter = "message";
	internal const string ChoicesParameter = "choices";
	internal const string PlaceholderParameter = "placeholder";
	internal const string DefaultTextParameter = "defaultText";
	internal const string TargetParameter = "target";
	internal const string DeviceParameter = "deviceId";
	internal const string TimeoutParameter = "timeout";
	internal const string AnswerVariableParameter = "answerVariable";
	internal const string IndexVariableParameter = "indexVariable";
	internal const string OutcomeVariableParameter = "outcomeVariable";

	internal const double MinTimeoutMs = 1000;
	internal const double MaxTimeoutMs = 540_000;
	internal const double DefaultTimeoutMs = 60_000;

	private readonly NotificationServiceAccessor _notifications;
	private readonly DeckNavigatorAccessor _deck;
	private readonly VariableApiAccessor _variables;

	public AskQuestionActionDefinition(
		NotificationServiceAccessor notifications,
		DeckNavigatorAccessor deck,
		VariableApiAccessor variables)
	{
		_notifications = notifications;
		_deck = deck;
		_variables = variables;
	}

	public string Id => "ask-question";

	public LocalizedText Name => AppStrings.Integrations.System.Actions.AskQuestion.Name();

	public LocalizedText Description => AppStrings.Integrations.System.Actions.AskQuestion.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(KindParameter,
			[
				new ActionParameterOption
				{
					Value = AskQuestionKinds.Confirm,
					Label = AppStrings.Integrations.System.Actions.AskQuestion.KindConfirm()
				},
				new ActionParameterOption
				{
					Value = AskQuestionKinds.Choose,
					Label = AppStrings.Integrations.System.Actions.AskQuestion.KindChoose()
				},
				new ActionParameterOption
				{
					Value = AskQuestionKinds.Text, Label = AppStrings.Integrations.System.Actions.AskQuestion.KindText()
				}
			],
			AppStrings.Integrations.System.Actions.AskQuestion.KindLabel(),
			defaultValue: AskQuestionKinds.Confirm),
		ActionParameter.Text(TitleParameter,
			AppStrings.Integrations.System.Actions.AskQuestion.TitleLabel(),
			required: true,
			maxLength: 200),
		ActionParameter.MultilineText(MessageParameter,
			AppStrings.Integrations.System.Actions.AskQuestion.MessageLabel(),
			maxLength: 1000),
		ActionParameter.MultilineText(ChoicesParameter,
				AppStrings.Integrations.System.Actions.AskQuestion.ChoicesLabel(),
				AppStrings.Integrations.System.Actions.AskQuestion.ChoicesDescription())
			.OnlyWhen(KindParameter, AskQuestionKinds.Choose),
		ActionParameter.Text(PlaceholderParameter,
				AppStrings.Integrations.System.Actions.AskQuestion.PlaceholderLabel(),
				maxLength: 200)
			.OnlyWhen(KindParameter, AskQuestionKinds.Text),
		ActionParameter.Text(DefaultTextParameter,
				AppStrings.Integrations.System.Actions.AskQuestion.DefaultTextLabel(),
				maxLength: AskQuestionDialog.MaxTextLength)
			.OnlyWhen(KindParameter, AskQuestionKinds.Text),
		ActionParameter.Choice(TargetParameter,
			[
				new ActionParameterOption
				{
					Value = AskQuestionTarget.Origin,
					Label = AppStrings.Integrations.System.Actions.AskQuestion.TargetOrigin()
				},
				new ActionParameterOption
				{
					Value = AskQuestionTarget.Device,
					Label = AppStrings.Integrations.System.Actions.AskQuestion.TargetDevice()
				},
				new ActionParameterOption
				{
					Value = AskQuestionTarget.All,
					Label = AppStrings.Integrations.System.Actions.AskQuestion.TargetAll()
				}
			],
			AppStrings.Integrations.System.Actions.AskQuestion.TargetLabel(),
			defaultValue: AskQuestionTarget.Origin),
		ActionParameter.DynamicChoice(DeviceParameter,
				AppStrings.Integrations.System.Actions.AskQuestion.DeviceLabel(),
				optionsSourceId: HostOptionsSources.Devices)
			.OnlyWhen(TargetParameter, AskQuestionTarget.Device),
		ActionParameter.Duration(TimeoutParameter,
			AppStrings.Integrations.System.Actions.AskQuestion.TimeoutLabel(),
			AppStrings.Integrations.System.Actions.AskQuestion.TimeoutDescription(),
			MinTimeoutMs,
			MaxTimeoutMs,
			DefaultTimeoutMs),
		ActionParameter.Autocomplete(AnswerVariableParameter,
			AppStrings.Integrations.System.Actions.AskQuestion.AnswerVariableLabel(),
			AppStrings.Integrations.System.Actions.AskQuestion.AnswerVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables,
			required: true),
		ActionParameter.Autocomplete(IndexVariableParameter,
				AppStrings.Integrations.System.Actions.AskQuestion.IndexVariableLabel(),
				AppStrings.Integrations.System.Actions.AskQuestion.IndexVariableDescription(),
				optionsSourceId: HostOptionsSources.UserVariables)
			.OnlyWhen(KindParameter, AskQuestionKinds.Choose),
		ActionParameter.Autocomplete(OutcomeVariableParameter,
			AppStrings.Integrations.System.Actions.AskQuestion.OutcomeVariableLabel(),
			AppStrings.Integrations.System.Actions.AskQuestion.OutcomeVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_notifications, _deck, _variables);
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<AskQuestionActionDefinition>(SystemIntegration.IntegrationId);

		private readonly NotificationServiceAccessor _notifications;
		private readonly DeckNavigatorAccessor _deck;
		private readonly VariableApiAccessor _variables;

		public Executor(
			NotificationServiceAccessor notifications,
			DeckNavigatorAccessor deck,
			VariableApiAccessor variables)
		{
			_notifications = notifications;
			_deck = deck;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_notifications.Current is not { } service || _deck.Current is not { } deck)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Actions.AskQuestion.NotInitialized());
			}

			var title = SystemActionValues.ReadString(context.Parameters, TitleParameter);
			var answerVariable = SystemActionValues.ReadString(context.Parameters, AnswerVariableParameter).Trim();
			if (string.IsNullOrWhiteSpace(title) || answerVariable.Length == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.Failed());
			}

			var kind = SystemActionValues.ReadString(context.Parameters, KindParameter) switch
			{
				AskQuestionKinds.Choose => AskQuestionKinds.Choose,
				AskQuestionKinds.Text => AskQuestionKinds.Text,
				_ => AskQuestionKinds.Confirm
			};

			IReadOnlyList<string> choices = [];
			if (kind == AskQuestionKinds.Choose)
			{
				choices = AskQuestionDialog.ParseChoices(SystemActionValues.ReadString(context.Parameters,
					ChoicesParameter));
				if (ValidateChoices(choices) is { } invalid)
				{
					return invalid;
				}
			}

			var writer = new AnswerWriter(this, context, kind, answerVariable);
			var target = AskQuestionTarget.Resolve(deck,
				SystemActionValues.ReadString(context.Parameters, TargetParameter),
				context.OriginClientId,
				SystemActionValues.ReadString(context.Parameters, DeviceParameter));
			if (target is null)
			{
				return Stored(await writer.NoAnswerAsync(AskQuestionOutcomes.NoClient));
			}

			var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
				SystemActionValues.ReadDouble(context.Parameters, TimeoutParameter, DefaultTimeoutMs),
				MinTimeoutMs,
				MaxTimeoutMs));
			var message = SystemActionValues.ReadString(context.Parameters, MessageParameter);
			var content = AskQuestionDialog.Build(kind,
				choices,
				SystemActionValues.ReadString(context.Parameters, DefaultTextParameter),
				SystemActionValues.ReadString(context.Parameters, PlaceholderParameter),
				timeout);
			if (content.SizeBytes > AskQuestionDialog.MaxContentBytes)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.ChoicesTooLarge());
			}

			NotificationResult result;
			try
			{
				result = await service.ShowForResultAsync(new NotificationOptions
					{
						Title = title,
						Message = string.IsNullOrWhiteSpace(message) ? null : message,
						Popup = NotificationPopup.Dialog,
						Duration = timeout,
						Target = target,
						Actions = AskQuestionDialog.Actions(kind),
						Content = content.Content
					},
					context.CancellationToken);
			}
			catch (NotificationException exception)
			{
				_logger.Warning(exception, "Could not show the question");
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Actions.AskQuestion.Failed());
			}

			return Stored(await WriteResultAsync(writer, kind, result, content, choices));
		}

		private static ActionResult Stored(string? failedVariable)
		{
			return failedVariable is null
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.AnswerNotStored(failedVariable));
		}

		private static ActionResult? ValidateChoices(IReadOnlyList<string> choices)
		{
			if (choices.Count == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.NoChoices());
			}

			if (choices.Count > AskQuestionDialog.MaxChoices)
			{
				var maxChoices = AskQuestionDialog.MaxChoices.ToString(CultureInfo.InvariantCulture);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.TooManyChoices(maxChoices));
			}

			var maxLength = AskQuestionDialog.MaxChoiceLength.ToString(CultureInfo.InvariantCulture);
			return choices.Any(choice => choice.Length > AskQuestionDialog.MaxChoiceLength)
				? ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.System.Actions.AskQuestion.ChoiceTooLong(maxLength))
				: null;
		}

		private static async Task<string?> WriteResultAsync(
			AnswerWriter writer,
			string kind,
			NotificationResult result,
			AskQuestionContent content,
			IReadOnlyList<string> choices)
		{
			switch (result.CloseReason)
			{
				case NotificationCloseReason.Timeout:
					return await writer.NoAnswerAsync(AskQuestionOutcomes.TimedOut);
				case not NotificationCloseReason.Action:
					return await writer.NoAnswerAsync(AskQuestionOutcomes.Cancelled);
			}

			switch (kind, result.ActionId)
			{
				case (AskQuestionKinds.Confirm, AskQuestionDialog.YesActionId or AskQuestionDialog.NoActionId):
					return await writer.AnswerAsync(VariableType.Boolean,
						result.ActionId == AskQuestionDialog.YesActionId);
				case (AskQuestionKinds.Choose, AskQuestionDialog.OkActionId):
					var index = Math.Max(Index(result, content.ChoiceId), Index(result, content.ChoiceRowId));
					return await writer.AnswerAsync(VariableType.Text, choices[index], index);
				case (AskQuestionKinds.Text, AskQuestionDialog.OkActionId):
					return await writer.AnswerAsync(VariableType.Text, Text(result, content.TextId));
				default:
					return await writer.NoAnswerAsync(AskQuestionOutcomes.Cancelled);
			}
		}

		private static int Index(NotificationResult result, string? fieldId)
		{
			return fieldId is not null
				&& result.Values.TryGetValue(fieldId, out var value)
				&& value.ValueKind == JsonValueKind.Number
				&& value.TryGetDouble(out var index)
					? (int)index
					: 0;
		}

		private static string Text(NotificationResult result, string? fieldId)
		{
			return fieldId is not null
				&& result.Values.TryGetValue(fieldId, out var value)
				&& value.ValueKind == JsonValueKind.String
					? value.GetString()!
					: string.Empty;
		}

		private sealed class AnswerWriter
		{
			private readonly Executor _owner;
			private readonly ActionExecutionContext _context;
			private readonly string _kind;
			private readonly string _answerVariable;

			public AnswerWriter(Executor owner, ActionExecutionContext context, string kind, string answerVariable)
			{
				_owner = owner;
				_context = context;
				_kind = kind;
				_answerVariable = answerVariable;
			}

			public async Task<string?> AnswerAsync(VariableType type, object answer, int? index = null)
			{
				return await WriteAsync(_answerVariable, type, answer)
					?? await WriteIndexAsync(index ?? -1)
					?? await WriteOutcomeAsync(AskQuestionOutcomes.Answered);
			}

			public async Task<string?> NoAnswerAsync(string outcome)
			{
				var reset = _kind == AskQuestionKinds.Confirm
					? await WriteAsync(_answerVariable, VariableType.Boolean, false)
					: await WriteAsync(_answerVariable, VariableType.Text, string.Empty);

				return reset ?? await WriteIndexAsync(-1) ?? await WriteOutcomeAsync(outcome);
			}

			private async Task<string?> WriteIndexAsync(int index)
			{
				var name = SystemActionValues.ReadString(_context.Parameters, IndexVariableParameter).Trim();
				return _kind == AskQuestionKinds.Choose && name.Length > 0
					? await WriteAsync(name, VariableType.Numeric, index)
					: null;
			}

			private async Task<string?> WriteOutcomeAsync(string outcome)
			{
				var name = SystemActionValues.ReadString(_context.Parameters, OutcomeVariableParameter).Trim();
				return name.Length > 0 ? await WriteAsync(name, VariableType.Text, outcome) : null;
			}

			private async Task<string?> WriteAsync(string name, VariableType type, object value)
			{
				var written = await ActionVariableTarget.WriteAsync(_owner._variables.UserVariables,
					_owner._variables.Current,
					name,
					UserVariableContext.From(_context),
					type,
					value,
					_logger,
					_context.CancellationToken);
				return written ? null : name;
			}
		}
	}
}
