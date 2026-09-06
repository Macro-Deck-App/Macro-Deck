using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class RunSequenceActionDefinitionTests
{
	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	[Test]
	public async Task Unparseable_sequence_json_fails_with_invalid_parameter()
	{
		var executor = new RecordingSequenceExecutor();
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = "not json"
		}));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
		Assert.That(executor.CallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Blank_sequence_succeeds_without_invoking_the_executor()
	{
		var executor = new RecordingSequenceExecutor();
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = "   "
		}));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(executor.CallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task A_sequence_with_zero_steps_succeeds_without_invoking_the_executor()
	{
		var executor = new RecordingSequenceExecutor();
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = """{"steps":[]}"""
		}));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		Assert.That(executor.CallCount, Is.EqualTo(0));
	}

	[Test]
	public async Task An_unresolvable_target_is_reported_rather_than_counted_as_a_keystroke()
	{
		var executor = new RecordingSequenceExecutor
			{ Unavailable = KeyboardSessionUnavailableReason.TargetNotFound };
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = """{"steps":[{"type":"keyCombo","modifiers":["Ctrl"],"key":"C"}]}"""
		}));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task A_target_that_cannot_be_focused_is_reported_as_permission_denied()
	{
		var executor = new RecordingSequenceExecutor
			{ Unavailable = KeyboardSessionUnavailableReason.FocusFailed };
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = """{"steps":[{"type":"keyCombo","modifiers":["Ctrl"],"key":"C"}]}"""
		}));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task An_unsupported_targeting_mode_is_reported_as_unavailable()
	{
		var executor = new RecordingSequenceExecutor
			{ Unavailable = KeyboardSessionUnavailableReason.Unsupported };
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = """{"steps":[{"type":"keyCombo","modifiers":["Ctrl"],"key":"C"}]}"""
		}));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	[Test]
	public async Task An_unfocused_only_when_focused_target_still_succeeds()
	{
		var executor = new RecordingSequenceExecutor
			{ Unavailable = KeyboardSessionUnavailableReason.NotFocused };
		var action = new RunSequenceActionDefinition(executor);

		var result = await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["sequence"] = """{"steps":[{"type":"keyCombo","modifiers":["Ctrl"],"key":"C"}]}"""
		}));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	private sealed class RecordingSequenceExecutor : IKeyboardSequenceExecutor
	{
		public int CallCount { get; private set; }

		public KeyboardSessionUnavailableReason? Unavailable { get; set; }

		public Task<KeyboardSessionUnavailableReason?> ExecuteAsync(
			KeyboardSequence sequence,
			KeyboardTarget target = default,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			return Task.FromResult(Unavailable);
		}
	}
}
