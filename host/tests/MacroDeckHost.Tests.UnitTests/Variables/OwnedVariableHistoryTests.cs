using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Variables;
using MacroDeckHost.Tests.UnitTests.Delegation;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class OwnedVariableHistoryTests
{
	private const string Owner = "app.macro-deck.twitch";
	private const string Name = "twitch_streamer_viewer_count";

	private FakeTimeProvider _time = null!;
	private VariableRegistry _variables = null!;
	private OwnedVariableHistory _history = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_variables = new VariableRegistry();
		_history = new OwnedVariableHistory(new VariableHistory(_variables, _time), _variables);
	}

	[Test]
	public async Task The_window_on_an_own_variable_follows_its_samples()
	{
		Set(Owner, "10");
		await using var window = _history.Open(Owner, Name, 10);
		var changes = 0;
		window.Changed += (_, _) => changes++;

		Set(Owner, "12");
		_time.Advance(VariableHistory.SampleInterval);

		Assert.Multiple(() =>
		{
			Assert.That(window.Values, Is.EqualTo(new double?[] { 10, 12 }));
			Assert.That(changes, Is.EqualTo(1));
		});
	}

	[Test]
	public void Another_owners_variable_cannot_be_opened()
	{
		Set("app.macro-deck.youtube", "10");

		var exception = Assert.Throws<VariableHistoryException>(() => _history.Open(Owner, Name, 10));

		Assert.That(exception!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.NotOwned));
	}

	[Test]
	public async Task A_window_opened_before_its_variable_existed_never_shows_one_someone_else_created()
	{
		await using var window = _history.Open(Owner, Name, 10);

		Set("app.macro-deck.youtube", "10");
		_time.Advance(VariableHistory.SampleInterval);

		Assert.That(window.Values, Is.Empty);
	}

	[Test]
	public async Task An_owner_holds_a_bounded_number_of_windows_and_closing_one_frees_a_place()
	{
		var windows = new List<MacroDeck.Sdk.Variables.IVariableHistoryWindow>();
		for (var index = 0; index < OwnedVariableHistory.MaxWindowsPerOwner; index++)
		{
			windows.Add(_history.Open(Owner, Name, 10));
		}

		var refused = Assert.Throws<VariableHistoryException>(() => _history.Open(Owner, Name, 10));
		await windows[0].DisposeAsync();
		await using var reopened = _history.Open(Owner, Name, 10);
		await using var otherOwner = _history.Open("app.macro-deck.youtube", "youtube_mine_viewer_count", 10);

		Assert.That(refused!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.LimitExceeded));

		foreach (var window in windows.Skip(1))
		{
			await window.DisposeAsync();
		}
	}

	[Test]
	public void An_integration_without_history_reports_it_as_unsupported()
	{
		var api = new IntegrationVariableApi(Owner, null!);

		var exception = Assert.ThrowsAsync<VariableHistoryException>(() => api.OpenHistoryAsync(Name, 10));

		Assert.That(exception!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.Unsupported));
	}

	[Test]
	public async Task An_integration_opens_the_history_of_its_own_variable_in_process()
	{
		Set(Owner, "7");
		var api = new IntegrationVariableApi(Owner, null!, history: _history);

		await using var window = await api.OpenHistoryAsync(Name, 10);

		Assert.That(window.Values, Is.EqualTo(new double?[] { 7 }));
	}

	private void Set(string owner, string value)
	{
		var existing = _variables.GetAll().FirstOrDefault(variable => variable.Name == Name);

		if (existing is not null)
		{
			existing.Value = value;
			_variables.Upsert(existing);
			return;
		}

		_variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = Name,
			Scope = VariableScope.Global,
			Type = DomainVariableType.Numeric,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = owner,
			Value = value,
			UpdatedAt = DateTime.UtcNow
		});
	}
}
