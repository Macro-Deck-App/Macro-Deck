using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Dsl;

/// <summary>
/// Regression coverage for the DSL's id materialization: the identity rules documented on
/// <see cref="UiViewBuilder" /> are the only contract a plugin author can rely on when composing chrome,
/// inputs, conditionals, fragments and repeats into a tree. Each test below traces to one of acceptance
/// scenarios 1-7 for issue #540.
/// </summary>
[TestFixture]
public class UiElementIdentityTests
{
	/// <summary>
	/// Builds the shared SampleFlow fixture: a "setup" flow with a "credentials" step (an apiKey string, a
	/// mode choice, an endpoint object with host/port, a headers array repeating name/value pairs, and a
	/// gated advanced-section with a timeout duration) and a "verify" step.
	/// </summary>
	private static UiFlow BuildSampleFlow(
		IReadOnlyList<HeaderItem> headers,
		bool advancedVisible = false,
		bool wrapApiKeyInWhenAndFragment = false,
		UiState<IReadOnlyList<HeaderItem>>? headerState = null)
	{
		UiElement apiKeyElement = new UiStringInput { Key = "apiKey" };

		if (wrapApiKeyInWhenAndFragment)
		{
			// Captured into its own local first: a closure over apiKeyElement would capture the variable,
			// which is reassigned on the next line, so Content would return a fragment containing this very
			// UiWhen and materialization would recurse forever.
			var wrapped = apiKeyElement;

			apiKeyElement = new UiWhen
			{
				Key = "gate",
				Condition = () => true,
				Content = () => new UiFragment { Key = "group", Children = [wrapped] },
			};
		}

		return new UiFlow
		{
			Key = "setup",
			Children =
			[
				new UiStep
				{
					Key = "credentials",
					Children =
					[
						apiKeyElement,
						new UiChoiceInput { Key = "mode" },
						new UiObjectInput
						{
							Key = "endpoint",
							Children = [new UiStringInput { Key = "host" }, new UiStringInput { Key = "port" }],
						},
						new UiArrayInput
						{
							Key = "headers",
							Children =
							[
								new UiRepeat<HeaderItem>
								{
									Key = "headerItems",
									Items = headerState is null
										? UiValue.Of(headers)
										: UiValue.From(() => headerState.Value),
									KeySelector = item => item.Id,
									Template = (item, _) => new UiObjectInput
									{
										Key = item.Id,
										Children =
										[
											new UiStringInput { Key = "name" }, new UiStringInput { Key = "value" },
										],
									},
								},
							],
						},
						new UiWhen
						{
							Key = "adv",
							Condition = () => advancedVisible,
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
	}

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiNode? FindById(UiNode node, string id)
	{
		if (node.Id == id)
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			var found = FindById(child, id);
			if (found is not null)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private static IEnumerable<string> AllIds(UiNode node)
	{
		yield return node.Id;

		foreach (var child in node.Children)
		{
			foreach (var id in AllIds(child))
			{
				yield return id;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var id in AllIds(node.Fallback))
			{
				yield return id;
			}
		}
	}

	private static IEnumerable<string> AllTypes(UiNode node)
	{
		yield return node.Type;

		foreach (var child in node.Children)
		{
			foreach (var type in AllTypes(child))
			{
				yield return type;
			}
		}

		if (node.Fallback is not null)
		{
			foreach (var type in AllTypes(node.Fallback))
			{
				yield return type;
			}
		}
	}

	private static int CountOccurrences(string text, string value)
	{
		var count = 0;
		var index = 0;

		while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += value.Length;
		}

		return count;
	}

	[Test]
	public void A_top_level_input_node_id_is_its_bare_key_regardless_of_its_chrome_ancestry()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(), BuildSampleFlow(headers: []));

		var apiKey = FindById(tree.Root, "apiKey");

		Assert.Multiple(() =>
		{
			Assert.That(apiKey, Is.Not.Null);
			Assert.That(apiKey!.Type, Is.EqualTo("string"));
			Assert.That(FindById(tree.Root, "setup.credentials.apiKey"), Is.Null);
			Assert.That(apiKey.Id, Does.Not.Contain("setup"));
			Assert.That(apiKey.Id, Does.Not.Contain("credentials"));
		});
	}

	[Test]
	public void An_input_inside_an_object_or_array_container_is_addressed_by_its_dotted_path_from_that_container()
	{
		var headers = new[] { new HeaderItem("h1"), new HeaderItem("h2") };
		var tree = UiViewBuilder.Build(ConfigSurface(), BuildSampleFlow(headers));

		var ids = AllIds(tree.Root).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Does.Contain("endpoint"));
			Assert.That(ids, Does.Contain("endpoint.host"));
			Assert.That(ids, Does.Contain("endpoint.port"));
			Assert.That(ids, Does.Contain("headers"));
			Assert.That(ids, Does.Contain("headers.h1.name"));
			Assert.That(ids, Does.Contain("headers.h1.value"));
			Assert.That(ids, Does.Contain("headers.h2.name"));
			Assert.That(ids, Does.Contain("headers.h2.value"));
			Assert.That(ids, Does.Not.Contain("host"));
			Assert.That(ids, Does.Not.Contain("port"));
			Assert.That(ids, Does.Not.Contain("name"));
			Assert.That(ids, Does.Not.Contain("value"));
			Assert.That(ids, Does.Not.Contain("setup.credentials.endpoint.host"));
		});
	}

	[Test]
	public void UiWhen_and_UiFragment_contribute_neither_a_node_nor_a_path_segment()
	{
		var plain = UiViewBuilder.Build(ConfigSurface(), BuildSampleFlow(headers: []));
		var wrapped = UiViewBuilder.Build(ConfigSurface(),
			BuildSampleFlow(headers: [], wrapApiKeyInWhenAndFragment: true));

		// Not the real 41-member vocabulary, which the configuration fixtures own - just the subset SampleFlow
		// uses, so this negatively asserts that "ui-when"/"ui-fragment" never leak in as node types.
		var allowedTypes = new HashSet<string>(StringComparer.Ordinal)
		{
			"flow", "step", "advanced-section", "string", "choice", "duration", "object", "array",
		};

		Assert.Multiple(() =>
		{
			Assert.That(FindById(wrapped.Root, "apiKey"), Is.Not.Null);
			Assert.That(FindById(wrapped.Root, "gate"), Is.Null);
			Assert.That(FindById(wrapped.Root, "group"), Is.Null);
			Assert.That(FindById(wrapped.Root, "gate.apiKey"), Is.Null);
			Assert.That(FindById(wrapped.Root, "group.apiKey"), Is.Null);
			Assert.That(AllTypes(wrapped.Root).ToHashSet(StringComparer.Ordinal), Is.SubsetOf(allowedTypes));
			Assert.That(UiCanonicalJson.Serialize(wrapped), Is.EqualTo(UiCanonicalJson.Serialize(plain)));
		});
	}

	[Test]
	public void UiRepeat_contributes_the_item_key_and_never_the_item_index()
	{
		var headers = new[] { new HeaderItem("alpha"), new HeaderItem("beta"), new HeaderItem("gamma") };
		var tree = UiViewBuilder.Build(ConfigSurface(), BuildSampleFlow(headers));
		var ids = AllIds(tree.Root).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(ids, Does.Contain("headers.alpha.name"));
			Assert.That(ids, Does.Contain("headers.beta.name"));
			Assert.That(ids, Does.Contain("headers.gamma.name"));
			Assert.That(ids, Has.None.Matches<string>(id => id.StartsWith("headers.0", StringComparison.Ordinal)));
			Assert.That(ids, Has.None.Matches<string>(id => id.StartsWith("headers.1", StringComparison.Ordinal)));
			Assert.That(ids, Has.None.Matches<string>(id => id.StartsWith("headers.2", StringComparison.Ordinal)));
		});

		// The same removal against a live view, so the id stability above is asserted of the node instances that
		// actually survived rather than of a freshly rebuilt tree.
		var headerState = new UiState<IReadOnlyList<HeaderItem>>(headers);
		var view = new UiView(ConfigSurface(), BuildSampleFlow(headers: [], headerState: headerState));

		view.DrainPatches();
		headerState.Value = [headers[1], headers[2]];

		var patches = view.DrainPatches();
		var idsAfterRemoval = AllIds(view.Tree.Root).ToList();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations,
			Has.Count.EqualTo(1),
			"removing one item removes one subtree, and the model removes the subtree with its top-most node");

		var operation = patches[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.RemoveNode));
			Assert.That(operation.NodeId, Is.EqualTo("headers.alpha"));

			// The assertion positional ids cannot satisfy: the surviving items keep the ids they had.
			Assert.That(idsAfterRemoval, Does.Contain("headers.beta.name"));
			Assert.That(idsAfterRemoval, Does.Contain("headers.gamma.name"));
			Assert.That(idsAfterRemoval, Does.Not.Contain("headers.alpha"));
			Assert.That(idsAfterRemoval, Does.Not.Contain("headers.alpha.name"));
		});
	}

	[Test]
	public void A_key_or_composed_id_that_violates_the_identifier_grammar_fails_the_view()
	{
		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [new UiStringInput { Key = "api key" }] }));

		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [new UiStringInput { Key = "api:key" }] }));

		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [new UiStringInput { Key = "_lead" }] }));

		// Every individual key is 30 characters - well under the 128-character limit on its own - but
		// five levels of object nesting compose to 154 characters. Validation must be of the composed id,
		// not the bare key, for this to fail.
		var longKey = new string('a', 30);
		UiElement deeplyNested = new UiStringInput { Key = "leaf" };
		for (var level = 0; level < 5; level++)
		{
			deeplyNested = new UiObjectInput { Key = longKey, Children = [deeplyNested] };
		}

		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [deeplyNested] }));

		Assert.DoesNotThrow(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [new UiStringInput { Key = "field.apiKey" }] }));

		Assert.DoesNotThrow(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [new UiStringInput { Key = "item-3" }] }));
	}

	[Test]
	public void A_duplicate_node_id_anywhere_in_the_tree_including_a_fallback_subtree_fails_the_view()
	{
		// Case 1: a structural element (the root itself, keyed "apiKey") and a top-level input both
		// resolve to the bare id "apiKey".
		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow
			{
				Key = "apiKey",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children = [new UiStringInput { Key = "apiKey" }],
					},
				],
			}));

		// Case 2: two repeat items whose KeySelector returns the same string.
		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			BuildSampleFlow([new HeaderItem("dup"), new HeaderItem("dup")])));

		// Case 3: an element's Fallback resolves to the same id as another node.
		Assert.Throws<UiViewException>(() => UiViewBuilder.Build(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children =
						[
							new UiStringInput { Key = "apiKey" },
							new UiStringInput { Key = "mode", Fallback = new UiStringInput { Key = "apiKey" } },
						],
					},
				],
			}));

		// Control: a Fallback with a distinct key does not throw and is reachable.
		var withDistinctFallback = UiViewBuilder.Build(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children =
						[
							new UiStringInput { Key = "mode", Fallback = new UiStringInput { Key = "modeFallback" } },
						],
					},
				],
			});

		var mode = FindById(withDistinctFallback.Root, "mode");

		Assert.Multiple(() =>
		{
			Assert.That(mode, Is.Not.Null);
			Assert.That(mode!.Fallback, Is.Not.Null);
			Assert.That(mode.Fallback!.Id, Is.EqualTo("modeFallback"));
			Assert.That(FindById(withDistinctFallback.Root, "modeFallback"), Is.Not.Null);
		});
	}

	[Test]
	public void RequiredComponentVersion_is_emitted_only_when_declared()
	{
		var tree = UiViewBuilder.Build(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "widget", RequiredComponentVersion = 2 },
					new UiStringInput { Key = "plainField" },
				],
			});

		var json = UiCanonicalJson.Serialize(tree);
		var plainField = FindById(tree.Root, "plainField");

		Assert.Multiple(() =>
		{
			Assert.That(CountOccurrences(json, "\"requiredComponentVersion\":2"), Is.EqualTo(1));
			Assert.That(plainField, Is.Not.Null);
			Assert.That(plainField!.RequiredComponentVersion, Is.Null);
		});
	}
}
