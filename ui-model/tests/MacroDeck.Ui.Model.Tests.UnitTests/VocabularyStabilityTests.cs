using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeck.Ui.Model.Tests.UnitTests;

/// <summary>
/// Pins every wire vocabulary against hard-coded literals, never derived from the constants under
/// test - deriving them would make the test agree with any rename, which is the one thing it exists to
/// catch.
/// </summary>
[TestFixture]
public class VocabularyStabilityTests
{
	private static readonly string[] _fivePatchOperations =
		["set-properties", "insert-node", "remove-node", "replace-node", "move-node"];

	private static readonly string[] _twoSessionModes = ["shared", "exclusive"];

	private static readonly string[] _surfaceKinds =
		["config", "widget", "dialog", "preview", "folder", "developer-preview"];

	private static readonly int[] _supportedVersions = [3, 4];

	[Test]
	public void The_five_patch_operations_are_the_frozen_list()
		=> Assert.That(UiPatchOperations.All, Is.EqualTo(_fivePatchOperations));

	[Test]
	public void The_two_session_modes_are_the_frozen_list()
		=> Assert.That(UiSessionModes.WellKnown, Is.EqualTo(_twoSessionModes));

	[Test]
	public void The_well_known_surface_kinds_are_the_frozen_list()
		=> Assert.That(UiSurfaceKinds.WellKnown, Is.EqualTo(_surfaceKinds));

	[Test]
	public void The_model_version_numbers_are_the_frozen_values()
	{
		Assert.Multiple(() =>
		{
			Assert.That(UiModelVersions.Minimum, Is.EqualTo(3));
			Assert.That(UiModelVersions.Current, Is.EqualTo(4));
			Assert.That(UiModelVersions.Supported, Is.EqualTo(_supportedVersions));
		});
	}
}
