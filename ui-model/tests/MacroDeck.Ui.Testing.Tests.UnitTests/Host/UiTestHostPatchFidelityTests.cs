using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

/// <summary>
/// Regression coverage for the property the test host makes every other test assert for free: a patch the
/// producer emits is one the producer's own applier accepts, and replaying the stream reproduces the producer's
/// tree exactly. A patch that is minimal but wrong - a stale value, an off-by-one index, a missing removal - is
/// caught by nothing else, because every other assertion reads the producer's own tree and would agree with
/// itself. Traces to acceptance scenario 12 for issue #540, plus the host's own applied-tree contract.
/// </summary>
[TestFixture]
public class UiTestHostPatchFidelityTests
{
	[Test]
	public void Replaying_the_emitted_patches_onto_the_initial_tree_reproduces_the_final_tree()
	{
		// Deliberately against a bare UiView rather than through the host: the host folds patches through the
		// applier itself, so a host-only version of this test would assert the host's bookkeeping instead of the
		// producer's output.
		var script = new MutationScript();
		var view = new UiView(ConfigSurface(), script.Build());
		var initialTree = view.Tree;

		script.Mutate();

		var patches = view.DrainPatches();
		var folded = initialTree;

		Assert.That(patches, Is.Not.Empty, "the script has to produce something to replay");

		foreach (var patch in patches)
		{
			var result = UiTreeApplier.Apply(folded, patch);

			Assert.That(result.IsApplied, Is.True, result.RejectionReason);
			Assert.That(result.RejectionReason, Is.Null);

			folded = result.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(folded), Is.EqualTo(UiCanonicalJson.Serialize(view.Tree)));
			Assert.That(folded.Revision, Is.EqualTo(view.Revision));
		});
	}

	[Test]
	public void Every_produced_patch_applies_to_the_previous_tree()
	{
		var script = new MutationScript();
		var host = UiTestHost.Render(script.Build());
		var previous = host.Tree;

		script.Mutate();

		var patches = host.Patches;

		Assert.That(patches, Is.Not.Empty);

		// Each patch is checked against the tree its predecessor produced, never against the final tree: a patch
		// that is applicable to the end state but not to the state it was emitted from is exactly the defect a
		// renderer hits and a producer-side assertion cannot see.
		foreach (var patch in patches)
		{
			var result = UiTreeApplier.Apply(previous, patch);

			Assert.That(result.IsApplied,
				Is.True,
				$"patch {patch.FromRevision}->{patch.ToRevision}: {result.RejectionReason}");
			Assert.That(patch.FromRevision, Is.EqualTo(previous.Revision));

			previous = result.Tree;
		}

		Assert.That(previous.Revision, Is.EqualTo(host.Revision));
	}

	[Test]
	public void The_applied_tree_equals_the_producers_own_tree_byte_for_byte()
	{
		var script = new MutationScript();
		var host = UiTestHost.Render(script.Build());

		script.Mutate();

		// host.Tree is reconstructed from the patch stream; host.View.Tree is what the producer built directly.
		// The two being byte-equal is what lets every other test query the host and get a claim about the patches.
		Assert.Multiple(() =>
		{
			Assert.That(host.ToCanonicalJson(), Is.EqualTo(UiCanonicalJson.Serialize(host.View.Tree)));
			Assert.That(host.Tree.Revision, Is.EqualTo(host.View.Revision));
			Assert.That(host.Tree.Surface, Is.EqualTo(host.View.Tree.Surface));
		});
	}

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	/// <summary>
	/// The six-mutation script: a bound value, a condition turning true, a reorder, a removal, a value cleared to
	/// absent, and the condition turning false again. Six kinds of change rather than six of one, so a patch
	/// stream that is faithful for property changes and wrong for structural ones cannot pass.
	/// </summary>
	private sealed class MutationScript
	{
		private readonly HeaderItem[] _initialHeaders = [new("a"), new("b"), new("c")];
		private readonly UiState<string> _apiKey = new("initial");
		private readonly UiState<string?> _note = new("abc");
		private readonly UiState<bool> _advancedVisible = new(false);
		private readonly UiState<IReadOnlyList<HeaderItem>> _headers;

		internal MutationScript() => _headers = new UiState<IReadOnlyList<HeaderItem>>(_initialHeaders);

		internal UiFlow Build()
			=> new()
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children =
						[
							new UiStringInput { Key = "apiKey", Binding = Bind.To(_apiKey) },
							new UiStringInput { Key = "note", Binding = Bind.ReadOnly(OptionalText(_note)) },
							new UiArrayInput
							{
								Key = "headers",
								Children =
								[
									new UiRepeat<HeaderItem>
									{
										Key = "headerItems",
										Items = UiValue.From(() => _headers.Value),
										KeySelector = item => item.Id,
										Template = (item, _) => new UiObjectInput
										{
											Key = item.Id,
											Children = [new UiStringInput { Key = "name" }],
										},
									},
								],
							},
							new UiWhen
							{
								Key = "adv",
								Condition = () => _advancedVisible.Value,
								Content = () => new UiAdvancedSection
								{
									Key = "advanced",
									Children = [new UiDurationInput { Key = "timeout" }],
								},
							},
						],
					},
					new UiStep { Key = "verify" },
				],
			};

		internal void Mutate()
		{
			_apiKey.Value = "k-1";
			_advancedVisible.Value = true;
			_headers.Value = [_initialHeaders[2], _initialHeaders[0], _initialHeaders[1]];
			_headers.Value = [_initialHeaders[2], _initialHeaders[0]];
			_note.Value = null;
			_advancedVisible.Value = false;
		}

		/// <summary>An optional text value: absent while the state holds null, present otherwise. Absence has to be
		/// expressed through the value itself, because a present null means "explicitly null" on the wire.</summary>
		private static UiValue<string> OptionalText(UiState<string?> state)
			=> UiValue.Optional(() => state.Value is { } text ? UiValue.Of(text) : UiValue.None<string>());
	}
}
