using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Integrations.System.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ShowNotificationActionTests
{
	private NotificationServiceAccessor _accessor = null!;
	private CapturingNotificationService _service = null!;
	private ShowNotificationActionDefinition _action = null!;

	[SetUp]
	public void SetUp()
	{
		_service = new CapturingNotificationService();
		_accessor = new NotificationServiceAccessor { Current = _service };
		_action = new ShowNotificationActionDefinition(_accessor);
	}

	[Test]
	public async Task A_toast_with_a_timeout_is_shown_with_what_the_parameters_say()
	{
		var result = await Execute(new Dictionary<string, object>
		{
			["title"] = "Backup finished",
			["message"] = "12 files",
			["severity"] = "success",
			["presentation"] = "toast-inbox",
			["timeout"] = 8000.0
		});

		var shown = _service.Shown.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(shown.Title.Literal, Is.EqualTo("Backup finished"));
			Assert.That(shown.Message!.Value.Literal, Is.EqualTo("12 files"));
			Assert.That(shown.Severity, Is.EqualTo(NotificationSeverity.Success));
			Assert.That(shown.Popup, Is.EqualTo(NotificationPopup.Toast));
			Assert.That(shown.Inbox, Is.True);
			Assert.That(shown.Duration, Is.EqualTo(TimeSpan.FromSeconds(8)));
			Assert.That(shown.Actions, Is.Empty);
		});
	}

	[TestCase("toast", NotificationPopup.Toast, false)]
	[TestCase("toast-inbox", NotificationPopup.Toast, true)]
	[TestCase("dialog", NotificationPopup.Dialog, false)]
	[TestCase("dialog-inbox", NotificationPopup.Dialog, true)]
	[TestCase("inbox", NotificationPopup.None, true)]
	public async Task The_presentation_choice_maps_to_a_popup_and_an_inbox_entry(
		string presentation,
		NotificationPopup popup,
		bool inbox)
	{
		await Execute(new Dictionary<string, object> { ["title"] = "Hello", ["presentation"] = presentation });

		var shown = _service.Shown.Single();
		Assert.Multiple(() =>
		{
			Assert.That(shown.Popup, Is.EqualTo(popup));
			Assert.That(shown.Inbox, Is.EqualTo(inbox));
		});
	}

	[Test]
	public async Task A_dialog_without_a_timeout_gets_an_ok_button_so_it_can_always_be_closed()
	{
		await Execute(new Dictionary<string, object> { ["title"] = "Read this", ["presentation"] = "dialog" });

		var shown = _service.Shown.Single();
		Assert.That(shown.Actions.Select(a => a.Id), Is.EqualTo(new[] { "ok" }));
	}

	[Test]
	public async Task The_action_never_waits_for_the_user()
	{
		_service.NeverCompletes = true;

		var result = await Execute(new Dictionary<string, object>
				{ ["title"] = "Read this", ["presentation"] = "dialog" })
			.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task Fails_with_a_localized_error_before_the_integration_is_initialized()
	{
		_accessor.Current = null;

		var result = await Execute(new Dictionary<string, object> { ["title"] = "Hello" });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(result.ErrorMessage.IsLocalized, Is.True);
		});
	}

	[Test]
	public async Task Fails_when_the_host_refuses_the_notification()
	{
		_service.Refuse = true;

		var result = await Execute(new Dictionary<string, object> { ["title"] = "Hello" });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorMessage.IsLocalized, Is.True);
		});
	}

	[Test]
	public async Task Fails_without_a_title()
	{
		var result = await Execute(new Dictionary<string, object> { ["title"] = "   " });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_service.Shown, Is.Empty);
		});
	}

	private Task<ActionResult> Execute(Dictionary<string, object> parameters)
	{
		return _action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}

	private sealed class CapturingNotificationService : INotificationService
	{
		private readonly List<NotificationOptions> _shown = [];

		public bool Refuse { get; set; }

		public bool NeverCompletes { get; set; }

		public IReadOnlyList<NotificationOptions> Shown => _shown;

		public Task<INotificationHandle> ShowAsync(
			NotificationOptions options,
			CancellationToken cancellationToken = default)
		{
			if (Refuse)
			{
				return Task.FromException<INotificationHandle>(
					new NotificationException(NotificationErrorCode.LimitExceeded, "too many"));
			}

			_shown.Add(options);
			return Task.FromResult<INotificationHandle>(new Handle(NeverCompletes));
		}

		public Task<NotificationResult> ShowForResultAsync(
			NotificationOptions options,
			CancellationToken cancellationToken = default)
		{
			throw new InvalidOperationException("The action must not wait for an answer.");
		}

		private sealed class Handle(bool neverCompletes) : INotificationHandle
		{
			public Guid Id { get; } = Guid.NewGuid();

			public Task<NotificationResult> Completion { get; } = neverCompletes
				? new TaskCompletionSource<NotificationResult>().Task
				: Task.FromResult(new NotificationResult { CloseReason = NotificationCloseReason.Dismissed });

			public Task UpdateAsync(NotificationUpdate update, CancellationToken cancellationToken = default)
			{
				return Task.CompletedTask;
			}

			public Task DismissAsync(CancellationToken cancellationToken = default)
			{
				return Task.CompletedTask;
			}
		}
	}
}
