using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Email;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class EmailInboxCapabilityHandlerTests
{
	private static readonly DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2));

	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp()
	{
		_services = new ServiceCollection().BuildServiceProvider();
	}

	[OneTimeTearDown]
	public static void OneTimeTearDown()
	{
		_services.Dispose();
	}

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id_and_no_provider_declares_nothing()
	{
		using var withProvider = Handler(new TestEmailInboxIntegration("IMAP", "work"));
		using var withoutProvider = Handler();

		Assert.Multiple(() =>
		{
			Assert.That(withProvider.DeclareCapabilities().Select(capability => (capability.Kind, capability.LocalId)),
				Is.EqualTo(new[] { (CapabilityKinds.EmailInbox, ProviderCapabilityId.LocalId) }));
			Assert.That(withoutProvider.DeclareCapabilities(), Is.Empty);
		});
	}

	[Test]
	public async Task Describe_merges_the_inboxes_of_every_provider_once_and_falls_back_to_the_manifest_name()
	{
		var first = new TestEmailInboxIntegration(string.Empty, "work");
		var second = new TestEmailInboxIntegration(string.Empty, "work", "home");
		using var handler = Handler(first, second);

		var described = await InvokeAsync<EmailInboxDescribePayload>(handler, CapabilityOperations.EmailInbox.Describe);
		var inboxes = await InvokeAsync<EmailInboxesResult>(handler, CapabilityOperations.EmailInbox.Inboxes);

		Assert.Multiple(() =>
		{
			Assert.That(described.ProviderName, Is.EqualTo(TestMetadata.Default.Name));
			Assert.That(described.Inboxes.Select(inbox => inbox.Id), Is.EqualTo(new[] { "work", "home" }));
			Assert.That(described.Inboxes[0].Address, Is.EqualTo("work@example.com"));
			Assert.That(inboxes.Inboxes, Is.EqualTo(described.Inboxes));
		});
	}

	[Test]
	public async Task The_inbox_list_is_cut_to_the_protocol_cap_and_long_names_are_clipped()
	{
		var provider = new TestEmailInboxIntegration("IMAP")
		{
			Inboxes =
			[
				.. Enumerable.Range(0, ProtocolLimits.MaxEmailInboxes + 5)
					.Select(index => new EmailInbox { Id = $"inbox-{index}", DisplayName = new string('n', 1000) })
			]
		};
		using var handler = Handler(provider);

		var inboxes = await InvokeAsync<EmailInboxesResult>(handler, CapabilityOperations.EmailInbox.Inboxes);

		Assert.Multiple(() =>
		{
			Assert.That(inboxes.Inboxes, Has.Count.EqualTo(ProtocolLimits.MaxEmailInboxes));
			Assert.That(inboxes.Inboxes[0].DisplayName, Has.Length.EqualTo(ProtocolLimits.MaxEmailSenderLength));
		});
	}

	[Test]
	public async Task A_snapshot_passes_the_query_on_and_answers_status_counts_and_the_newest_messages_first()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work")
		{
			Snapshots =
			{
				["work"] = new EmailInboxSnapshot
				{
					Status = EmailInboxStatus.Error,
					StatusMessage = "Sign-in failed.",
					UnreadCount = 2,
					TotalCount = 40,
					SyncedAt = _now,
					Messages =
					[
						Message("older", _now.AddHours(-2)),
						Message("newest", _now.AddMinutes(-1)) with
						{
							SenderName = "Bob",
							SenderAddress = "bob@example.com",
							Subject = new string('s', ProtocolLimits.MaxEmailSubjectLength + 10),
							Preview = new string('p', ProtocolLimits.MaxEmailPreviewLength + 10),
							IsRead = true
						}
					]
				}
			}
		};
		using var handler = Handler(provider);

		var result = await InvokeAsync<EmailInboxSnapshotResult>(handler,
			CapabilityOperations.EmailInbox.Snapshot,
			new EmailInboxSnapshotArguments { InboxId = "work", MaxMessages = 500, UnreadOnly = true });

		var newest = result.Messages[0];
		Assert.Multiple(() =>
		{
			Assert.That(provider.LastQuery!.MaxMessages, Is.EqualTo(ProtocolLimits.MaxEmailMessages));
			Assert.That(provider.LastQuery.UnreadOnly, Is.True);
			Assert.That(result.Status, Is.EqualTo("Error"));
			Assert.That(result.StatusMessage, Is.EqualTo("Sign-in failed."));
			Assert.That(result.UnreadCount, Is.EqualTo(2));
			Assert.That(result.TotalCount, Is.EqualTo(40));
			Assert.That(result.SyncedAt, Is.EqualTo(_now));
			Assert.That(result.Truncated, Is.False);
			Assert.That(result.Messages.Select(message => message.Id), Is.EqualTo(new[] { "newest", "older" }));
			Assert.That(newest.SenderName, Is.EqualTo("Bob"));
			Assert.That(newest.SenderAddress, Is.EqualTo("bob@example.com"));
			Assert.That(newest.Subject, Has.Length.EqualTo(ProtocolLimits.MaxEmailSubjectLength));
			Assert.That(newest.Preview, Has.Length.EqualTo(ProtocolLimits.MaxEmailPreviewLength));
			Assert.That(newest.IsRead, Is.True);
		});
	}

	[Test]
	public async Task A_snapshot_never_carries_more_messages_than_were_asked_for()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work")
		{
			Snapshots =
			{
				["work"] = Snapshot([
					.. Enumerable.Range(0, 8).Select(index => Message($"m{index}", _now.AddMinutes(-index)))
				])
			}
		};
		using var handler = Handler(provider);

		var result = await InvokeAsync<EmailInboxSnapshotResult>(handler,
			CapabilityOperations.EmailInbox.Snapshot,
			new EmailInboxSnapshotArguments { InboxId = "work", MaxMessages = 3 });

		Assert.That(result.Messages.Select(message => message.Id), Is.EqualTo(new[] { "m0", "m1", "m2" }));
	}

	[Test]
	public async Task A_snapshot_over_the_byte_budget_keeps_the_newest_messages_and_says_it_was_truncated()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work")
		{
			Snapshots =
			{
				["work"] = Snapshot([
					.. Enumerable.Range(0, ProtocolLimits.MaxEmailMessages)
						.Select(index => Message($"m{index:D2}", _now.AddMinutes(-index)) with
						{
							SenderName = new string('漢', 1000),
							SenderAddress = new string('漢', 1000),
							Subject = new string('漢', 1000),
							Preview = new string('漢', 1000)
						})
				])
			}
		};
		using var handler = Handler(provider);

		var raw = await InvokeRawAsync(handler,
			CapabilityOperations.EmailInbox.Snapshot,
			new EmailInboxSnapshotArguments { InboxId = "work", MaxMessages = ProtocolLimits.MaxEmailMessages });
		var result = raw.Deserialize<EmailInboxSnapshotResult>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(Encoding.UTF8.GetByteCount(raw.GetRawText()),
				Is.LessThanOrEqualTo(ProtocolLimits.MaxEmailInboxReplyBytes));
			Assert.That(result.Truncated, Is.True);
			Assert.That(result.Messages, Is.Not.Empty);
			Assert.That(result.Messages.Select(message => message.Id),
				Is.EqualTo(Enumerable.Range(0, result.Messages.Count).Select(index => $"m{index:D2}")));
		});
	}

	[Test]
	public async Task
		An_unknown_inbox_is_unavailable_missing_arguments_are_invalid_and_an_unknown_operation_is_unsupported()
	{
		using var handler = Handler(new TestEmailInboxIntegration("IMAP", "work"));

		var unknownInbox = await handler.InvokeAsync(Invocation(CapabilityOperations.EmailInbox.Snapshot,
				new EmailInboxSnapshotArguments { InboxId = "mallory" }),
			CancellationToken.None);
		var noArguments = await handler.InvokeAsync(Invocation(CapabilityOperations.EmailInbox.Snapshot),
			CancellationToken.None);
		var unknownOperation = await handler.InvokeAsync(Invocation("mark-read"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknownInbox.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(noArguments.Error!.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	[Test]
	public void A_burst_of_changes_to_one_inbox_reaches_the_host_as_one_state_update_naming_that_inbox()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work", "home");
		var notifier = new RecordingCatalogNotifier();
		var time = new ManualTimeProvider(_now);
		using var handler = Handler(notifier, time, provider);

		provider.RaiseChanged("work");
		provider.RaiseChanged("work");
		time.Advance(TimeSpan.FromMilliseconds(100));
		provider.RaiseChanged("work");
		provider.RaiseChanged("home");

		Assert.That(notifier.Updates, Is.Empty, "a change is held back while more may follow");

		time.Advance(TimeSpan.FromMilliseconds(250));

		Assert.That(notifier.Updates,
			Is.EquivalentTo(new[] { (CapabilityKinds.EmailInbox, "work"), (CapabilityKinds.EmailInbox, "home") }));
	}

	[Test]
	public void A_change_to_the_inbox_list_reaches_the_host_as_a_state_update_without_a_local_id()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work");
		var notifier = new RecordingCatalogNotifier();
		var time = new ManualTimeProvider(_now);
		using var handler = Handler(notifier, time, provider);

		provider.RaiseChanged(null);
		provider.RaiseChanged(null);
		time.Advance(TimeSpan.FromMilliseconds(250));
		provider.RaiseChanged(null);
		time.Advance(TimeSpan.FromMilliseconds(250));

		Assert.That(notifier.Updates,
			Is.EqualTo(new (string, string?)[]
				{ (CapabilityKinds.EmailInbox, null), (CapabilityKinds.EmailInbox, null) }));
	}

	[Test]
	public void Disposing_the_handler_unsubscribes_and_drops_changes_still_waiting()
	{
		var provider = new TestEmailInboxIntegration("IMAP", "work");
		var notifier = new RecordingCatalogNotifier();
		var time = new ManualTimeProvider(_now);
		var handler = Handler(notifier, time, provider);

		provider.RaiseChanged("work");
		handler.Dispose();
		time.Advance(TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(provider.HasChangedSubscribers, Is.False);
			Assert.That(notifier.Updates, Is.Empty);
		});
	}

	private static EmailInboxCapabilityHandler Handler(params TestEmailInboxIntegration[] providers)
	{
		return Handler(new RecordingCatalogNotifier(), new ManualTimeProvider(_now), providers);
	}

	private static EmailInboxCapabilityHandler Handler(
		IPluginCatalogNotifier notifier,
		TimeProvider time,
		params TestEmailInboxIntegration[] providers)
	{
		return new EmailInboxCapabilityHandler(providers, TestMetadata.Default, notifier, time);
	}

	private static EmailMessage Message(string id, DateTimeOffset receivedAt)
	{
		return new EmailMessage { Id = id, Subject = id, ReceivedAt = receivedAt };
	}

	private static EmailInboxSnapshot Snapshot(IReadOnlyList<EmailMessage> messages)
	{
		return new EmailInboxSnapshot
		{
			Status = EmailInboxStatus.Connected,
			UnreadCount = 0,
			TotalCount = messages.Count,
			SyncedAt = _now,
			Messages = messages
		};
	}

	private static CapabilityInvocation Invocation(string operation, object? arguments = null)
	{
		return new CapabilityInvocation
		{
			Kind = CapabilityKinds.EmailInbox,
			LocalId = ProviderCapabilityId.LocalId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};
	}

	private static async Task<JsonElement> InvokeRawAsync(
		EmailInboxCapabilityHandler handler,
		string operation,
		object? arguments = null)
	{
		var result = await handler.InvokeAsync(Invocation(operation, arguments), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False, result.Error?.Message);
		return result.Data!.Value;
	}

	private static async Task<T> InvokeAsync<T>(
		EmailInboxCapabilityHandler handler,
		string operation,
		object? arguments = null)
	{
		return (await InvokeRawAsync(handler, operation, arguments)).Deserialize<T>(PluginProtocolJson.Options)!;
	}

	private sealed class RecordingCatalogNotifier : IPluginCatalogNotifier
	{
		private readonly Lock _gate = new();
		private readonly List<(string Kind, string? LocalId)> _updates = [];

		public IReadOnlyList<(string Kind, string? LocalId)> Updates
		{
			get
			{
				lock (_gate)
				{
					return [.. _updates];
				}
			}
		}

		public void CatalogChanged(string kind, string? localId = null, string? reason = null)
		{
			lock (_gate)
			{
				_updates.Add((kind, localId));
			}
		}
	}
}
