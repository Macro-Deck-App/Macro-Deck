using System.Text.Json;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Patches;

[TestFixture]
public class UiPatchSequencingTests
{
	private static UiPatch PatchFromTo(int from, int to) => new()
	{
		FromRevision = from,
		ToRevision = to,
		Operations =
		[
			new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "x", RemovedProperties = ["a"] },
		],
	};

	[TestCase(4, ExpectedResult = true)]
	[TestCase(3, ExpectedResult = false)]
	[TestCase(5, ExpectedResult = false)]
	public bool A_patch_applies_only_when_the_current_revision_matches_from_revision(int currentRevision)
		=> UiPatchSequencing.CheckRevisions(currentRevision, PatchFromTo(4, 5)).IsApplicable;

	[TestCase(4, 4)]
	[TestCase(4, 3)]
	public void A_patch_that_does_not_advance_the_revision_is_rejected(int from, int to)
		=> Assert.That(UiPatchSequencing.CheckRevisions(4, PatchFromTo(from, to)).IsApplicable, Is.False);

	[Test]
	public void A_patch_with_no_operations_is_rejected()
	{
		var patch = new UiPatch { FromRevision = 4, ToRevision = 5, Operations = [] };

		Assert.That(UiPatchSequencing.CheckRevisions(4, patch).IsApplicable, Is.False);
	}

	[Test]
	public void Revision_advance_is_not_computed_by_subtraction()
	{
		var patch = PatchFromTo(int.MaxValue, int.MinValue);

		Assert.That(UiPatchSequencing.CheckRevisions(int.MaxValue, patch).IsApplicable, Is.False);
	}

	[Test]
	public void A_null_operation_element_is_dropped_rather_than_deserialized_as_null()
	{
		const string json = """{"fromRevision":1,"toRevision":2,"operations":[null]}""";

		var patch = JsonSerializer.Deserialize<UiPatch>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(patch.Operations, Has.Count.EqualTo(0));
			Assert.That(UiCanonicalJson.Serialize(patch), Does.Contain("\"operations\":[]"));
		});
	}

	[Test]
	public void Rejection_reason_is_present_exactly_when_the_patch_is_rejected()
	{
		var accept = UiPatchSequencing.CheckRevisions(4, PatchFromTo(4, 5));
		var wrongCurrent = UiPatchSequencing.CheckRevisions(3, PatchFromTo(4, 5));
		var noAdvance = UiPatchSequencing.CheckRevisions(4, PatchFromTo(4, 4));
		var emptyPatch = new UiPatch { FromRevision = 4, ToRevision = 5, Operations = [] };
		var noOperations = UiPatchSequencing.CheckRevisions(4, emptyPatch);

		Assert.Multiple(() =>
		{
			foreach (var outcome in new[] { accept, wrongCurrent, noAdvance, noOperations })
			{
				Assert.That(outcome.RejectionReason is null, Is.EqualTo(outcome.IsApplicable));
				if (outcome.RejectionReason is not null)
				{
					Assert.That(outcome.RejectionReason, Is.Not.Empty);
				}
			}

			Assert.That(accept.IsApplicable, Is.True);
			Assert.That(wrongCurrent.IsApplicable, Is.False);
			Assert.That(noAdvance.IsApplicable, Is.False);
			Assert.That(noOperations.IsApplicable, Is.False);
		});
	}
}
