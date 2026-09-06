using System.Text.RegularExpressions;
using MacroDeckHost.Application.Secrets;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetSecretClonerTests
{
	private FakeSecretService _secrets = null!;
	private WidgetSecretCloner _cloner = null!;

	[SetUp]
	public void SetUp()
	{
		_secrets = new FakeSecretService();
		_cloner = new WidgetSecretCloner(_secrets);
	}

	[Test]
	public async Task NullOrEmptyData_IsReturnedUnchanged()
	{
		Assert.That(await _cloner.CloneReferencedSecrets(null), Is.Null);
		Assert.That(await _cloner.CloneReferencedSecrets(string.Empty), Is.EqualTo(string.Empty));
	}

	[Test]
	public async Task DataWithoutSecretReferences_IsReturnedUnchanged()
	{
		const string data = "{\"label\":\"hi\",\"flows\":\"[]\"}";

		Assert.That(await _cloner.CloneReferencedSecrets(data), Is.EqualTo(data));
	}

	[Test]
	public async Task SecretReference_IsRepointedToAFreshCloneWithTheSameValue()
	{
		var original = _secrets.Store("hunter2");
		var data = $"{{\"$secret\":\"{original}\"}}";

		var result = await _cloner.CloneReferencedSecrets(data);

		Assert.That(result, Does.Not.Contain(original.ToString()));
		var cloneId = SingleSecretId(result!);
		var cloneValue = await _secrets.Resolve(cloneId);
		var originalValue = await _secrets.Resolve(original);
		Assert.Multiple(() =>
		{
			Assert.That(cloneId, Is.Not.EqualTo(original));
			Assert.That(cloneValue, Is.EqualTo("hunter2"));
			Assert.That(originalValue, Is.EqualTo("hunter2"));
		});
	}

	[Test]
	public async Task SameSecretReferencedTwice_IsClonedOnceAndBothReferencesShareTheClone()
	{
		var original = _secrets.Store("value");
		var data = $"{{\"a\":{{\"$secret\":\"{original}\"}},\"b\":{{\"$secret\":\"{original}\"}}}}";

		var result = await _cloner.CloneReferencedSecrets(data);
		var ids = AllSecretIds(result!);

		Assert.Multiple(() =>
		{
			Assert.That(ids, Has.Count.EqualTo(2));
			Assert.That(ids.Distinct().Count(), Is.EqualTo(1));
			Assert.That(ids[0], Is.Not.EqualTo(original));
		});
	}

	[Test]
	public async Task SecretInsideEscapedFlowsJson_IsCloned()
	{
		var original = _secrets.Store("token");
		var data = $"{{\"flows\":\"[{{\\\"$secret\\\":\\\"{original}\\\"}}]\"}}";

		var result = await _cloner.CloneReferencedSecrets(data);

		Assert.That(result, Does.Not.Contain(original.ToString()));
		Assert.That(await _secrets.Resolve(SingleSecretId(result!)), Is.EqualTo("token"));
	}

	[Test]
	public async Task MissingSourceSecret_LeavesTheReferenceUnchanged()
	{
		var missing = Guid.NewGuid();
		var data = $"{{\"$secret\":\"{missing}\"}}";

		Assert.That(await _cloner.CloneReferencedSecrets(data), Is.EqualTo(data));
	}

	private static Guid SingleSecretId(string data) => AllSecretIds(data).Single();

	private static List<Guid> AllSecretIds(string data)
		=> Regex.Matches(data, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
			.Select(m => Guid.Parse(m.Value))
			.ToList();
}
