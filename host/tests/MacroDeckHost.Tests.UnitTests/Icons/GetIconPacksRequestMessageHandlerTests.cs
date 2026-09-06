using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class GetIconPacksRequestMessageHandlerTests
{
	private static readonly string[] _expectedOrder = ["Zebra Icons", "Apple", "banana"];

	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Handle_SortsDefaultPackFirst_ThenAlphabetically()
	{
		await _harness.CreatePack("banana");
		await _harness.CreatePack("Zebra Icons", isDefault: true);
		await _harness.CreatePack("Apple");
		var handler = new GetIconPacksRequestMessageHandler(_harness.Cache, new IconPackOwnerRegistry([]));

		var response = await handler.Handle(new GetIconPacksRequest(), CancellationToken.None);

		Assert.That(response.Packs.Select(p => p.Name), Is.EqualTo(_expectedOrder));
	}
}
