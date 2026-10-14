using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteNotificationServiceTests
{
	private readonly Guid _id = Guid.NewGuid();
	private ScriptedInvoker _invoker = null!;
	private PluginConnectionState _state = null!;
	private NotificationResultStore _results = null!;
	private RemoteNotificationService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_invoker = new ScriptedInvoker
		{
			Reply = _ => JsonSerializer.SerializeToElement(new NotificationsShowResult { Id = _id },
				PluginProtocolJson.Options)
		};
		_state = new PluginConnectionState();
		_results = new NotificationResultStore();
		_service = new RemoteNotificationService(_invoker, _state, _results);
	}

	[TearDown]
	public void TearDown()
	{
		_service.Dispose();
		_invoker.Dispose();
	}

	[Test]
	public async Task Showing_sends_the_options_on_the_wire_and_returns_the_hosts_id()
	{
		var content = new UiNode { Id = "form", Type = "ui.stack" };

		var handle = await _service.ShowAsync(new NotificationOptions
		{
			Title = "Pick one",
			Severity = NotificationSeverity.Warning,
			Popup = NotificationPopup.Dialog,
			Inbox = true,
			Duration = TimeSpan.FromSeconds(30),
			Target = NotificationTarget.Devices(Guid.Parse("11111111-1111-1111-1111-111111111111")),
			Actions = [new NotificationAction { Id = "yes", Label = "Yes", Style = NotificationActionStyle.Primary }],
			Content = content
		});

		var sent = (NotificationsShowArguments)_invoker.Calls.Single().Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(handle.Id, Is.EqualTo(_id));
			Assert.That(_invoker.Calls.Single().Api, Is.EqualTo(HostApis.Notifications));
			Assert.That(_invoker.Calls.Single().Operation, Is.EqualTo(HostOperations.Notifications.Show));
			Assert.That(sent.Severity, Is.EqualTo(NotificationsWire.Severity.Warning));
			Assert.That(sent.Popup, Is.EqualTo(NotificationsWire.Popup.Dialog));
			Assert.That(sent.TargetKind, Is.EqualTo(NotificationsWire.TargetKind.Devices));
			Assert.That(sent.TargetDeviceIds, Has.Count.EqualTo(1));
			Assert.That(sent.DurationMs, Is.EqualTo(30_000));
			Assert.That(sent.Inbox, Is.True);
			Assert.That(sent.Actions!.Single().Style, Is.EqualTo(NotificationsWire.ActionStyle.Primary));
			Assert.That(sent.Content!.Value.GetProperty("id").GetString(), Is.EqualTo("form"));
		});
	}

	[Test]
	public async Task The_handle_completes_once_with_what_the_host_reports()
	{
		var handle = await _service.ShowAsync(new NotificationOptions
			{ Title = "Q", Popup = NotificationPopup.Dialog });

		_results.Complete(Result(NotificationsWire.CloseReason.Action,
			"yes",
			"admin",
			new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") }));
		_results.Complete(Result(NotificationsWire.CloseReason.Dismissed, null, "other"));
		var result = await handle.Completion;

		Assert.Multiple(() =>
		{
			Assert.That(result.CloseReason, Is.EqualTo(NotificationCloseReason.Action));
			Assert.That(result.ActionId, Is.EqualTo("yes"));
			Assert.That(result.ClientId, Is.EqualTo("admin"));
			Assert.That(result.Values["name"].GetString(), Is.EqualTo("Ada"));
		});
	}

	[Test]
	public async Task A_result_that_arrives_before_show_returned_is_not_lost()
	{
		_invoker.Reply = _ =>
		{
			_results.Complete(Result(NotificationsWire.CloseReason.Timeout, null, null));
			return JsonSerializer.SerializeToElement(new NotificationsShowResult { Id = _id },
				PluginProtocolJson.Options);
		};

		var handle = await _service.ShowAsync(new NotificationOptions { Title = "Quick" });

		Assert.That((await handle.Completion).CloseReason, Is.EqualTo(NotificationCloseReason.Timeout));
	}

	[Test]
	public async Task An_unknown_close_reason_reads_as_dismissed()
	{
		var handle = await _service.ShowAsync(new NotificationOptions { Title = "Q" });

		_results.Complete(Result("something-newer", null, null));

		Assert.That((await handle.Completion).CloseReason, Is.EqualTo(NotificationCloseReason.Dismissed));
	}

	[Test]
	public async Task A_new_session_ends_every_pending_notification_as_host_unavailable_but_a_resume_does_not()
	{
		var handle = await _service.ShowAsync(new NotificationOptions
			{ Title = "Q", Popup = NotificationPopup.Dialog });

		_state.RaiseConnected(true);
		var afterResume = handle.Completion.IsCompleted;
		_state.RaiseConnected(false);

		Assert.That(afterResume, Is.False);
		Assert.That((await handle.Completion).CloseReason, Is.EqualTo(NotificationCloseReason.HostUnavailable));
	}

	[Test]
	public void A_host_that_predates_notifications_is_reported_as_unsupported()
	{
		_invoker.Failure = HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported,
			"The 'notifications' api has no operation 'show'.");

		var exception = Assert.ThrowsAsync<NotificationException>(() =>
			_service.ShowAsync(new NotificationOptions { Title = "Hello" }));

		Assert.That(exception!.ErrorCode, Is.EqualTo(NotificationErrorCode.Unsupported));
	}

	[Test]
	public async Task Updating_and_dismissing_name_the_notification()
	{
		var handle = await _service.ShowAsync(new NotificationOptions { Title = "Sync" });

		await handle.UpdateAsync(new NotificationUpdate { Progress = new NotificationProgress(3, 10) });
		await handle.DismissAsync();

		var update = (NotificationsUpdateArguments)_invoker.Calls[1].Arguments!;
		var close = (NotificationsCloseArguments)_invoker.Calls[2].Arguments!;
		Assert.Multiple(() =>
		{
			Assert.That(_invoker.Calls[1].Operation, Is.EqualTo(HostOperations.Notifications.Update));
			Assert.That(update.Id, Is.EqualTo(_id));
			Assert.That(update.Progress!.Processed, Is.EqualTo(3));
			Assert.That(_invoker.Calls[2].Operation, Is.EqualTo(HostOperations.Notifications.Close));
			Assert.That(close.Id, Is.EqualTo(_id));
		});
	}

	[Test]
	public async Task Cancelling_the_wait_dismisses_the_notification_and_throws()
	{
		using var cancellation = new CancellationTokenSource();

		var wait = _service.ShowForResultAsync(
			new NotificationOptions { Title = "Q", Popup = NotificationPopup.Dialog },
			cancellation.Token);
		await _invoker.WaitForCallsAsync(1);
		await cancellation.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await wait);
		await _invoker.WaitForCallsAsync(2);
		Assert.That(_invoker.Calls[1].Operation, Is.EqualTo(HostOperations.Notifications.Close));
	}

	private UiNotificationResultArguments Result(
		string reason,
		string? actionId,
		string? clientId,
		IReadOnlyDictionary<string, JsonElement>? values = null)
	{
		return new UiNotificationResultArguments
		{
			NotificationId = _id, CloseReason = reason, ActionId = actionId, ClientId = clientId, Values = values
		};
	}

	private sealed record Call(string Api, string Operation, object? Arguments);

	private sealed class ScriptedInvoker : IHostInvoker, IDisposable
	{
		private readonly Lock _gate = new();
		private readonly List<Call> _calls = [];
		private readonly SemaphoreSlim _signal = new(0);

		public Func<Call, JsonElement?> Reply { get; set; } = _ => null;

		public HostInvocationException? Failure { get; set; }

		public IReadOnlyList<Call> Calls
		{
			get
			{
				lock (_gate)
				{
					return [.. _calls];
				}
			}
		}

		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			var call = new Call(api, operation, arguments);
			lock (_gate)
			{
				_calls.Add(call);
			}

			_signal.Release();

			if (Failure is { } failure)
			{
				return Task.FromException<JsonElement?>(failure);
			}

			return Task.FromResult(string.Equals(operation, HostOperations.Notifications.Show, StringComparison.Ordinal)
				? Reply(call)
				: null);
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}

		public void Dispose()
		{
			_signal.Dispose();
		}

		public async Task WaitForCallsAsync(int count)
		{
			while (Calls.Count < count)
			{
				await _signal.WaitAsync(TimeSpan.FromSeconds(5));
			}
		}
	}
}
