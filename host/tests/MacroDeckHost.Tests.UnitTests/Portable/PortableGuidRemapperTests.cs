using MacroDeckHost.Application.Portable;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableGuidRemapperTests
{
	[Test]
	public void Remap_ReplacesMappedGuids_AndLeavesOthersUntouched()
	{
		var oldIcon = Guid.NewGuid();
		var newIcon = Guid.NewGuid();
		var unmapped = Guid.NewGuid();
		var data = $"{{\"iconId\":\"{oldIcon}\",\"target\":\"{unmapped}\"}}";

		var result = PortableGuidRemapper.Remap(data, new Dictionary<Guid, Guid> { [oldIcon] = newIcon });

		Assert.Multiple(() =>
		{
			Assert.That(result, Does.Contain(newIcon.ToString()));
			Assert.That(result, Does.Not.Contain(oldIcon.ToString()));
			Assert.That(result, Does.Contain(unmapped.ToString()));
		});
	}

	[Test]
	public void Remap_IsCaseInsensitive()
	{
		var oldId = Guid.NewGuid();
		var newId = Guid.NewGuid();
		var data = $"{{\"$secret\":\"{oldId.ToString().ToUpperInvariant()}\"}}";

		var result = PortableGuidRemapper.Remap(data, new Dictionary<Guid, Guid> { [oldId] = newId });

		Assert.That(result, Does.Contain(newId.ToString()));
	}

	[Test]
	public void Remap_HandlesEscapedNestedFlowsJson()
	{
		var oldId = Guid.NewGuid();
		var newId = Guid.NewGuid();
		var data = $"{{\"flows\":\"[{{\\\"$secret\\\":\\\"{oldId}\\\"}}]\"}}";

		var result = PortableGuidRemapper.Remap(data, new Dictionary<Guid, Guid> { [oldId] = newId });

		Assert.Multiple(() =>
		{
			Assert.That(result, Does.Contain(newId.ToString()));
			Assert.That(result, Does.Not.Contain(oldId.ToString()));
		});
	}

	[Test]
	public void Remap_WithNoMap_ReturnsInputUnchanged()
	{
		const string data = "{\"label\":\"hi\"}";

		Assert.That(PortableGuidRemapper.Remap(data, new Dictionary<Guid, Guid>()), Is.EqualTo(data));
	}

	[Test]
	public void Remap_WithNullData_ReturnsNull()
		=> Assert.That(
			PortableGuidRemapper.Remap(null, new Dictionary<Guid, Guid> { [Guid.NewGuid()] = Guid.NewGuid() }),
			Is.Null);
}
