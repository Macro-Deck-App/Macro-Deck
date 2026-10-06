using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeQuotaBudgetTests
{
	private static readonly DateTimeOffset _summerNoonLosAngeles = new(2026, 7, 1, 19, 0, 0, TimeSpan.Zero);

	[TestCase(0, YouTubeQuotaLevel.Normal)]
	[TestCase(79, YouTubeQuotaLevel.Normal)]
	[TestCase(80, YouTubeQuotaLevel.Throttled)]
	[TestCase(94, YouTubeQuotaLevel.Throttled)]
	[TestCase(95, YouTubeQuotaLevel.Paused)]
	[TestCase(130, YouTubeQuotaLevel.Paused)]
	public void The_level_follows_the_share_of_the_daily_limit_spent(int spent, YouTubeQuotaLevel expected)
	{
		var budget = new YouTubeQuotaBudget(100, new YouTubeManualClock(_summerNoonLosAngeles));

		budget.Charge(spent);

		Assert.Multiple(() =>
		{
			Assert.That(budget.Level, Is.EqualTo(expected));
			Assert.That(budget.Spent, Is.EqualTo(spent));
			Assert.That(budget.Remaining, Is.EqualTo(Math.Max(0, 100 - spent)));
		});
	}

	[Test]
	public void A_quota_exceeded_answer_pauses_until_the_next_pacific_midnight()
	{
		var clock = new YouTubeManualClock(_summerNoonLosAngeles);
		var budget = new YouTubeQuotaBudget(10_000, clock);
		budget.Charge(10);

		budget.MarkExhausted();

		Assert.Multiple(() =>
		{
			Assert.That(budget.Level, Is.EqualTo(YouTubeQuotaLevel.Paused));
			Assert.That(budget.Remaining, Is.Zero);
			Assert.That(budget.ResetsAt, Is.EqualTo(new DateTimeOffset(2026, 7, 2, 7, 0, 0, TimeSpan.Zero)));
		});

		clock.Now = new DateTimeOffset(2026, 7, 2, 6, 59, 59, TimeSpan.Zero);
		Assert.That(budget.Level, Is.EqualTo(YouTubeQuotaLevel.Paused), "still the same Pacific day");

		clock.Now = new DateTimeOffset(2026, 7, 2, 7, 0, 0, TimeSpan.Zero);
		Assert.Multiple(() =>
		{
			Assert.That(budget.Level, Is.EqualTo(YouTubeQuotaLevel.Normal));
			Assert.That(budget.IsExhausted, Is.False);
			Assert.That(budget.Spent, Is.Zero);
		});
	}

	[Test]
	public void In_winter_the_day_turns_at_eight_utc()
	{
		var clock = new YouTubeManualClock(new DateTimeOffset(2026, 1, 15, 20, 0, 0, TimeSpan.Zero));
		var budget = new YouTubeQuotaBudget(100, clock);
		budget.Charge(90);

		Assert.That(budget.ResetsAt, Is.EqualTo(new DateTimeOffset(2026, 1, 16, 8, 0, 0, TimeSpan.Zero)));

		clock.Now = new DateTimeOffset(2026, 1, 16, 7, 59, 0, TimeSpan.Zero);
		Assert.That(budget.Spent, Is.EqualTo(90));

		clock.Now = new DateTimeOffset(2026, 1, 16, 8, 0, 0, TimeSpan.Zero);
		Assert.That(budget.Spent, Is.Zero);
	}

	[Test]
	public void On_the_spring_forward_day_the_reset_is_still_local_midnight()
	{
		var clock = new YouTubeManualClock(new DateTimeOffset(2026, 3, 8, 20, 0, 0, TimeSpan.Zero));
		var budget = new YouTubeQuotaBudget(100, clock);
		budget.Charge(50);

		Assert.That(budget.ResetsAt, Is.EqualTo(new DateTimeOffset(2026, 3, 9, 7, 0, 0, TimeSpan.Zero)));

		clock.Now = new DateTimeOffset(2026, 3, 9, 7, 0, 0, TimeSpan.Zero);
		Assert.That(budget.Spent, Is.Zero);
	}

	[Test]
	public void A_client_id_shares_one_budget_at_the_limit_set_last()
	{
		var budgets = new YouTubeQuotaBudgets(new YouTubeManualClock(_summerNoonLosAngeles));

		var first = budgets.For("client-a", 10_000);
		var second = budgets.For("client-a", 50_000);
		var third = budgets.For("client-a", 20_000);
		var other = budgets.For("client-b", 10_000);

		first.Charge(5);

		Assert.Multiple(() =>
		{
			Assert.That(second, Is.SameAs(first));
			Assert.That(third, Is.SameAs(first));
			Assert.That(first.Limit, Is.EqualTo(20_000));
			Assert.That(third.Spent, Is.EqualTo(5));
			Assert.That(other, Is.Not.SameAs(first));
			Assert.That(other.Spent, Is.Zero);
			Assert.That(other.Limit, Is.EqualTo(10_000));
		});
	}
}
