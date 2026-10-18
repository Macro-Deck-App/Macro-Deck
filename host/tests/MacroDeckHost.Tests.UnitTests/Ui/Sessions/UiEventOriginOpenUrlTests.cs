using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Links;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiEventOriginOpenUrlTests : UiSessionFixture
{
	private const string ClientId = "client-a";
	private const string Pressing = "conn-pressing";
	private const string OtherTab = "conn-other-tab";
	private const string Article = "https://example.com/article";

	private async Task<string> OpenSharedSessionAsync()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);
		Assert.That(Attach(sessionId, Pressing).Accepted, Is.True);
		return sessionId;
	}

	private void Send(string sessionId, string connectionId, string eventName = UiComponentEvents.Press)
	{
		var request = new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = eventName };
		var response = Broker.SendEvent(request, connectionId, ClientId);
		Assert.That(response.Accepted, Is.True, "The event was refused.");
	}

	private Task<bool> OpenUrlAsync(string sessionId, string url = Article)
	{
		return Origins.OpenUrlAsync(ProviderId, sessionId, ClientId, url, CancellationToken.None);
	}

	[Test]
	public async Task A_link_reaches_only_the_connection_that_pressed_even_when_another_tab_shares_its_client_id()
	{
		var sessionId = await OpenSharedSessionAsync();
		Assert.That(Attach(sessionId, OtherTab).Accepted, Is.True);
		ClientFeatures.Declare(Pressing, [UiClientFeatures.OpenUrl]);
		ClientFeatures.Declare(OtherTab, [UiClientFeatures.OpenUrl]);
		Send(sessionId, OtherTab, UiComponentEvents.Reveal);
		Send(sessionId, Pressing);

		var delivered = await OpenUrlAsync(sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.True);
			Assert.That(MessagesFor<UiOpenUrlEvent>(Pressing).Single().Url, Is.EqualTo(Article));
			Assert.That(MessagesFor<UiOpenUrlEvent>(OtherTab), Is.Empty);
		});
	}

	[Test]
	public async Task A_client_that_did_not_declare_it_can_open_links_is_not_sent_one()
	{
		var sessionId = await OpenSharedSessionAsync();
		ClientFeatures.Declare(Pressing, ["something-else"]);
		Send(sessionId, Pressing);

		var delivered = await OpenUrlAsync(sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.False);
			Assert.That(MessagesFor<UiOpenUrlEvent>(Pressing), Is.Empty);
		});
	}

	[Test]
	public async Task A_link_is_only_opened_shortly_after_the_press()
	{
		var sessionId = await OpenSharedSessionAsync();
		ClientFeatures.Declare(Pressing, [UiClientFeatures.OpenUrl]);
		Send(sessionId, Pressing);
		Time.Now += UiEventOrigins.OpenUrlWindow + TimeSpan.FromSeconds(1);

		var delivered = await OpenUrlAsync(sessionId);

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.False);
			Assert.That(MessagesFor<UiOpenUrlEvent>(Pressing), Is.Empty);
		});
	}

	[Test]
	public async Task A_connection_that_detached_is_not_sent_a_link()
	{
		var sessionId = await OpenSharedSessionAsync();
		ClientFeatures.Declare(Pressing, [UiClientFeatures.OpenUrl]);
		Send(sessionId, Pressing);
		Broker.Detach(sessionId, Pressing);

		var delivered = await OpenUrlAsync(sessionId);

		Assert.That(delivered, Is.False);
	}

	[Test]
	public async Task A_link_that_is_not_a_web_address_is_never_sent()
	{
		var sessionId = await OpenSharedSessionAsync();
		ClientFeatures.Declare(Pressing, [UiClientFeatures.OpenUrl]);
		Send(sessionId, Pressing);

		var script = await OpenUrlAsync(sessionId, "javascript:alert(1)");
		var file = await OpenUrlAsync(sessionId, "file:///etc/passwd");

		Assert.Multiple(() =>
		{
			Assert.That(script, Is.False);
			Assert.That(file, Is.False);
			Assert.That(MessagesFor<UiOpenUrlEvent>(Pressing), Is.Empty);
		});
	}

	[Test]
	public async Task Another_provider_cannot_open_a_link_from_a_session_it_does_not_own()
	{
		var sessionId = await OpenSharedSessionAsync();
		ClientFeatures.Declare(Pressing, [UiClientFeatures.OpenUrl]);
		Send(sessionId, Pressing);

		var delivered = await Origins.OpenUrlAsync("com.example.other",
			sessionId,
			ClientId,
			Article,
			CancellationToken.None);

		Assert.That(delivered, Is.False);
	}
}
