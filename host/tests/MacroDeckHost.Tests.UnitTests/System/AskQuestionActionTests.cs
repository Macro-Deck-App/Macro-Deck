using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.System;

public class AskQuestionActionTests
{
	private static readonly NotificationOwner _system =
		new(NotificationSourceCategory.BuiltInIntegration, "app.macro-deck.system", "System");

	private static readonly Guid _phone = Guid.NewGuid();
	private static readonly Guid _tablet = Guid.NewGuid();

	private FakeTimeProvider _time = null!;
	private NotificationManager _manager = null!;
	private RecordingDeckNavigator _deck = null!;
	private StoringUserVariableApi _variables = null!;
	private AskQuestionActionDefinition _action = null!;
	private TaskCompletionSource<ActiveNotification> _shown = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider { Now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero) };
		_manager = new NotificationManager(new UserNotificationStore(),
			TestLocalization.Resolver,
			TestLocalization.ScopeFactory,
			_time,
			Serilog.Core.Logger.None);
		_shown = new TaskCompletionSource<ActiveNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
		_manager.Published += notification =>
		{
			if (notification.Kind == NotificationEventKind.Shown)
			{
				_shown.TrySetResult(notification.Notification);
			}
		};

		_deck = new RecordingDeckNavigator
		{
			Clients =
			[
				Client("ws-phone", _phone),
				Client("ws-desktop", null),
				Client("device:" + _tablet, _tablet)
			]
		};
		_variables = new StoringUserVariableApi();
		_action = new AskQuestionActionDefinition(new NotificationServiceAccessor
				{ Current = new OwnedNotificationService(_system, _manager) },
			new DeckNavigatorAccessor { Current = _deck },
			new VariableApiAccessor { UserVariables = _variables });
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
	}

	[Test]
	public async Task A_confirmed_question_from_the_pressing_phone_stores_true_and_answered()
	{
		var run = Run(Confirm(), "ws-phone");
		var shown = await _shown.Task;

		Respond(Device(_phone), shown, "yes");
		var result = await run;

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(shown.Target.Kind, Is.EqualTo(NotificationTargetKind.Devices));
			Assert.That(shown.Target.DeviceIds, Is.EqualTo(new[] { _phone }).AsCollection);
			Assert.That(_variables.Values["answer"], Is.EqualTo("true"));
			Assert.That(_variables.Values["outcome"], Is.EqualTo("answered"));
		});
	}

	[Test]
	public async Task A_question_that_times_out_after_an_earlier_yes_does_not_leave_the_yes_behind()
	{
		_variables.Values["answer"] = "true";

		var run = Run(With(Confirm(), ("timeout", 5000.0)), "ws-phone");
		await _shown.Task;
		_time.Advance(TimeSpan.FromSeconds(6));
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Values["answer"], Is.EqualTo("false"));
			Assert.That(_variables.Values["outcome"], Is.EqualTo("timedOut"));
		});
	}

	[Test]
	public async Task A_choice_picked_in_the_vertical_list_stores_the_item_and_its_index()
	{
		var choices = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"Scene {i}"));

		var run = Run(Choose(choices), "ws-phone");
		var shown = await _shown.Task;
		var draft = ClientDraft(shown);
		draft[PrimaryChoice(shown)] = JsonSerializer.SerializeToElement(3);

		Assert.That(Respond(Device(_phone), shown, "ok", draft), Is.EqualTo(NotificationRespondStatus.Accepted));
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Values["answer"], Is.EqualTo("Scene 4"));
			Assert.That(_variables.Values["index"], Is.EqualTo("3"));
			Assert.That(_variables.Values["outcome"], Is.EqualTo("answered"));
		});
	}

	[Test]
	public async Task A_choice_picked_in_the_one_row_fallback_of_an_older_reader_is_stored_too()
	{
		var run = Run(Choose("""["Intro", "Gameplay", 3, true]"""), "ws-phone");
		var shown = await _shown.Task;
		var draft = ClientDraft(shown);
		var fallback = draft.Keys.Single(id => id != PrimaryChoice(shown));
		draft[fallback] = JsonSerializer.SerializeToElement(2);

		Respond(Device(_phone), shown, "ok", draft);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Values["answer"], Is.EqualTo("3"));
			Assert.That(_variables.Values["index"], Is.EqualTo("2"));
		});
	}

	[Test]
	public async Task Entered_text_is_stored_as_typed_and_never_rendered_as_a_template()
	{
		var run = Run(Text(), "ws-desktop");
		var shown = await _shown.Task;
		var draft = ClientDraft(shown);
		var field = draft.Keys.Single();
		draft[field] = JsonSerializer.SerializeToElement("{{ secret }} title");

		Respond(Admin(), shown, "ok", draft);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(shown.Target, Is.EqualTo(NotificationTarget.AdminUi));
			Assert.That(_variables.Values["answer"], Is.EqualTo("{{ secret }} title"));
		});
	}

	[Test]
	public async Task An_overlong_default_text_is_cut_to_the_field_limit_so_an_untouched_ok_is_accepted()
	{
		var run = Run(With(Text(), ("defaultText", new string('a', 1500))), "ws-phone");
		var shown = await _shown.Task;

		var status = Respond(Device(_phone), shown, "ok", ClientDraft(shown));
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(NotificationRespondStatus.Accepted));
			Assert.That(_variables.Values["answer"], Has.Length.EqualTo(1000));
		});
	}

	[TestCase("cancel")]
	[TestCase(null)]
	public async Task Cancelling_or_dismissing_the_question_stores_no_answer(string? actionId)
	{
		_variables.Values["answer"] = "old";

		var run = Run(Text(), "ws-phone");
		var shown = await _shown.Task;
		Respond(Device(_phone), shown, actionId);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Values["answer"], Is.Empty);
			Assert.That(_variables.Values["outcome"], Is.EqualTo("cancelled"));
		});
	}

	[TestCase("origin", "device:hardware")]
	[TestCase("origin", null)]
	[TestCase("origin", "ws-gone")]
	public async Task A_question_with_nobody_who_can_answer_it_ends_at_once_as_no_client(
		string target,
		string? origin)
	{
		var result = await Run(With(Confirm(), ("target", target)), origin).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_shown.Task.IsCompleted, Is.False, "nothing is shown");
			Assert.That(_variables.Values["outcome"], Is.EqualTo("noClient"));
		});
	}

	[Test]
	public async Task A_chosen_device_that_is_only_a_hardware_deck_cannot_be_asked()
	{
		await Run(With(Confirm(), ("target", "device"), ("deviceId", _tablet.ToString())), "ws-phone");

		Assert.That(_variables.Values["outcome"], Is.EqualTo("noClient"));
	}

	[Test]
	public async Task Asking_every_client_needs_one_that_can_show_a_dialog()
	{
		_deck.Clients.RemoveAll(client => !client.ClientId.StartsWith("device:", StringComparison.Ordinal));

		await Run(With(Confirm(), ("target", "all")), null);

		Assert.That(_variables.Values["outcome"], Is.EqualTo("noClient"));
	}

	[Test]
	public async Task A_chosen_connected_device_is_asked_whoever_pressed()
	{
		var run = Run(With(Confirm(), ("target", "device"), ("deviceId", _phone.ToString())), null);
		var shown = await _shown.Task;
		Respond(Device(_phone), shown, "no");
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(shown.Target.Kind, Is.EqualTo(NotificationTargetKind.Devices));
			Assert.That(shown.Target.DeviceIds, Is.EqualTo(new[] { _phone }).AsCollection);
			Assert.That(_variables.Values["answer"], Is.EqualTo("false"));
			Assert.That(_variables.Values["outcome"], Is.EqualTo("answered"));
		});
	}

	[Test]
	public async Task Cancelling_the_flow_while_it_waits_closes_the_question()
	{
		using var cancellation = new CancellationTokenSource();
		var run = Run(Confirm(), "ws-phone", cancellation.Token);
		var shown = await _shown.Task;

		await cancellation.CancelAsync();

		Assert.That(async () => await run, Throws.InstanceOf<OperationCanceledException>());
		Assert.That(_manager.Find(shown.Id), Is.Null);
	}

	[Test]
	public async Task Fifty_short_choices_fit_into_one_question()
	{
		var choices = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"Item {i}"));

		var run = Run(Choose(choices), "ws-phone");
		var shown = await _shown.Task;
		Respond(Device(_phone), shown, "ok", ClientDraft(shown));
		await run;

		Assert.That(_variables.Values["answer"], Is.EqualTo("Item 1"));
	}

	[Test]
	public async Task A_list_too_large_to_show_fails_with_a_localized_error_and_no_question()
	{
		var choices = string.Join('\n', Enumerable.Range(0, 50).Select(i => new string((char)('漢' + i), 100)));

		var result = await Run(Choose(choices), "ws-phone");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.System.Actions.AskQuestion.ChoicesTooLarge()));
			Assert.That(_shown.Task.IsCompleted, Is.False);
		});
	}

	[TestCase("")]
	[TestCase("[]")]
	public async Task An_empty_list_fails_before_anything_is_shown(string choices)
	{
		var result = await Run(Choose(choices), "ws-phone");

		Assert.That(result.ErrorMessage,
			Is.EqualTo((LocalizedText)AppStrings.Integrations.System.Actions.AskQuestion.NoChoices()));
	}

	[Test]
	public async Task An_answer_the_variable_refuses_fails_the_action_instead_of_reporting_answered()
	{
		_variables.Refused.Add("answer");

		var run = Run(Text(), "ws-phone");
		var shown = await _shown.Task;
		Respond(Device(_phone), shown, "ok", ClientDraft(shown));
		var result = await run;

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.System.Actions.AskQuestion
					.AnswerNotStored("answer")));
			Assert.That(_variables.Values.ContainsKey("outcome"), Is.False);
		});
	}

	[Test]
	public async Task More_than_fifty_choices_fail_before_anything_is_shown()
	{
		var result = await Run(Choose(string.Join('\n', Enumerable.Range(0, 51))), "ws-phone");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.System.Actions.AskQuestion.TooManyChoices("50")));
			Assert.That(_shown.Task.IsCompleted, Is.False);
		});
	}

	private static Dictionary<string, object> Confirm()
	{
		return new Dictionary<string, object>
		{
			["kind"] = "confirm",
			["title"] = "Really shut down?",
			["answerVariable"] = "answer",
			["outcomeVariable"] = "outcome"
		};
	}

	private static Dictionary<string, object> Choose(string choices)
	{
		return new Dictionary<string, object>
		{
			["kind"] = "choose",
			["title"] = "Which scene?",
			["choices"] = choices,
			["answerVariable"] = "answer",
			["indexVariable"] = "index",
			["outcomeVariable"] = "outcome"
		};
	}

	private static Dictionary<string, object> Text()
	{
		return new Dictionary<string, object>
		{
			["kind"] = "text",
			["title"] = "Clip title",
			["defaultText"] = "My clip",
			["answerVariable"] = "answer",
			["outcomeVariable"] = "outcome"
		};
	}

	private static Dictionary<string, object> With(
		Dictionary<string, object> parameters,
		params (string Key, object Value)[] changes)
	{
		foreach (var (key, value) in changes)
		{
			parameters[key] = value;
		}

		return parameters;
	}

	private Task<ActionResult> Run(
		Dictionary<string, object> parameters,
		string? originClientId,
		CancellationToken cancellationToken = default)
	{
		return _action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters, OriginClientId = originClientId, CancellationToken = cancellationToken
			});
	}

	private NotificationRespondStatus Respond(
		NotificationPrincipal principal,
		ActiveNotification shown,
		string? actionId,
		IReadOnlyDictionary<string, JsonElement>? values = null)
	{
		return _manager.Respond(principal, shown.Id, actionId, values).Status;
	}

	// What createNotificationDraft in ui/runtime sends: every input of the tree, at its seed unless touched.
	private static Dictionary<string, JsonElement> ClientDraft(ActiveNotification shown)
	{
		return NotificationContent.Inspect(shown.Content!).Fields.ToDictionary(field => field.Id, field => field.Seed);
	}

	private static string PrimaryChoice(ActiveNotification shown)
	{
		return Walk(shown.Content!).Single(node => node.Type == "ui.segmented").Id;
	}

	private static IEnumerable<MacroDeck.Ui.Model.Nodes.UiNode> Walk(MacroDeck.Ui.Model.Nodes.UiNode node)
	{
		yield return node;

		foreach (var descendant in node.Children.SelectMany(Walk))
		{
			yield return descendant;
		}
	}

	private static DeckClient Client(string clientId, Guid? deviceId)
	{
		return new DeckClient
		{
			ClientId = clientId,
			DeviceId = deviceId?.ToString("D"),
			ProfileId = "profile",
			FolderId = "folder",
			IsDeviceSession = clientId.StartsWith("device:", StringComparison.Ordinal)
		};
	}

	private static NotificationPrincipal Device(Guid deviceId)
	{
		return new NotificationPrincipal(false, deviceId);
	}

	private static NotificationPrincipal Admin()
	{
		return new NotificationPrincipal(true, null);
	}

	private sealed class StoringUserVariableApi : IUserVariableApi
	{
		public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

		public HashSet<string> Refused { get; } = new(StringComparer.Ordinal);

		public Task<UserVariableWriteResult> ApplyInContextAsync(
			string name,
			UserVariableContext context,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			return ApplyAsync(name, context.OwnerWidgetId, operation, value, cancellationToken);
		}

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			if (Refused.Contains(name))
			{
				return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.InvalidValue, "refused"));
			}

			Values[name] = value ?? string.Empty;
			return Task.FromResult(UserVariableWriteResult.Applied());
		}
	}
}
