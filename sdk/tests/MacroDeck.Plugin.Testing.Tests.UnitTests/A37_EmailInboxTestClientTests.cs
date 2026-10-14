using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Email;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A37EmailInboxTestClientTests
{
	private static readonly DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2));

	[Test]
	public async Task The_harness_and_the_wire_answer_every_email_inbox_read_alike()
	{
		await using var harness = PluginTestHarness.Create(builder =>
			builder.RegisterIntegration(_ => new EmailIntegration()));

		await using var host = await MacroDeckTestHost.StartAsync();
		var hostBuilder = MacroDeckPlugin.CreatePlugin().RegisterIntegration(_ => new EmailIntegration());
		await using var plugin = await host.HostAsync(hostBuilder);
		var session = await host.WaitForSessionAsync();

		var snapshot = new EmailInboxSnapshotArguments { InboxId = "work", MaxMessages = 1, UnreadOnly = true };
		var harnessSnapshot = await harness.EmailInbox.GetSnapshotAsync(snapshot);
		var unknownInbox = await harness.EmailInbox.GetSnapshotAsync(new EmailInboxSnapshotArguments
		{
			InboxId = "mallory"
		});

		Assert.Multiple(() =>
		{
			Assert.That(harnessSnapshot.DataAs<EmailInboxSnapshotResult>()!.Messages.Single().Subject,
				Is.EqualTo("Lunch?"));
			Assert.That(unknownInbox.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		});

		await AssertSameOutcome(harness.EmailInbox.DescribeAsync(), session.EmailInbox.DescribeAsync());
		await AssertSameOutcome(harness.EmailInbox.GetInboxesAsync(), session.EmailInbox.GetInboxesAsync());
		await AssertSameOutcome(Task.FromResult(harnessSnapshot), session.EmailInbox.GetSnapshotAsync(snapshot));
	}

	[Test]
	public async Task Raising_changed_in_a_plugin_sends_the_host_a_state_update_naming_the_inbox()
	{
		var integration = new EmailIntegration();
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => integration));
		await host.WaitForSessionAsync();

		integration.RaiseChanged("work");
		var update = await NextStateUpdateAsync(host);

		Assert.Multiple(() =>
		{
			Assert.That(update.Kind, Is.EqualTo(CapabilityKinds.EmailInbox));
			Assert.That(update.LocalId, Is.EqualTo("work"));
		});
	}

	private static async Task<StateUpdatePayload> NextStateUpdateAsync(MacroDeckTestHost host)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (true)
		{
			var update = host.Messages.OfType(MessageTypes.StateUpdate)
				.Select(message
					=> message.Envelope.Payload!.Value.Deserialize<StateUpdatePayload>(PluginProtocolJson.Options)!)
				.FirstOrDefault(payload => payload.Kind == CapabilityKinds.EmailInbox);
			if (update is not null)
			{
				return update;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
		}
	}

	private static async Task AssertSameOutcome(
		Task<CapabilityInvocationOutcome> viaHarness,
		Task<CapabilityInvocationOutcome> viaHost)
	{
		var harnessOutcome = await viaHarness;
		var hostOutcome = await viaHost;

		Assert.Multiple(() =>
		{
			Assert.That(harnessOutcome.Succeeded, Is.True, harnessOutcome.Error?.Message);
			Assert.That(hostOutcome.Succeeded, Is.True, hostOutcome.Error?.Message);
			Assert.That(JsonNode.DeepEquals(JsonNode.Parse(harnessOutcome.Data!.Value.GetRawText()),
					JsonNode.Parse(hostOutcome.Data!.Value.GetRawText())),
				Is.True);
		});
	}

	private sealed class EmailIntegration : IPluginIntegration, IEmailInboxProvider
	{
		private static readonly EmailMessage[] _messages =
		[
			new() { Id = "2", SenderName = "Bob", Subject = "Lunch?", ReceivedAt = _now },
			new() { Id = "1", Subject = "Welcome", ReceivedAt = _now.AddDays(-1), IsRead = true }
		];

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public event EventHandler<EmailInboxChangedEventArgs>? Changed;

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public IReadOnlyList<EmailInbox> GetInboxes()
		{
			return
			[
				new EmailInbox
					{ Id = "work", DisplayName = "Work", Address = "alice@example.com" }
			];
		}

		public Task<EmailInboxSnapshot> GetSnapshotAsync(
			string inboxId,
			EmailMessageQuery query,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new EmailInboxSnapshot
			{
				Status = EmailInboxStatus.Connected,
				UnreadCount = 1,
				TotalCount = 2,
				SyncedAt = _now,
				Messages =
				[
					.. _messages.Where(message => !query.UnreadOnly || !message.IsRead).Take(query.MaxMessages)
				]
			});
		}

		public void RaiseChanged(string? inboxId)
		{
			Changed?.Invoke(this, new EmailInboxChangedEventArgs(inboxId));
		}
	}
}
