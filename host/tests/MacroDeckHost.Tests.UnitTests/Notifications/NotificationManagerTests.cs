using System.Text.Json;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Nodes;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Notifications;

public class NotificationManagerTests
{
	private static readonly NotificationOwner _plugin =
		new(NotificationSourceCategory.Plugin, "com.example.plugin", "Example Plugin");

	private static readonly NotificationOwner _otherPlugin =
		new(NotificationSourceCategory.Plugin, "com.example.other", "Other Plugin");

	private static readonly NotificationPrincipal _admin = new(true, null);
	private static readonly NotificationPrincipal _device = new(false, Guid.NewGuid());
	private static readonly NotificationPrincipal _otherDevice = new(false, Guid.NewGuid());

	private FakeTimeProvider _time = null!;
	private UserNotificationStore _inbox = null!;
	private NotificationManager _manager = null!;
	private List<NotificationEvent> _events = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider { Now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero) };
		_inbox = new UserNotificationStore();
		_manager = new NotificationManager(_inbox,
			TestLocalization.Resolver,
			TestLocalization.ScopeFactory,
			_time,
			Serilog.Core.Logger.None);
		_events = [];
		_manager.Published += _events.Add;
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
	}

	[Test]
	public async Task The_first_valid_response_wins_and_the_result_is_produced_once()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("yes"), Ok("no")));

		var first = _manager.Respond(_device, shown.Id, "yes", null);
		var second = _manager.Respond(_admin, shown.Id, "no", null);
		var result = await shown.Completion;

		Assert.Multiple(() =>
		{
			Assert.That(first.Status, Is.EqualTo(NotificationRespondStatus.Accepted));
			Assert.That(second.Status, Is.EqualTo(NotificationRespondStatus.AlreadyClosed));
			Assert.That(result.CloseReason, Is.EqualTo(NotificationCloseReason.Action));
			Assert.That(result.ActionId, Is.EqualTo("yes"));
			Assert.That(result.ClientId, Is.EqualTo(_device.DeviceId.ToString()));
			Assert.That(_events.Count(e => e.Kind == NotificationEventKind.Closed), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Concurrent_responses_from_many_clients_accept_exactly_one()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("a"), Ok("b")));

		var outcomes = await Task.WhenAll(Enumerable.Range(0, 32)
			.Select(i => Task.Run(()
				=> _manager.Respond(i % 2 == 0 ? _admin : _device, shown.Id, i % 2 == 0 ? "a" : "b", null))));

		Assert.That(outcomes.Count(o => o.Status == NotificationRespondStatus.Accepted), Is.EqualTo(1));
		Assert.That((await shown.Completion).CloseReason, Is.EqualTo(NotificationCloseReason.Action));
	}

	[Test]
	public async Task An_undeclared_action_is_refused_and_the_popup_stays_open()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("yes")));

		var outcome = _manager.Respond(_admin, shown.Id, "delete-everything", null);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Status, Is.EqualTo(NotificationRespondStatus.Invalid));
			Assert.That(shown.Completion.IsCompleted, Is.False);
			Assert.That(_manager.Find(shown.Id), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_client_outside_the_target_cannot_respond()
	{
		var options = Dialog(Ok("yes")) with { Target = NotificationTarget.Devices(_device.DeviceId!.Value) };
		var shown = await _manager.ShowAsync(_plugin, options);

		var stranger = _manager.Respond(_otherDevice, shown.Id, "yes", null);
		var admin = _manager.Respond(_admin, shown.Id, "yes", null);
		var member = _manager.Respond(_device, shown.Id, "yes", null);

		Assert.Multiple(() =>
		{
			Assert.That(stranger.Status, Is.EqualTo(NotificationRespondStatus.Forbidden));
			Assert.That(admin.Status, Is.EqualTo(NotificationRespondStatus.Forbidden));
			Assert.That(member.Status, Is.EqualTo(NotificationRespondStatus.Accepted));
		});
	}

	[Test]
	public async Task The_admin_role_target_is_not_visible_to_a_device()
	{
		await _manager.ShowAsync(_plugin, Dialog(Ok("yes")) with { Target = NotificationTarget.AdminUi });

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Snapshot(_admin), Has.Count.EqualTo(1));
			Assert.That(_manager.Snapshot(_device), Is.Empty);
		});
	}

	[Test]
	public async Task Dismissing_closes_the_popup_for_everyone_unless_it_cannot_be_dismissed()
	{
		var closable = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		var pinned = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")) with { Dismissible = false });

		var dismissed = _manager.Respond(_device, closable.Id, null, null);
		var refused = _manager.Respond(_device, pinned.Id, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(dismissed.Status, Is.EqualTo(NotificationRespondStatus.Accepted));
			Assert.That(closable.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.Dismissed));
			Assert.That(refused.Status, Is.EqualTo(NotificationRespondStatus.Invalid));
			Assert.That(pinned.Completion.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task The_host_closes_a_toast_when_its_duration_elapses()
	{
		var shown = await _manager.ShowAsync(_plugin, new NotificationOptions { Title = "Saved" });

		_time.Advance(NotificationLimits.DefaultToastDuration + TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(shown.Completion.IsCompleted, Is.True);
			Assert.That(shown.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.Timeout));
			Assert.That(_manager.Find(shown.Id), Is.Null);
		});
	}

	[Test]
	public async Task No_popup_outlives_the_host_maximum_lifetime()
	{
		var shown = await _manager.ShowAsync(_plugin,
			Dialog(Ok("ok")) with { Duration = TimeSpan.FromDays(3) });

		_time.Advance(NotificationLimits.MaxLifetime + TimeSpan.FromSeconds(1));

		Assert.That(shown.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.Timeout));
	}

	[Test]
	public async Task Only_the_owner_can_update_or_close_a_notification()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));

		var update = Assert.ThrowsAsync<NotificationException>(() =>
			_manager.UpdateAsync(_otherPlugin, shown.Id, new NotificationUpdate { Title = "Hijacked" }));
		var close = Assert.Throws<NotificationException>(() => _manager.Close(_otherPlugin, shown.Id));

		Assert.Multiple(() =>
		{
			Assert.That(update!.ErrorCode, Is.EqualTo(NotificationErrorCode.Forbidden));
			Assert.That(close!.ErrorCode, Is.EqualTo(NotificationErrorCode.Forbidden));
			Assert.That(shown.Completion.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task An_update_raises_the_revision_each_time()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		var revisions = new List<long> { _manager.Find(shown.Id)!.Revision };

		await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = "Step 1" });
		revisions.Add(_manager.Find(shown.Id)!.Revision);
		await _manager.UpdateAsync(_plugin,
			shown.Id,
			new NotificationUpdate { Progress = new NotificationProgress(2, 4) });
		revisions.Add(_manager.Find(shown.Id)!.Revision);

		Assert.That(revisions, Is.Ordered.Ascending.And.Unique);
		Assert.That(_manager.Find(shown.Id)!.Progress, Is.EqualTo(new NotificationProgress(2, 4)));
	}

	[Test]
	public void A_notification_needs_a_popup_or_an_inbox_entry()
	{
		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Nothing", Popup = NotificationPopup.None }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Invalid));
	}

	[Test]
	public void Only_a_dialog_can_carry_content()
	{
		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Form", Popup = NotificationPopup.Toast, Content = Form() }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Invalid));
	}

	[Test]
	public void A_dialog_without_a_way_to_close_is_refused()
	{
		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Stuck", Popup = NotificationPopup.Dialog, Dismissible = false }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Invalid));
	}

	[Test]
	public async Task Form_values_are_returned_seeded_and_validated()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("send")) with { Content = Form() });

		var unknown = _manager.Respond(_admin, shown.Id, "send", Values(("password", "x")));
		var wrongType = _manager.Respond(_admin, shown.Id, "send", Values(("name", 5)));
		var outOfRange = _manager.Respond(_admin, shown.Id, "send", Values(("level", 1.5)));
		var stillOpen = !shown.Completion.IsCompleted;
		var accepted = _manager.Respond(_admin, shown.Id, "send", Values(("name", "Ada"), ("choice", 1)));
		var result = await shown.Completion;

		Assert.Multiple(() =>
		{
			Assert.That(unknown.Status, Is.EqualTo(NotificationRespondStatus.Invalid));
			Assert.That(wrongType.Status, Is.EqualTo(NotificationRespondStatus.Invalid));
			Assert.That(outOfRange.Status, Is.EqualTo(NotificationRespondStatus.Invalid));
			Assert.That(stillOpen, Is.True);
			Assert.That(accepted.Status, Is.EqualTo(NotificationRespondStatus.Accepted));
			Assert.That(result.Values["name"].GetString(), Is.EqualTo("Ada"));
			Assert.That(result.Values["choice"].GetInt32(), Is.EqualTo(1));
			Assert.That(result.Values["subscribed"].GetBoolean(), Is.True);
			Assert.That(result.Values["level"].GetDouble(), Is.EqualTo(0.5));
		});
	}

	[Test]
	public async Task A_popup_with_an_inbox_entry_keeps_it_after_the_popup_closes()
	{
		var shown = await _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Backup done", Popup = NotificationPopup.Toast, Inbox = true });

		_manager.Respond(_admin, shown.Id, null, null);

		var entry = _inbox.Snapshot().Single();
		Assert.Multiple(() =>
		{
			Assert.That(shown.Completion.IsCompleted, Is.True);
			Assert.That(entry.Id, Is.EqualTo(shown.Id.ToString()));
			Assert.That(entry.Title, Is.EqualTo("Backup done"));
			Assert.That(entry.SourceCategory, Is.EqualTo(NotificationSourceCategory.Plugin));
			Assert.That(entry.Read, Is.False);
		});
	}

	[Test]
	public async Task Dismissing_the_inbox_entry_does_not_close_the_popup()
	{
		var shown = await _manager.ShowAsync(_plugin,
			Dialog(Ok("ok")) with { Inbox = true });

		_inbox.Dismiss(shown.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(_inbox.Snapshot(), Is.Empty);
			Assert.That(_manager.Find(shown.Id), Is.Not.Null);
			Assert.That(shown.Completion.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task An_update_changes_the_inbox_entry_without_adding_a_second_one()
	{
		var shown = await _manager.ShowAsync(_plugin,
			Dialog(Ok("ok")) with { Inbox = true, Title = "Syncing" });

		await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Title = "Synced" });

		var entry = _inbox.Snapshot().Single();
		Assert.That(entry.Title, Is.EqualTo("Synced"));
	}

	[Test]
	public async Task A_popup_without_an_inbox_entry_leaves_the_inbox_alone()
	{
		await _manager.ShowAsync(_plugin, new NotificationOptions { Title = "Quiet" });

		Assert.That(_inbox.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Closing_an_owner_closes_everything_it_has_open()
	{
		var first = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		var second = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		var other = await _manager.ShowAsync(_otherPlugin, Dialog(Ok("ok")));

		var closed = _manager.CloseOwner(_plugin.Id);

		Assert.Multiple(() =>
		{
			Assert.That(closed, Is.EqualTo(2));
			Assert.That(first.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.OwnerGone));
			Assert.That(second.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.OwnerGone));
			Assert.That(other.Completion.IsCompleted, Is.False);
		});
	}

	[Test]
	public async Task A_snapshot_lists_only_open_popups_a_principal_may_see()
	{
		var open = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		await _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Inbox only", Popup = NotificationPopup.None, Inbox = true });
		var closed = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		_manager.Respond(_admin, closed.Id, "ok", null);

		var snapshot = _manager.Snapshot(_device);

		Assert.That(snapshot.Select(n => n.Id), Is.EqualTo(new[] { open.Id }));
	}

	[Test]
	public async Task The_source_comes_from_the_caller_and_not_from_the_options()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")) with { Title = "Macro Deck" });

		var view = _manager.Find(shown.Id)!;

		Assert.Multiple(() =>
		{
			Assert.That(view.Owner.Category, Is.EqualTo(NotificationSourceCategory.Plugin));
			Assert.That(view.Owner.Id, Is.EqualTo(_plugin.Id));
		});
	}

	[Test]
	public async Task One_owner_cannot_keep_more_than_the_allowed_notifications_open()
	{
		for (var i = 0; i < NotificationLimits.MaxActivePerOwner; i++)
		{
			await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
			_time.Advance(TimeSpan.FromSeconds(3));
		}

		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin, Dialog(Ok("ok"))));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.LimitExceeded));
	}

	[Test]
	public async Task Creating_notifications_faster_than_the_rate_limit_is_refused()
	{
		var refused = 0;
		for (var i = 0; i < NotificationLimits.CreatesPerMinutePerOwner + 5; i++)
		{
			try
			{
				var shown = await _manager.ShowAsync(_plugin, new NotificationOptions { Title = "Spam" });
				_manager.Close(_plugin, shown.Id);
			}
			catch (NotificationException exception) when (exception.ErrorCode == NotificationErrorCode.LimitExceeded)
			{
				refused++;
			}
		}

		Assert.That(refused, Is.EqualTo(5));
	}

	[Test]
	public void Content_larger_than_the_allowed_size_is_refused()
	{
		var big = new UiNode
		{
			Id = "root",
			Type = "ui.text",
			Properties = new Dictionary<string, JsonElement>
			{
				["text"] = JsonSerializer.SerializeToElement(new string('x', NotificationLimits.MaxContentBytes))
			}
		};

		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin,
			Dialog(Ok("ok")) with { Content = big }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.LimitExceeded));
	}

	[Test]
	public async Task Showing_and_closing_are_published_to_subscribers_in_order()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));
		await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = "More" });
		_manager.Respond(_admin, shown.Id, "ok", null);

		Assert.That(_events.Select(e => e.Kind),
			Is.EqualTo(new[]
				{ NotificationEventKind.Shown, NotificationEventKind.Updated, NotificationEventKind.Closed }));
		Assert.That(_events.Select(e => e.Notification.Revision), Is.Ordered.Ascending);
	}

	[Test]
	public async Task An_inbox_only_notification_leaves_an_entry_and_holds_no_popup_slot()
	{
		for (var i = 0; i < NotificationLimits.MaxActivePerOwner + 5; i++)
		{
			var shown = await _manager.ShowAsync(_plugin,
				new NotificationOptions { Title = $"Note {i}", Popup = NotificationPopup.None, Inbox = true });
			_time.Advance(TimeSpan.FromSeconds(3));

			Assert.That(shown.Completion.IsCompleted, Is.True);
		}

		var popup = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")));

		Assert.Multiple(() =>
		{
			Assert.That(_inbox.Snapshot(), Has.Count.EqualTo(NotificationLimits.MaxActivePerOwner + 5));
			Assert.That(_manager.Snapshot(_admin).Select(n => n.Id), Is.EqualTo(new[] { popup.Id }));
		});
	}

	[Test]
	public void Inbox_entries_raised_through_the_store_interface_land_in_the_inbox_and_can_be_read_and_dismissed()
	{
		var store = AsStore(_manager);
		var entry = store.Raise(new UserNotificationDraft
		{
			Severity = UserNotificationSeverity.Warning,
			Kind = UserNotificationKind.Security,
			Title = "Failed login"
		})!;

		var read = store.MarkRead(entry.Id);
		var dismissed = store.Dismiss(entry.Id);

		Assert.Multiple(() =>
		{
			Assert.That(read, Is.True);
			Assert.That(dismissed, Is.True);
			Assert.That(_inbox.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task A_toast_that_keeps_getting_updates_stays_open_and_closes_once_they_stop()
	{
		var shown = await _manager.ShowAsync(_plugin, new NotificationOptions { Title = "Exporting" });

		for (var step = 1; step <= 4; step++)
		{
			_time.Advance(TimeSpan.FromSeconds(4));
			await _manager.UpdateAsync(_plugin,
				shown.Id,
				new NotificationUpdate { Progress = new NotificationProgress(step, 4) });
		}

		var stillOpen = !shown.Completion.IsCompleted;
		_time.Advance(NotificationLimits.DefaultToastDuration + TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(stillOpen, Is.True);
			Assert.That(shown.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.Timeout));
		});
	}

	[Test]
	public async Task Updates_never_push_a_popup_past_the_maximum_lifetime()
	{
		var shown = await _manager.ShowAsync(_plugin, new NotificationOptions { Title = "Long job" });

		for (var minute = 0; minute < 65 && !shown.Completion.IsCompleted; minute++)
		{
			_time.Advance(TimeSpan.FromMinutes(1));
			if (!shown.Completion.IsCompleted)
			{
				await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = $"minute {minute}" });
			}
		}

		Assert.That(shown.Completion.Result.CloseReason, Is.EqualTo(NotificationCloseReason.Timeout));
	}

	[Test]
	public async Task An_update_does_not_bring_back_an_inbox_entry_the_user_removed()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")) with { Inbox = true });
		_inbox.Dismiss(shown.Id.ToString());

		await _manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Message = "Changed" });

		Assert.That(_inbox.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task An_update_cannot_leave_a_dialog_with_no_way_to_close_it()
	{
		var shown = await _manager.ShowAsync(_plugin, Dialog(Ok("ok")) with { Dismissible = false });

		var exception = Assert.ThrowsAsync<NotificationException>(() =>
			_manager.UpdateAsync(_plugin, shown.Id, new NotificationUpdate { Actions = [] }));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Invalid));
			Assert.That(_manager.Find(shown.Id)!.Actions, Has.Count.EqualTo(1));
		});
	}

	[TestCase(-1, null)]
	[TestCase(1, 0)]
	public void Progress_that_cannot_be_shown_is_refused(int processed, int? total)
	{
		var exception = Assert.ThrowsAsync<NotificationException>(() => _manager.ShowAsync(_plugin,
			new NotificationOptions { Title = "Bad", Progress = new NotificationProgress(processed, total) }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Invalid));
	}

	private static IUserNotificationStore AsStore(object manager)
	{
		return (IUserNotificationStore)manager;
	}

	private static NotificationOptions Dialog(params NotificationAction[] actions)
	{
		return new NotificationOptions
		{
			Title = "Question",
			Popup = NotificationPopup.Dialog,
			Actions = actions
		};
	}

	private static NotificationAction Ok(string id)
	{
		return new NotificationAction { Id = id, Label = id };
	}

	private static Dictionary<string, JsonElement> Values(params (string Key, object Value)[] values)
	{
		return values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value));
	}

	private static UiNode Form()
	{
		return new UiNode
		{
			Id = "form",
			Type = "ui.stack",
			Children =
			[
				Input("name", "ui.text-field"),
				Input("subscribed", "ui.toggle", ("on", true)),
				Input("level", "ui.slider", ("level", 0.5)),
				Input("choice", "ui.segmented", ("selected", 0)) with
				{
					Children =
					[
						new UiNode { Id = "choice-a", Type = "ui.text" },
						new UiNode { Id = "choice-b", Type = "ui.text" }
					]
				}
			]
		};
	}

	private static UiNode Input(string id, string type, params (string Key, object Value)[] properties)
	{
		var map = new Dictionary<string, JsonElement>
		{
			["events"] = JsonSerializer.SerializeToElement(new[] { "change" })
		};

		foreach (var (key, value) in properties)
		{
			map[key] = JsonSerializer.SerializeToElement(value);
		}

		return new UiNode { Id = id, Type = type, Properties = map };
	}
}
