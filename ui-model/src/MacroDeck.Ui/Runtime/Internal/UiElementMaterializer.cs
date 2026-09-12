using System.Text.Json;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Identity;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// The recursive walk from an authored <see cref="UiElement" /> tree to materialized <see cref="UiNode" />
/// instances, enforcing the identity rules <see cref="UiViewBuilder" /> documents. Internal:
/// <see cref="UiViewBuilder.Build" /> and <see cref="UiView" /> are the only public surfaces over it.
///
/// <para>
/// One instance materializes exactly one tree once, because the id registry and the dependents it collects
/// describe that one walk. A <c>null</c> view materializes an <b>untracked</b> tree: cells still evaluate, but
/// nothing subscribes to any state, so <see cref="UiViewBuilder.Build" /> cannot leave a state holding a view
/// it never handed out.
/// </para>
/// </summary>
internal sealed class UiElementMaterializer
{
	/// <summary>
	/// The deepest chain of nested elements a view may declare, counted in materialized nodes.
	///
	/// <para>
	/// Its real job is to turn unbounded recursion into an author error. A <see cref="UiWhen.Content" /> or
	/// <see cref="UiRepeat{TItem}.Template" /> closure that reaches its own element - which a closure
	/// capturing the variable it is being assigned to does by accident - would otherwise exhaust the stack,
	/// and a <see cref="StackOverflowException" /> cannot be caught, so a plugin author's slip would take the
	/// whole plugin process down instead of surfacing as a <see cref="UiViewException" /> naming the path.
	/// </para>
	///
	/// <para>
	/// The value is not arbitrary: <see cref="Model.Serialization.UiCanonicalJson.MaxDepth" /> caps the
	/// canonical form at 32 levels and every node costs two of them (its object plus its children array), so
	/// a tree deeper than this could not be serialized at all. Anything approaching it is a runaway rather
	/// than a real configuration surface.
	/// </para>
	/// </summary>
	private const int _maxDepth = 32;

	private readonly List<UiDependent> _dependents = [];
	private readonly UiNodeIdRegistry _registry = new();
	private readonly UiView? _view;

	internal UiElementMaterializer(UiView? view) => _view = view;

	/// <summary>Every cell and structural scope this walk created, so the view can release them all when it
	/// discards this materialization.</summary>
	internal List<UiDependent> Dependents => _dependents;

	/// <summary>Materializes <paramref name="root" /> and links the resulting shadow tree, so every node
	/// knows its parent.</summary>
	internal UiMaterializedNode MaterializeRoot(UiElement root)
	{
		var rootNode = MaterializeSingle(root,
			structuralPrefix: null,
			inputScope: null,
			parentDeclarationPath: string.Empty,
			disabled: null);

		Link(rootNode, parent: null, isFallbackOfParent: false);

		return rootNode;
	}

	/// <summary>
	/// Materializes <paramref name="element" /> into <paramref name="region" />: one node for a structural
	/// element (<see cref="UiContainer" />) or an input (<see cref="UiInput{T}" />), a nested region for a
	/// <see cref="UiWhen" /> or a <see cref="UiRepeat{TItem}" />, and its children straight into
	/// <paramref name="region" /> for a <see cref="UiFragment" />.
	/// </summary>
	internal void MaterializeInto(
		UiElement element,
		UiChildRegion region,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		// The declaration path gains a segment on every recursion, including through the transparent
		// elements, so its depth is the recursion depth - no separate counter has to be threaded through.
		if (Depth(parentDeclarationPath) >= _maxDepth)
		{
			throw new UiViewException($"The element keyed '{element.Key}' nests more than {_maxDepth} levels deep at " +
				$"'{parentDeclarationPath}'. A conditional or repeat whose content closure reaches its own " +
				"element - which a closure capturing the variable it is assigned to does - recurses forever.");
		}

		switch (element)
		{
			case UiWhen when1:
			{
				var scope = CreateStructuralScope(when1, structuralPrefix, inputScope, parentDeclarationPath, disabled);
				var content = region.AddRegion(scope);

				// The condition and the content closure are the structural decision, so they - and only they -
				// are evaluated inside a structural scope. Materializing the content happens outside it, so a
				// property provider's reads are attributed to that property's own cell.
				UiElement? contentElement = null;
				using (UiTracking.Push(scope))
				{
					scope.LastCondition = when1.Condition();

					if (scope.LastCondition)
					{
						contentElement = when1.Content();
					}
				}

				if (contentElement is not null)
				{
					MaterializeInto(contentElement,
						content,
						structuralPrefix,
						inputScope,
						Extend(parentDeclarationPath, when1.Key),
						disabled,
						modifiers: null);
				}

				return;
			}

			case UiFragment fragment:
			{
				var declarationPath = Extend(parentDeclarationPath, fragment.Key);

				foreach (var child in fragment.Children)
				{
					MaterializeInto(child, region, structuralPrefix, inputScope, declarationPath, disabled, null);
				}

				return;
			}

			case IUiRepeatElement repeat:
			{
				var scope = CreateStructuralScope(element, structuralPrefix, inputScope, parentDeclarationPath, disabled);
				var items = region.AddRegion(scope);

				// The templates run inside the structural scope too: an item's element is part of the
				// structural decision, so a state a template reads has to invalidate this scope.
				var expanded = new List<(string ItemKey, UiElement Element)>();
				using (UiTracking.Push(scope))
				{
					expanded.AddRange(EvaluateRepeat(repeat, scope));
				}

				foreach (var (itemKey, itemElement) in expanded)
				{
					MaterializeInto(itemElement,
						items,
						structuralPrefix,
						inputScope,
						Extend(parentDeclarationPath, itemKey),
						disabled,
						modifiers: null);
				}

				return;
			}

			case IUiInputContainerElement inputContainer:
				region.AddNode(MaterializeInputContainer(element,
					inputContainer,
					structuralPrefix,
					inputScope,
					parentDeclarationPath,
					disabled,
					modifiers));

				return;

			case IUiInputElement:
				region.AddNode(MaterializeInput(element,
					structuralPrefix,
					inputScope,
					parentDeclarationPath,
					disabled,
					modifiers));

				return;

			case UiModifier modifier:
				MaterializeModifier(modifier,
					region,
					structuralPrefix,
					inputScope,
					parentDeclarationPath,
					disabled,
					modifiers);

				return;

			case UiContainer container:
				region.AddNode(MaterializeStructural(container,
					container.Children,
					structuralPrefix,
					inputScope,
					parentDeclarationPath,
					disabled,
					modifiers));

				return;

			case UiLeaf leaf:
				region.AddNode(MaterializeStructural(leaf,
					[],
					structuralPrefix,
					inputScope,
					parentDeclarationPath,
					disabled,
					modifiers));

				return;

			default:
				throw new UiViewException(
					$"Unrecognized DSL element type '{element.GetType().FullName}' keyed '{element.Key}' " +
					$"declared near '{parentDeclarationPath}'.");
		}
	}

	/// <summary>Expands <paramref name="repeat" /> and records the decision it produced on
	/// <paramref name="scope" />. Must be called with <paramref name="scope" /> installed as the current
	/// dependent, so the item list, the key selector and the templates are all observed by it.</summary>
	internal static List<(string ItemKey, UiElement Element)> EvaluateRepeat(
		IUiRepeatElement repeat,
		UiStructuralScope scope)
	{
		var expansion = repeat.Expand(key => Compose(scope.InputScope, key));
		var expanded = new List<(string ItemKey, UiElement Element)>(expansion.Items.Count);

		foreach (var item in expansion.Items)
		{
			expanded.Add((item.ItemKey, item.Build()));
		}

		scope.LastItemList = expansion.ItemList;

		return expanded;
	}

	/// <summary>
	/// Materializes <paramref name="element" /> to exactly one node. Used for slots that must produce exactly
	/// one node: the tree root and a <see cref="UiElement.Fallback" />. Throws
	/// <see cref="UiViewException" /> when <paramref name="element" /> materializes to zero or more than
	/// one node - for example a <see cref="UiWhen" /> whose condition is false, or a
	/// <see cref="UiFragment" /> with other than one child.
	/// </summary>
	private UiMaterializedNode MaterializeSingle(
		UiElement element,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled)
	{
		// No owner: a slot that must hold exactly one node has no parent child list, so nothing in it can be
		// inserted, removed or moved. A structural scope declared here reports that on invalidation.
		var slot = new UiChildRegion(parent: null, scope: null);

		MaterializeInto(element, slot, structuralPrefix, inputScope, parentDeclarationPath, disabled, null);

		var nodes = new List<UiMaterializedNode>();
		slot.Flatten(nodes);

		if (nodes.Count != 1)
		{
			throw new UiViewException(
				$"The element keyed '{element.Key}' declared near '{parentDeclarationPath}' must materialize " +
				$"to exactly one node here, but produced {nodes.Count}.");
		}

		return nodes[0];
	}

	private void MaterializeModifier(
		UiModifier modifier,
		UiChildRegion region,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		var child = modifier.Child;
		var where = $"The modifier keyed '{modifier.Key}' declared near '{parentDeclarationPath}'";

		if (modifier.Children.Count != 0)
		{
			throw new UiViewException($"{where} takes its one element through Child, not Children.");
		}

		if (child is UiWhen or UiFragment or IUiRepeatElement)
		{
			throw new UiViewException($"{where} needs a single element as its Child, not a conditional, a " +
				"repeat or a fragment, which can produce other than one node.");
		}

		IReadOnlyList<UiModifier> layers = modifiers is null ? [modifier] : [modifier, .. modifiers];
		IReadOnlyList<UiValue<bool>>? chain = !modifier.Disabled.IsDeclared
			? disabled
			: disabled is null ? [modifier.Disabled] : [.. disabled, modifier.Disabled];

		if (!modifier.IsWrapper)
		{
			if (modifier.MainSize.IsDeclared || modifier.Fill.IsDeclared || modifier.Answer.IsDeclared ||
				modifier.Fallback is not null || modifier.RequiredComponentVersion is not null)
			{
				throw new UiViewException($"{where} sets only node modifiers, so it emits no node that could carry " +
					"MainSize, Fill, Answer, a Fallback or a component version. Set them on its child, or " +
					"assign a wrapper member.");
			}

			MaterializeInto(child,
				region,
				structuralPrefix,
				inputScope,
				Extend(parentDeclarationPath, modifier.Key),
				chain,
				layers);

			return;
		}

		if (SizesItselfInParent(child))
		{
			throw new UiViewException($"{where} is a wrapper, so its child is laid out inside it and cannot set " +
				"MainSize or Fill. Set them on the modifier instead.");
		}

		region.AddNode(MaterializeStructural(modifier,
			[child],
			structuralPrefix,
			inputScope,
			parentDeclarationPath,
			chain,
			layers));
	}

	private static bool SizesItselfInParent(UiElement element)
	{
		while (element is UiModifier { IsWrapper: false } inner)
		{
			element = inner.Child;
		}

		return element switch
		{
			UiComponentContainer container => container.MainSize.IsDeclared || container.Fill.IsDeclared,
			UiComponentLeaf leaf => leaf.MainSize.IsDeclared || leaf.Fill.IsDeclared,
			_ => false,
		};
	}

	/// <summary>Materializes a structural element - a <see cref="UiContainer" /> with children or a
	/// <see cref="UiLeaf" /> without - which composes its id from the structural prefix and opens no input-id
	/// scope.</summary>
	private UiMaterializedNode MaterializeStructural(
		UiElement element,
		IReadOnlyList<UiElement> children,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		var (id, declarationPath) = ComposeAndValidate(structuralPrefix, element.Key, parentDeclarationPath);

		var childRegion = new UiChildRegion(parent: null, scope: null);
		foreach (var child in children)
		{
			MaterializeInto(child, childRegion, id, inputScope, declarationPath, disabled, null);
		}

		var fallback = MaterializeFallback(element, structuralPrefix, inputScope, parentDeclarationPath, disabled);

		return BuildNode(element, id, childRegion, fallback, disabled, modifiers);
	}

	private UiMaterializedNode MaterializeInput(
		UiElement input,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		var (id, _) = ComposeAndValidate(inputScope, input.Key, parentDeclarationPath);
		var fallback = MaterializeFallback(input, structuralPrefix, inputScope, parentDeclarationPath, disabled);

		return BuildNode(input, id, new UiChildRegion(parent: null, scope: null), fallback, disabled, modifiers);
	}

	private UiMaterializedNode MaterializeInputContainer(
		UiElement element,
		IUiInputContainerElement inputContainer,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		var (id, declarationPath) = ComposeAndValidate(inputScope, element.Key, parentDeclarationPath);

		var childRegion = new UiChildRegion(parent: null, scope: null);
		foreach (var child in inputContainer.Children)
		{
			MaterializeInto(child, childRegion, structuralPrefix, id, declarationPath, disabled, null);
		}

		var fallback = MaterializeFallback(element, structuralPrefix, inputScope, parentDeclarationPath, disabled);

		return BuildNode(element, id, childRegion, fallback, disabled, modifiers);
	}

	private UiMaterializedNode? MaterializeFallback(
		UiElement element,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled)
	{
		if (element.Fallback is null)
		{
			return null;
		}

		return MaterializeSingle(element.Fallback,
			structuralPrefix,
			inputScope,
			Extend(parentDeclarationPath, element.Key) + ".fallback",
			disabled);
	}

	private UiMaterializedNode BuildNode(
		UiElement element,
		string id,
		UiChildRegion childRegion,
		UiMaterializedNode? fallback,
		IReadOnlyList<UiValue<bool>>? disabled,
		IReadOnlyList<UiModifier>? modifiers)
	{
		var declaration = new UiPropertyDeclaration();
		element.DeclareProperties(declaration);

		var modifierHandlers = new List<UiEventHandler>();

		if (modifiers is not null)
		{
			DeclareModifiers(declaration, modifiers, id);

			foreach (var layer in modifiers)
			{
				if (!ReferenceEquals(layer, element))
				{
					modifierHandlers.AddRange(layer.Events);
				}
			}
		}

		if (modifierHandlers.Count > 0 || disabled is not null)
		{
			ComposeEvents(declaration.Cells, modifierHandlers, disabled);
		}

		var cells = declaration.Cells;
		var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

		foreach (var cell in cells)
		{
			cell.BindTo(_view);

			var value = cell.EvaluateInitial();
			if (value is not null)
			{
				properties[cell.Key] = value.Value;
			}
		}

		var children = new List<UiMaterializedNode>();
		childRegion.Flatten(children);

		var childNodes = new List<UiNode>(children.Count);
		foreach (var child in children)
		{
			childNodes.Add(child.Node);
		}

		var node = new UiNode
		{
			Id = id,
			Type = element.Type,
			RequiredComponentVersion = element.RequiredComponentVersion,
			Properties = properties,
			Children = childNodes,
			Fallback = fallback?.Node,
		};

		var materialized = new UiMaterializedNode(element, node, childRegion, children, fallback, cells)
		{
			ModifierHandlers = modifierHandlers,
			Disabled = disabled,
		};

		childRegion.Owner = materialized;

		foreach (var cell in cells)
		{
			cell.Attach(materialized);
			_dependents.Add(cell);
		}

		return materialized;
	}

	private static void DeclareModifiers(UiPropertyDeclaration declaration, IReadOnlyList<UiModifier> modifiers, string id)
	{
		var members = new List<UiModifierMember>();

		foreach (var layer in modifiers)
		{
			layer.CollectSafeMembers(members);
		}

		var keys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var member in members)
		{
			if (!keys.Add(member.Key))
			{
				throw new UiViewException($"The node '{id}' is given the modifier '{member.Key}' by more than one " +
					"UiModifier. Assign each modifier once per node.");
			}
		}

		if (members.Count == 0)
		{
			return;
		}

		declaration.Set(UiComponentProperties.Modifiers,
			UiValue.Optional(() =>
			{
				var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

				foreach (var member in members)
				{
					if (member.Evaluate() is { } value)
					{
						values[member.Key] = value;
					}
				}

				return values.Count == 0
					? UiValue.None<IReadOnlyDictionary<string, JsonElement>>()
					: UiValue.Of<IReadOnlyDictionary<string, JsonElement>>(values);
			}));
	}

	// Reading the disabled values inside the cell subscribes it, so a toggle re-emits the events.
	private static void ComposeEvents(
		List<UiPropertyCell> cells,
		List<UiEventHandler> modifierHandlers,
		IReadOnlyList<UiValue<bool>>? disabled)
	{
		var index = cells.FindIndex(cell => string.Equals(cell.Key, UiComponentProperties.Events, StringComparison.Ordinal));
		var declared = index >= 0
			? ((UiPropertyCell<IReadOnlyList<string>>)cells[index]).Value
			: UiValue.None<IReadOnlyList<string>>();

		var composed = UiValue.Optional(() =>
		{
			if (UiMaterializedNode.IsAnyDisabled(disabled))
			{
				return UiValue.None<IReadOnlyList<string>>();
			}

			var names = declared.TryEvaluate(out var own) ? new List<string>(own) : [];

			foreach (var handler in modifierHandlers)
			{
				if (!names.Contains(handler.Name, StringComparer.Ordinal))
				{
					names.Add(handler.Name);
				}
			}

			return names.Count == 0
				? UiValue.None<IReadOnlyList<string>>()
				: UiValue.Of<IReadOnlyList<string>>(names);
		});

		var replacement = new UiPropertyCell<IReadOnlyList<string>>(UiComponentProperties.Events, composed);

		if (index >= 0)
		{
			cells[index] = replacement;
		}
		else
		{
			cells.Add(replacement);
		}
	}

	private UiStructuralScope CreateStructuralScope(
		UiElement element,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath,
		IReadOnlyList<UiValue<bool>>? disabled)
	{
		var scope = new UiStructuralScope(element, structuralPrefix, inputScope, parentDeclarationPath)
		{
			Disabled = disabled,
		};

		scope.BindTo(_view);
		_dependents.Add(scope);

		return scope;
	}

	/// <summary>Fills in the parent links the flush walks upwards. A separate pass because a child is
	/// materialized before the parent that owns it exists.</summary>
	internal static void Link(UiMaterializedNode node, UiMaterializedNode? parent, bool isFallbackOfParent)
	{
		node.Parent = parent;
		node.IsFallbackOfParent = isFallbackOfParent;

		foreach (var child in node.Children)
		{
			Link(child, node, isFallbackOfParent: false);
		}

		if (node.Fallback is not null)
		{
			Link(node.Fallback, node, isFallbackOfParent: true);
		}
	}

	/// <summary>Composes <paramref name="key" /> against <paramref name="prefix" />, validates the result
	/// and registers it. Returns the id and the diagnostic declaration path it was registered under.
	/// Throws <see cref="UiViewException" /> naming the composed id when it fails
	/// <see cref="UiIdentifier.TryValidate" />; validation is of the composed id, never of the bare key.
	/// </summary>
	private (string Id, string DeclarationPath) ComposeAndValidate(
		string? prefix,
		string key,
		string parentDeclarationPath)
	{
		var id = Compose(prefix, key);

		if (!UiIdentifier.TryValidate(id, out var error))
		{
			throw new UiViewException($"The key '{key}' composed to id '{id}' is not a valid node id: {error}");
		}

		var declarationPath = Extend(parentDeclarationPath, key);
		_registry.Register(id, declarationPath);

		return (id, declarationPath);
	}

	/// <summary>Composes <paramref name="key" /> against an id prefix, which is a plain dot join.</summary>
	internal static string Compose(string? prefix, string key) => prefix is null ? key : $"{prefix}.{key}";

	/// <summary>Extends a diagnostic declaration path by one key.</summary>
	internal static string Extend(string parentPath, string key)
		=> parentPath.Length == 0 ? key : $"{parentPath}.{key}";

	/// <summary>Counts the segments in a declaration path, which is the current recursion depth.</summary>
	private static int Depth(string declarationPath)
	{
		if (declarationPath.Length == 0)
		{
			return 0;
		}

		var depth = 1;

		foreach (var character in declarationPath)
		{
			if (character == '.')
			{
				depth++;
			}
		}

		return depth;
	}
}
