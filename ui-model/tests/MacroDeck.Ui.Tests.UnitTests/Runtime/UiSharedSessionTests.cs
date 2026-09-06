using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Tests.UnitTests.Dsl;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for the one rule a shared session adds: per-client state never enters the tree. The
/// model documents focus, scroll and hover as client-local, and if they leak one user's caret moves another's -
/// and removing the property afterwards would be a breaking change, so it has to be caught before the
/// vocabulary ships. Traces to acceptance scenario 39 for issue #540.
/// </summary>
[TestFixture]
public class UiSharedSessionTests
{
	/// <summary>
	/// The test's own deny-list, not derived from anything in the package under test. <c>expanded</c> is on it
	/// deliberately: <c>defaultExpanded</c> is authored data and legal, and the <c>expand</c> and
	/// <c>collapse</c> events are the client reporting what a user did, but the tree must never carry which
	/// clients currently have the section open.
	/// </summary>
	private static readonly string[] _forbiddenKeys =
	[
		"focus", "focused", "hasFocus", "scroll", "scrollTop", "scrollOffset", "hover", "hovered", "caret",
		"caretPosition", "selectionStart", "selectionEnd", "expanded",
	];

	[Test]
	public async Task Per_client_state_never_appears_in_the_tree_in_a_shared_session()
	{
		var apiKeyState = new UiState<string>("initial");
		var modeState = new UiState<string>("advanced");
		IReadOnlyList<HeaderItem> headers = [new("h1"), new("h2")];
		var headerState = new UiState<IReadOnlyList<HeaderItem>>(headers);

		var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Shared },
			BuildSampleFlow(apiKeyState, modeState, headerState));

		// The authored counterpart is legal and has to be there, so this test cannot pass by the section having
		// lost its expansion properties altogether.
		var advanced = FindById(view.Tree.Root, "setup.credentials.advanced");

		Assert.That(advanced, Is.Not.Null);
		Assert.That(advanced!.Properties.ContainsKey(UiConfigProperties.DefaultExpanded), Is.True);

		AssertNoForbiddenKeys(view.Tree.Root);

		view.DrainPatches();

		// Every frozen event name, at every node that accepts it. A property that only appears after an
		// interaction is exactly the shape a focus or expansion leak takes.
		var accepted = 0;

		foreach (var node in Walk(view.Tree.Root))
		{
			foreach (var name in UiConfigEvents.WellKnown)
			{
				var result = view.Dispatch(new UiEvent
				{
					NodeId = node.Id,
					Name = name,
					Data = ChangeData(name),
				});

				if (result.IsAccepted)
				{
					accepted++;
				}
			}
		}

		await view.WhenIdleAsync(TestContext.CurrentContext.CancellationToken);

		var patches = view.DrainPatches();

		Assert.That(accepted, Is.GreaterThan(0), "the fixture has to accept something for this to prove anything");

		AssertNoForbiddenKeys(view.Tree.Root);

		Assert.Multiple(() =>
		{
			foreach (var patch in patches)
			{
				foreach (var operation in patch.Operations)
				{
					foreach (var key in _forbiddenKeys)
					{
						Assert.That(operation.Properties?.ContainsKey(key) ?? false,
							Is.False,
							$"a patch on '{operation.NodeId}' carried the client-local key '{key}'");
					}

					if (operation.Node is { } inserted)
					{
						AssertNoForbiddenKeys(inserted);
					}
				}
			}
		});
	}

	/// <summary>The payload a change event needs, and nothing for the rest - a wrong payload is rejected rather
	/// than dispatched, which would make the sweep above prove less than it looks.</summary>
	private static JsonElement? ChangeData(string name)
		=> string.Equals(name, UiConfigEvents.Change, StringComparison.Ordinal)
			? UiCanonicalJson.ToElement("swept")
			: null;

	private static void AssertNoForbiddenKeys(UiNode root)
	{
		Assert.Multiple(() =>
		{
			foreach (var node in Walk(root))
			{
				foreach (var key in _forbiddenKeys)
				{
					Assert.That(node.Properties.ContainsKey(key),
						Is.False,
						$"the node '{node.Id}' carried the client-local key '{key}'");
				}
			}
		});
	}

	/// <summary>The SampleFlow fixture, with the advanced section visible and every element carrying handlers for
	/// the events its primitive actually raises.</summary>
	private static UiFlow BuildSampleFlow(
		UiState<string> apiKeyState,
		UiState<string> modeState,
		UiState<IReadOnlyList<HeaderItem>> headerState)
		=> new()
		{
			Key = "setup",
			Title = "Setup",
			CanSubmit = true,
			Events =
			[
				UiEventHandler.On(UiConfigEvents.Submit, () => { }),
				UiEventHandler.On(UiConfigEvents.Cancel, () => { }),
				UiEventHandler.On(UiConfigEvents.Back, () => { }),
			],
			Children =
			[
				new UiStep
				{
					Key = "credentials",
					Events = [UiEventHandler.On(UiConfigEvents.Submit, () => { })],
					Children =
					[
						new UiStringInput { Key = "apiKey", Label = "API key", Binding = Bind.To(apiKeyState) },
						new UiChoiceInput { Key = "mode", Binding = Bind.To(modeState) },
						new UiObjectInput
						{
							Key = "endpoint",
							Children = [new UiStringInput { Key = "host" }, new UiNumberInput { Key = "port" }],
						},
						new UiArrayInput
						{
							Key = "headers",
							Events =
							[
								UiEventHandler.On(UiConfigEvents.Add, () => { }),
								UiEventHandler.On(UiConfigEvents.Remove, () => { }),
							],
							Children =
							[
								new UiRepeat<HeaderItem>
								{
									Key = "headerItems",
									Items = UiValue.From(() => headerState.Value),
									KeySelector = item => item.Id,
									Template = (item, _) => new UiObjectInput
									{
										Key = item.Id,
										Children =
										[
											new UiStringInput { Key = "name" },
											new UiStringInput { Key = "value" },
										],
									},
								},
							],
						},
						new UiLink
						{
							Key = "docs",
							Label = "Docs",
							Url = "https://example.com",
							Events = [UiEventHandler.On(UiConfigEvents.Activate, () => { })],
						},
						new UiWhen
						{
							Key = "adv",
							Condition = () => string.Equals(modeState.Value, "advanced", StringComparison.Ordinal),
							Content = () => new UiAdvancedSection
							{
								Key = "advanced",
								Label = "Advanced configuration",
								DefaultExpanded = false,
								Events =
								[
									UiEventHandler.On(UiConfigEvents.Expand, () => { }),
									UiEventHandler.On(UiConfigEvents.Collapse, () => { }),
								],
								Children = [new UiDurationInput { Key = "timeout" }],
							},
						},
					],
				},
			],
		};

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private static IEnumerable<UiNode> Walk(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children)
		{
			foreach (var descendant in Walk(child))
			{
				yield return descendant;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var descendant in Walk(node.Fallback))
			{
				yield return descendant;
			}
		}
	}
}
