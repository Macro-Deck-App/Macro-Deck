using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Layouts;

/// <summary>The registry side of the layout provider contract (issue #384): registration is qualified
/// and owner-scoped, and two providers naming the same local id resolve independently.</summary>
public class LayoutRegistryTests
{
	private LayoutRegistry _registry = null!;
	private RecordingMediator _mediator = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_registry = new LayoutRegistry(_mediator);
	}

	[Test]
	public async Task Register_QualifiesTheLayoutIdWithTheOwner()
	{
		var registration = await _registry.Register("com.example.deck", FixedGrid("grid-1", 3, 5));

		Assert.That(registration.LayoutId, Is.EqualTo("com.example.deck::grid-1"));
		Assert.That(registration.ProviderId, Is.EqualTo("com.example.deck"));
	}

	[Test]
	public void Register_RejectsAnEmptyOwnerId()
		=> Assert.That(() => _registry.Register(string.Empty, FixedGrid("grid-1", 3, 5)),
			Throws.ArgumentException);

	[Test]
	public void Register_RejectsALocalIdContainingTheQualifierSeparator()
		=> Assert.That(() => _registry.Register("com.example.deck", FixedGrid("a::b", 3, 5)),
			Throws.ArgumentException);

	[Test]
	public void TryResolve_UnknownQualifiedId_ReturnsFalse()
		=> Assert.That(_registry.TryResolve("com.example.deck::missing", out _), Is.False);

	[Test]
	public async Task TwoProviders_RegisteringTheSameLocalName_ResolveIndependentlyByQualifiedId()
	{
		var first = await _registry.Register("com.example.first", FixedGrid("grid", 3, 5));
		var second = await _registry.Register("com.example.second", FixedGrid("grid", 8, 4));

		Assert.That(_registry.TryResolve(first.LayoutId, out var resolvedFirst), Is.True);
		Assert.That(_registry.TryResolve(second.LayoutId, out var resolvedSecond), Is.True);
		Assert.That(resolvedFirst.PrimaryGrid!.Grid!.Rows, Is.EqualTo(3));
		Assert.That(resolvedSecond.PrimaryGrid!.Grid!.Rows, Is.EqualTo(8));
	}

	[Test]
	public async Task UnregisterAll_RemovesOnlyThatOwnersLayouts()
	{
		var owned = await _registry.Register("com.example.first", FixedGrid("grid", 3, 5));
		var other = await _registry.Register("com.example.second", FixedGrid("grid", 8, 4));

		await _registry.UnregisterAll("com.example.first");

		Assert.That(_registry.TryResolve(owned.LayoutId, out _), Is.False);
		Assert.That(_registry.TryResolve(other.LayoutId, out _), Is.True);
	}

	[Test]
	public async Task CrossOwnerReference_ResolvesByFullQualifiedId()
	{
		var registration = await _registry.Register("com.example.layouts", FixedGrid("grid", 3, 5));

		// A device provider from a different owner may still reference this layout - descriptive
		// metadata, not a privilege.
		var resolved = _registry.TryResolve(registration.LayoutId, out var layout);

		Assert.That(resolved, Is.True);
		Assert.That(layout.Id, Is.EqualTo("grid"));
	}

	[Test]
	public async Task Register_PublishesLayoutCatalogChanged()
	{
		await _registry.Register("com.example.deck", FixedGrid("grid-1", 3, 5));

		Assert.That(_mediator.Published, Has.Some.InstanceOf<LayoutCatalogChangedNotification>());
	}

	[Test]
	public async Task Unregister_OfUnknownId_DoesNotPublish()
	{
		await _registry.Unregister("com.example.deck", "missing");

		Assert.That(_mediator.Published, Is.Empty);
	}

	private static LayoutDescriptor FixedGrid(string id, int rows, int columns)
		=> new(id,
			"Test Grid",
			[
				new LayoutRegion
				{
					Id = "grid", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = rows, Columns = columns }
				}
			]);
}
