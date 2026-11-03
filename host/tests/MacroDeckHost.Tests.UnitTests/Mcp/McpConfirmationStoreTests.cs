using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpConfirmationStoreTests
{
	private static readonly McpActionRef _press = new("app.macro-deck.keyboard", "press-key");

	private ManualTimeProvider _time = null!;
	private McpConfirmationStore _store = null!;
	private int _changes;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_store = new McpConfirmationStore(_time);
		_changes = 0;
		_store.Changed += () => _changes++;
	}

	[Test]
	public async Task An_unanswered_in_app_request_is_withdrawn_when_it_expires()
	{
		var entry = InApp(Guid.NewGuid(), "call");
		var changesAfterCreate = _changes;

		_time.Advance(McpConfirmationStore.Lifetime);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_store.PendingInApp(), Is.Empty);
			Assert.That(entry.Completion.IsCanceled, Is.True);
			Assert.That(_changes, Is.GreaterThan(changesAfterCreate));
			Assert.That(_store.AnswerInApp(entry.Id, true, out _), Is.EqualTo(McpInAppAnswerResult.NotFound));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task One_allow_is_handed_to_exactly_one_caller()
	{
		var entry = InApp(Guid.NewGuid(), "call");
		_store.AnswerInApp(entry.Id, true, out _);

		var first = _store.TryConsumeInApp(entry.Id, out var firstAllowed);
		var second = _store.TryConsumeInApp(entry.Id, out _);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(first && firstAllowed, Is.True);
			Assert.That(second, Is.False);
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_same_call_of_the_same_client_shares_one_request_and_another_client_gets_its_own()
	{
		var token = Guid.NewGuid();
		var first = InApp(token, "call");

		var again = InApp(token, "call");
		var otherCall = InApp(token, "other call");
		var otherClient = InApp(Guid.NewGuid(), "call");

		await Assert.MultipleAsync(() =>
		{
			Assert.That(again.Id, Is.EqualTo(first.Id));
			Assert.That(otherCall.Id, Is.Not.EqualTo(first.Id));
			Assert.That(otherClient.Id, Is.Not.EqualTo(first.Id));
			Assert.That(_store.PendingInApp(), Has.Count.EqualTo(3));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task A_request_is_answered_once_and_only_where_it_was_asked()
	{
		var inApp = InApp(Guid.NewGuid(), "call");
		var page = _store.Create(Guid.NewGuid(), "client", "page call", ["line"]);

		var first = _store.AnswerInApp(inApp.Id, false, out _);
		var second = _store.AnswerInApp(inApp.Id, true, out _);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(first, Is.EqualTo(McpInAppAnswerResult.Answered));
			Assert.That(second, Is.EqualTo(McpInAppAnswerResult.AlreadyAnswered));
			Assert.That(_store.Find(inApp.Id), Is.Null);
			Assert.That(_store.AnswerInApp(page.Id, true, out _), Is.EqualTo(McpInAppAnswerResult.NotFound));
			Assert.That(_store.PendingInApp().Select(entry => entry.Id), Does.Not.Contain(page.Id));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Revoking_a_client_withdraws_every_request_it_left_open()
	{
		var token = Guid.NewGuid();
		var revoked = InApp(token, "call");
		var page = _store.Create(token, "client", "page call", ["line"]);
		var other = InApp(Guid.NewGuid(), "call");

		_store.AbandonForToken(token);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_store.PendingInApp().Select(entry => entry.Id), Is.EqualTo(new[] { other.Id }));
			Assert.That(revoked.Completion.IsCanceled, Is.True);
			Assert.That(_store.Find(page.Id), Is.Null);
			Assert.That(_store.AnswerInApp(revoked.Id, true, out _), Is.EqualTo(McpInAppAnswerResult.NotFound));
			return Task.CompletedTask;
		});
	}

	private McpConfirmationEntry InApp(Guid token, string fingerprint)
	{
		return _store.GetOrCreateInApp(token, "client", fingerprint, ["line"], [_press], true);
	}
}
