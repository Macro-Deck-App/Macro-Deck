using MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class EmailInboxContractTests : CapabilityContractFixture
{
	private static readonly DateTimeOffset _noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

	private static DeclaredCapability Provider()
	{
		return new DeclaredCapability
		{
			Kind = CapabilityKinds.EmailInbox,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};
	}

	private async Task<IEmailInboxProvider> ConnectAsync(TestEmailInboxIntegration mail)
	{
		return (IEmailInboxProvider)await ConnectAsync([new EmailInboxCapabilityHandler([mail], TestMetadata.Default)],
			[Provider()],
			[CapabilityKinds.EmailInbox]);
	}

	[Test]
	public async Task A_plugin_inbox_provider_surfaces_its_inboxes_through_the_host_integration()
	{
		var provider = await ConnectAsync(new TestEmailInboxIntegration("Example Mail", "work", "home"));

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate((IIntegration)provider), Is.Empty);
			Assert.That(provider.ProviderName, Is.EqualTo("Example Mail"));
			Assert.That(provider.GetInboxes().Select(inbox => (inbox.Id, inbox.DisplayName, inbox.Address)),
				Is.EqualTo(new[]
				{
					("work", "Inbox work", "work@example.com"), ("home", "Inbox home", "home@example.com")
				}));
			Assert.That(ProvidedCapabilityCatalog.For((IIntegration)provider).Select(capability => capability.Kind),
				Does.Contain(CapabilityKinds.EmailInbox));
		});
	}

	[Test]
	public async Task A_snapshot_round_trips_to_the_sdk_types()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work")
		{
			Snapshots =
			{
				["work"] = new EmailInboxSnapshot
				{
					Status = EmailInboxStatus.Error,
					StatusMessage = "Sign-in failed.",
					UnreadCount = 1,
					TotalCount = 7,
					SyncedAt = _noon,
					Messages =
					[
						new EmailMessage
						{
							Id = "2", SenderName = "Alice", SenderAddress = "alice@example.com", Subject = "Hello",
							Preview = "Hi there", ReceivedAt = _noon.AddMinutes(-1)
						},
						new EmailMessage
							{ Id = "1", Subject = "Older", ReceivedAt = _noon.AddMinutes(-5), IsRead = true }
					]
				}
			}
		};
		var provider = await ConnectAsync(mail);

		var all = await provider.GetSnapshotAsync("work",
			new EmailMessageQuery { MaxMessages = 5 },
			CancellationToken.None);
		var unread = await provider.GetSnapshotAsync("work",
			new EmailMessageQuery { MaxMessages = 5, UnreadOnly = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(all.Status, Is.EqualTo(EmailInboxStatus.Error));
			Assert.That(all.StatusMessage, Is.EqualTo("Sign-in failed."));
			Assert.That((all.UnreadCount, all.TotalCount), Is.EqualTo((1, 7)));
			Assert.That(all.SyncedAt, Is.EqualTo(_noon));
			Assert.That(all.Messages, Is.EqualTo(mail.Snapshots["work"].Messages));
			Assert.That(unread.Messages.Select(message => message.Id), Is.EqualTo(new[] { "2" }));
		});
	}

	[Test]
	public async Task A_failed_read_reaches_the_host_as_a_failure_never_as_an_empty_inbox()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work")
		{
			ReadFailure = new HttpRequestException("password=hunter2")
		};
		var provider = await ConnectAsync(mail);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await provider.GetSnapshotAsync("work", new EmailMessageQuery(), CancellationToken.None));

		Assert.That(exception!.Message, Does.Not.Contain("hunter2"));
	}

	[Test]
	public async Task A_plugin_inbox_reaches_the_host_registry_and_cache()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work")
		{
			Snapshots =
			{
				["work"] = new EmailInboxSnapshot
				{
					Status = EmailInboxStatus.Connected,
					UnreadCount = 1,
					TotalCount = 1,
					SyncedAt = _noon,
					Messages = [new EmailMessage { Id = "1", Subject = "Hello", ReceivedAt = _noon }]
				}
			}
		};
		await ConnectAsync(mail);

		using var signal = new EmailSyncSignal();
		using var registry = new EmailInboxRegistry(IntegrationRegistry, signal, Serilog.Core.Logger.None);
		using var cache = new EmailInboxCache(registry, new InMemoryKeys(), signal, Time, Serilog.Core.Logger.None);
		await cache.SyncAsync(EmailSyncRequest.Everything, CancellationToken.None);

		var inbox = cache.Snapshot.Inboxes.Single();
		Assert.Multiple(() =>
		{
			Assert.That(inbox.Ref, Is.EqualTo($"{PluginId}::work"));
			Assert.That(inbox.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That(inbox.UnreadCount, Is.EqualTo(1));
			Assert.That(inbox.LatestUnread.Single().Subject, Is.EqualTo("Hello"));
		});
	}

	[Test]
	public async Task A_disconnected_plugin_leaves_its_inbox_disconnected_after_the_retry()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work")
		{
			Snapshots =
			{
				["work"] = new EmailInboxSnapshot
				{
					Status = EmailInboxStatus.Connected, UnreadCount = 0, TotalCount = 0, SyncedAt = _noon
				}
			}
		};
		await ConnectAsync(mail);
		using var signal = new EmailSyncSignal();
		using var registry = new EmailInboxRegistry(IntegrationRegistry, signal, Serilog.Core.Logger.None);
		using var cache = new EmailInboxCache(registry, new InMemoryKeys(), signal, Time, Serilog.Core.Logger.None);
		await cache.SyncAsync(EmailSyncRequest.Everything, CancellationToken.None);

		Disconnect();
		await cache.SyncAsync(EmailSyncRequest.Everything, CancellationToken.None);
		var stale = cache.Snapshot.Inboxes.Single();
		Time.Advance(EmailInboxCache.RetryDelay);
		await cache.SyncAsync(EmailSyncRequest.Nothing, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(stale.IsStale, Is.True);
			Assert.That(stale.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That(cache.Snapshot.Inboxes.Single().Status, Is.EqualTo(EmailInboxStatus.Disconnected));
		});
	}

	[Test]
	public async Task Notifying_a_changed_inbox_list_makes_the_host_report_the_new_inboxes()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work");
		await ConnectAsync(mail);

		mail.InboxIds.Add("home");
		await SendStateUpdateFromPluginAsync(CapabilityKinds.EmailInbox);

		await WaitForAsync(() => CurrentProvider().GetInboxes().Count == 2,
			"The host's snapshot was never refreshed after state.update.");

		IEmailInboxProvider CurrentProvider()
		{
			return (IEmailInboxProvider)IntegrationRegistry.Integrations
				.Single(candidate => string.Equals(candidate.Id, PluginId, StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task An_unchanged_inbox_list_does_not_re_register_the_plugin()
	{
		var mail = new TestEmailInboxIntegration("Example Mail", "work");
		await ConnectAsync(mail);
		var before = Current();

		await Registrar.ApplyStateUpdateAsync(PluginId, CapabilityKinds.EmailInbox);
		var unchanged = Current();

		mail.InboxIds.Add("home");
		await Registrar.ApplyStateUpdateAsync(PluginId, CapabilityKinds.EmailInbox);
		var changed = Current();

		Assert.Multiple(() =>
		{
			Assert.That(unchanged, Is.SameAs(before), "an unchanged list must not replace the integration");
			Assert.That(changed, Is.Not.SameAs(before), "a changed list is applied");
			Assert.That(((IEmailInboxProvider)changed).GetInboxes(), Has.Count.EqualTo(2));
		});

		IIntegration Current()
		{
			return IntegrationRegistry.Integrations.Single(candidate
				=> string.Equals(candidate.Id, PluginId, StringComparison.Ordinal));
		}
	}

	private sealed class InMemoryKeys : IEmailInboxKeyStore
	{
		private EmailInboxKeySet _keys = EmailInboxKeySet.Empty;

		public IReadOnlyList<EmailInboxKey> Keys => _keys.Keys;

		public IReadOnlyDictionary<string, string> Assign(IReadOnlyList<EmailInboxDescriptor> visible)
		{
			_keys = EmailInboxKeys.Assign(_keys, visible);
			return _keys.Keys.ToDictionary(key => key.Ref, key => key.Slug, StringComparer.Ordinal);
		}
	}
}
