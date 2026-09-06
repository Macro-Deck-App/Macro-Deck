using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionAccessTests : UiSessionFixture
{
	private static readonly string[] _threeClients = ["c1", "c2", "c3"];

	[Test]
	public async Task Attaching_a_second_client_to_an_exclusive_session_is_rejected_without_disturbing_the_owner()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);

		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The owner never received its tree.");

		var refused = Attach(sessionId, "c2");

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The owner stopped receiving patches after a second client was refused.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.SessionBusy));
			Assert.That(refused.Revision, Is.Zero);
			Assert.That(MessagesFor("c2"), Is.Empty, "A refused client was still fanned out to.");
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"), Has.Count.EqualTo(1));
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task A_shared_session_delivers_the_same_patch_bytes_to_every_attached_client()
	{
		// Driven through a real provider rather than the sink, so "produced once" is a claim about the
		// provider and not about how many times the test happened to call the host.
		var session = AddInProcessProvider(() => TreeAt(1, UiSessionModes.Shared));
		var sessionId = await OpenAsync(ProviderId, UiSessionModes.Shared);

		var responses = _threeClients.Select(id => Attach(sessionId, id)).ToArray();
		foreach (var connectionId in _threeClients)
		{
			await WaitForMessagesAsync(connectionId, 1, $"{connectionId} never received its tree.");
		}

		var produced = new UiPatch
		{
			FromRevision = 1,
			ToRevision = 2,
			Operations = [new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "root" }]
		};

		session.Emit(produced);

		foreach (var connectionId in _threeClients)
		{
			await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>(connectionId).Count == 1,
				$"{connectionId} never received the shared patch.");
		}

		await SettleAsync();

		var expected = UiCanonicalJson.SerializeToUtf8Bytes(produced);
		var delivered = _threeClients
			.Select(id => Transport.For(id).Single(recorded => recorded.Message is UiSessionPatchedEvent).Payload!)
			.ToArray();

		Assert.Multiple(() =>
		{
			foreach (var response in responses)
			{
				Assert.That(response.Accepted, Is.True);
				Assert.That(response.SessionMode, Is.EqualTo(UiSessionModes.Shared));
				Assert.That(response.Revision, Is.EqualTo(1));
			}

			foreach (var payload in delivered)
			{
				Assert.That(payload, Is.EqualTo(expected), "A client received re-encoded patch bytes.");
			}

			Assert.That(session.PatchesDrained,
				Is.EqualTo(1),
				"The provider was asked to produce the patch once per attached client.");
			Assert.That(Transport.All.Count(recorded => recorded.Message is UiSessionPatchedEvent),
				Is.EqualTo(1),
				"The patch was relayed once per client instead of once per session.");
		});
	}

	[Test]
	public async Task Attaching_past_the_shared_attachment_limit_is_rejected_as_full_not_as_busy()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);

		for (var index = 0; index < ProtocolLimits.MaxUiAttachmentsPerSession; index++)
		{
			var accepted = Attach(sessionId, $"c{index}");
			Assert.That(accepted.Accepted, Is.True, $"Attachment {index} was refused below the limit.");
		}

		var refused = Attach(sessionId, "one-too-many");
		await WaitForMessagesAsync("c0", 1, "The first client never received its tree.");

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c0").Count == 1,
			"An existing client stopped receiving patches once the session filled up.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.SessionFull));
			Assert.That(MessagesFor("one-too-many"), Is.Empty);
		});
	}

	[TestCase("exclusive-with-observers")]
	[TestCase("sHaReD")]
	[TestCase("")]
	public async Task An_unrecognised_session_mode_is_treated_as_exclusive(string declaredMode)
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, declaredMode);

		var owner = Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The owner never received its tree.");

		var refused = Attach(sessionId, "c2");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			// The response reports the mode the session will behave as, not the one that was declared.
			Assert.That(owner.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.SessionBusy));
			Assert.That(MessagesFor("c2"), Is.Empty);
		});
	}

	[Test]
	public async Task A_session_rejects_an_attach_from_a_different_principal_than_the_one_that_opened_it()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, principal: DeviceA);

		var foreign = Attach(sessionId, "c-other", DeviceB);

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await SettleAsync();

		var owner = Attach(sessionId, "c1", DeviceA);
		await WaitForMessagesAsync("c1", 1, "The owning principal was locked out of its own session.");

		Assert.Multiple(() =>
		{
			Assert.That(foreign.Accepted, Is.False);
			Assert.That(foreign.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(MessagesFor("c-other"), Is.Empty, "A foreign principal was fanned out to.");
			Assert.That(owner.Accepted, Is.True);
		});
	}

	/// <summary>
	/// The broker has no notion of a scope, so this proves only that the principal it is handed has to
	/// match - the half of the rule that lives here. Whether a privileged scope can arrive with a
	/// principal it did not earn is decided in the hub; see <see cref="UiHubSessionMethodTests" />.
	/// </summary>
	[Test]
	public async Task An_attach_carrying_no_principal_is_refused_like_any_other_foreign_one()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, principal: DeviceA);

		// An admin connection with no device claim reaches the broker with an empty principal.
		var admin = Attach(sessionId, "c-admin", string.Empty);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(admin.Accepted, Is.False);
			Assert.That(admin.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(MessagesFor("c-admin"), Is.Empty);
		});
	}

	[Test]
	public async Task A_session_bound_to_a_principal_resumes_within_the_grace_window_only_for_that_principal()
	{
		var provider = AddProvider(treeRevision: 2);
		var sessionId = await OpenAsync(provider, principal: DeviceA);

		Attach(sessionId, "c1", DeviceA);
		await WaitForMessagesAsync("c1", 1, "The owner never received its tree.");

		Broker.Detach(sessionId, "c1");

		Time.Advance(TimeSpan.FromSeconds(5));
		var foreign = Attach(sessionId, "c-other", DeviceB);

		Time.Advance(TimeSpan.FromSeconds(1));
		var resumed = Attach(sessionId, "c1", DeviceA);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(foreign.Accepted, Is.False);
			Assert.That(foreign.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(resumed.Accepted, Is.True, "A refused foreign attach consumed the owner's grace window.");
			Assert.That(resumed.Revision, Is.EqualTo(2));
			Assert.That(provider.OpenCalls, Is.EqualTo(1));
			Assert.That(provider.CloseCalls, Is.Zero);
		});
	}
}
