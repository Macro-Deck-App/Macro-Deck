using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconPackServiceTests
{
	private IconTestHarness _harness = null!;
	private IconPackService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = new IconPackService(_harness.Cache,
			_harness.BatchTracker,
			_harness.Storage,
			_harness.Mediator,
			new IconPackOwnerRegistry([]),
			_harness.Logger);
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Create_TrimsAndPersists_AndPublishesEvent()
	{
		var result = await _service.Create("  My Pack  ", " desc ", null, "1.0");

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Name, Is.EqualTo("My Pack"));
			Assert.That(result.Data!.Description, Is.EqualTo("desc"));
			Assert.That(result.Data!.Author, Is.Null);
			Assert.That(_harness.Mediator.Published.OfType<IconPackCreatedNotification>().Count(), Is.EqualTo(1));
			Assert.That(_harness.PackStore.LoadAll(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Create_EmptyName_Fails()
	{
		var result = await _service.Create("   ", null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconPackError.ValidationError));
		});
	}

	[Test]
	public async Task Update_ReadOnlyPack_IsRejected()
	{
		var pack = await _harness.CreatePack("Store Pack", isReadOnly: true);

		var result = await _service.Update(pack.Id, "New Name", null, null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconPackError.ReadOnly));
			Assert.That(_harness.Cache.GetPackById(pack.Id)!.Name, Is.EqualTo("Store Pack"));
		});
	}

	[Test]
	public async Task Delete_DefaultPack_IsRejected()
	{
		var pack = await _harness.CreatePack("Imported Icons", isDefault: true);

		var result = await _service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconPackError.DefaultPackProtected));
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Not.Null);
		});
	}

	[Test]
	public async Task Delete_ReadOnlyPack_IsRejected()
	{
		var pack = await _harness.CreatePack("Store Pack", isReadOnly: true);

		var result = await _service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconPackError.ReadOnly));
		});
	}

	[Test]
	public async Task Delete_RemovesPackAndPublishesEvent()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Delete(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Null);
			Assert.That(_harness.Mediator.Published.OfType<IconPackDeletedNotification>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Update_UnknownPack_ReturnsNotFound()
	{
		var result = await _service.Update(Guid.NewGuid(), "x", null, null, null);

		Assert.That(result.Error, Is.EqualTo(IconPackError.NotFound));
	}
}
