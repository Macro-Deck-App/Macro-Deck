using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.System;

public class NullPowerServiceTests
{
	[Test]
	public void Is_not_supported()
	{
		Assert.That(new NullPowerService().IsSupported, Is.False);
	}

	[TestCase(PowerOperation.Lock)]
	[TestCase(PowerOperation.Sleep)]
	[TestCase(PowerOperation.Hibernate)]
	[TestCase(PowerOperation.Restart)]
	[TestCase(PowerOperation.ShutDown)]
	public void Supports_nothing(PowerOperation operation)
	{
		Assert.That(new NullPowerService().Supports(operation), Is.False);
	}

	[Test]
	public async Task ExecuteAsync_fails_with_a_reason()
	{
		var result = await new NullPowerService().ExecuteAsync(PowerOperation.Lock, force: false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(TestLocalization.Resolve(result.FailureReason), Is.Not.Null.And.Not.Empty);
		});
	}
}
