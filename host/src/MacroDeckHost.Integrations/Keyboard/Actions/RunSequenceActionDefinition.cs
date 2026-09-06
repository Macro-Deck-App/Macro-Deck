using System.Text.Json;
using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Keyboard.Actions;

internal sealed class RunSequenceActionDefinition : IActionDefinition
{
	internal static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly IKeyboardSequenceExecutor _executor;

	public RunSequenceActionDefinition(IKeyboardSequenceExecutor executor)
	{
		_executor = executor;
	}

	public string Id => "run-sequence";
	public LocalizedText Name => AppStrings.Integrations.Keyboard.Actions.RunSequenceName();
	public LocalizedText Description => AppStrings.Integrations.Keyboard.Actions.RunSequenceDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.KeyboardSequence("sequence",
			label: AppStrings.Integrations.Keyboard.Actions.SequenceLabel(),
			description: AppStrings.Integrations.Keyboard.Actions.SequenceDescription(),
			required: true),
		.. KeyboardTargetParameters.All
	];

	public IActionExecutor CreateExecutor() => new Executor(_executor);

	private sealed class Executor : IActionExecutor
	{
		private readonly IKeyboardSequenceExecutor _executor;

		public Executor(IKeyboardSequenceExecutor executor)
		{
			_executor = executor;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var json = KeyboardActionValues.ReadString(context.Parameters, "sequence");
			if (string.IsNullOrWhiteSpace(json))
			{
				return ActionResult.Success();
			}

			KeyboardSequence sequence;
			try
			{
				sequence = JsonSerializer.Deserialize<KeyboardSequence>(json, SerializerOptions) ??
					new KeyboardSequence();
			}
			catch (JsonException)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Keyboard.Errors.SequenceCouldNotBeRead());
			}

			if (sequence.Steps.Count == 0)
			{
				return ActionResult.Success();
			}

			var target = KeyboardActionValues.ReadTarget(context.Parameters);
			var unavailable = await _executor.ExecuteAsync(sequence, target, context.CancellationToken);
			return unavailable is null
				? ActionResult.Success()
				: KeyboardActionValues.SessionUnavailableResult(unavailable);
		}
	}
}
