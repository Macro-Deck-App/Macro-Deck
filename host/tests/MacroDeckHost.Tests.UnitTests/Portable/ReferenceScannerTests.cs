using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class ReferenceScannerTests
{
	[Test]
	public void SecretReferences_ExtractsTopLevelAndEscapedFlowRefs()
	{
		var top = Guid.NewGuid();
		var nested = Guid.NewGuid();
		var data = $"{{\"$secret\":\"{top}\",\"flows\":\"[{{\\\"$secret\\\":\\\"{nested}\\\"}}]\"}}";

		var ids = SecretReferences.Extract(data);

		Assert.That(ids, Is.EquivalentTo(new[] { top, nested }));
	}

	[Test]
	public void SecretReferences_IgnoresPlainGuidsThatAreNotSecretRefs()
	{
		var icon = Guid.NewGuid();
		var data = $"{{\"iconId\":\"{icon}\"}}";

		Assert.That(SecretReferences.Extract(data), Is.Empty);
	}

	[Test]
	public void GuidReferences_ExtractsEveryGuid()
	{
		var icon = Guid.NewGuid();
		var secret = Guid.NewGuid();
		var data = $"{{\"iconId\":\"{icon}\",\"$secret\":\"{secret}\"}}";

		Assert.That(GuidReferences.ExtractAll(data), Is.EquivalentTo(new[] { icon, secret }));
	}

	[Test]
	public void Scanners_OnEmptyData_ReturnEmpty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SecretReferences.Extract(null), Is.Empty);
			Assert.That(GuidReferences.ExtractAll(""), Is.Empty);
		});
	}
}
