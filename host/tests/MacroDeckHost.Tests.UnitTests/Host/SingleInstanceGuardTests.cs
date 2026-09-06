namespace MacroDeckHost.Tests.UnitTests.Host;

public class SingleInstanceGuardTests
{
	[Test]
	public void ParsePort_accepts_valid_port()
	{
		Assert.That(SingleInstanceGuard.ParsePort("52345\n"), Is.EqualTo(52345));
	}

	[Test]
	public void ParsePort_rejects_invalid_content()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SingleInstanceGuard.ParsePort(null), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort(""), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("not-a-port"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("0"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("-1"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("70000"), Is.Null);
		});
	}
}
