using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class WidgetVariableIndexTests
{
	private const string LabelOnCpu = "{\"label\":\"{{ vars.cpu }}\"}";
	private const string LabelOnRam = "{\"label\":\"{{ vars.ram }}\"}";

	private const string BoundOnCpu =
		"{\"stateBinding\":{\"kind\":\"compare\",\"left\":{\"$var\":\"cpu\"},\"right\":80}}";

	private static WidgetEntity Widget(string? data, string type = WidgetTypeIds.ActionButton) => new()
	{
		Id = Guid.NewGuid(),
		FolderId = Guid.Empty,
		Type = type,
		Data = data
	};

	[Test]
	public void Rebuild_indexesOnlyReferencingActionButtons()
	{
		var referencing = Widget(LabelOnCpu);
		var other = Widget(LabelOnRam);
		var plain = Widget("{\"label\":\"static\"}");
		var cache = new StubFolderCache();
		cache.AddFolder(referencing, other, plain);
		var index = new WidgetVariableIndex(cache);

		index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(index.FindLabelReferences("cpu"), Is.EquivalentTo([referencing.Id]));
			Assert.That(index.FindLabelReferences("ram"), Is.EquivalentTo([other.Id]));
			Assert.That(index.FindLabelReferences("gpu"), Is.Empty);
		});
	}

	[Test]
	public void Rebuild_ignoresNonActionButtonWidgets()
	{
		var clock = Widget("{\"label\":\"{{ vars.cpu }}\"}", WidgetTypeIds.Clock);
		var cache = new StubFolderCache();
		cache.AddFolder(clock);
		var index = new WidgetVariableIndex(cache);

		index.Rebuild();

		Assert.That(index.FindLabelReferences("cpu"), Is.Empty);
	}

	[Test]
	public void LabelAndStateBindingAreIndexedSeparately()
	{
		var labelled = Widget(LabelOnCpu);
		var bound = Widget(BoundOnCpu);
		var cache = new StubFolderCache();
		cache.AddFolder(labelled, bound);
		var index = new WidgetVariableIndex(cache);

		index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(index.FindLabelReferences("cpu"), Is.EquivalentTo([labelled.Id]));
			Assert.That(index.FindStateMappingReferences("cpu"), Is.EquivalentTo([bound.Id]));
		});
	}

	[Test]
	public void ReindexWidget_picksUpANewReference()
	{
		var widget = Widget("{\"label\":\"static\"}");
		var cache = new StubFolderCache();
		cache.AddFolder(widget);
		var index = new WidgetVariableIndex(cache);
		index.Rebuild();

		index.ReindexWidget(widget.Id, WidgetTypeIds.ActionButton, LabelOnCpu);

		Assert.That(index.FindLabelReferences("cpu"), Is.EquivalentTo([widget.Id]));
	}

	[Test]
	public void ReindexWidget_dropsAReferenceThatWasEditedAway()
	{
		var widget = Widget(LabelOnCpu);
		var cache = new StubFolderCache();
		cache.AddFolder(widget);
		var index = new WidgetVariableIndex(cache);
		index.Rebuild();

		index.ReindexWidget(widget.Id, WidgetTypeIds.ActionButton, "{\"label\":\"static\"}");

		Assert.Multiple(() =>
		{
			Assert.That(index.FindLabelReferences("cpu"), Is.Empty);
			Assert.That(index.LabelReferences(widget.Id, "cpu"), Is.False);
		});
	}

	[Test]
	public void Remove_dropsTheWidget()
	{
		var widget = Widget(LabelOnCpu);
		var cache = new StubFolderCache();
		cache.AddFolder(widget);
		var index = new WidgetVariableIndex(cache);
		index.Rebuild();

		index.Remove(widget.Id);

		Assert.That(index.FindLabelReferences("cpu"), Is.Empty);
	}

	[Test]
	public void Rebuild_afterAFolderLeftTheCache_dropsItsWidgets()
	{
		var kept = Widget(LabelOnCpu);
		var going = Widget(LabelOnCpu);
		var cache = new StubFolderCache();
		cache.AddFolder(kept);
		var doomed = cache.AddFolder(going);
		var index = new WidgetVariableIndex(cache);
		index.Rebuild();

		cache.RemoveFolder(doomed.Id);
		index.Rebuild();

		Assert.That(index.FindLabelReferences("cpu"), Is.EquivalentTo([kept.Id]));
	}

	[Test]
	public void PerWidgetLookup_answersForTheOwnerOnly()
	{
		var owner = Widget(LabelOnCpu);
		var unrelated = Widget(LabelOnCpu);
		var cache = new StubFolderCache();
		cache.AddFolder(owner, unrelated);
		var index = new WidgetVariableIndex(cache);
		index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(index.LabelReferences(owner.Id, "cpu"), Is.True);
			Assert.That(index.LabelReferences(owner.Id, "ram"), Is.False);
			Assert.That(index.LabelReferences(Guid.NewGuid(), "cpu"), Is.False);
		});
	}
}
