using System.Globalization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// A headless renderer for a <see cref="UiView" />: render an authored element tree, query the result by id,
/// type or text, raise events on it, wait for asynchronous work to settle, and read the patches the view
/// produced. No host process, no browser, no renderer - a plugin's configuration surface is testable with this
/// package, <c>MacroDeck.Ui</c> and <c>MacroDeck.Ui.Model</c> and nothing else.
///
/// <para>
/// <b>Every query implicitly verifies the producer.</b> The host keeps its own copy of the tree and, after every
/// drain, applies each patch to that copy through <see cref="UiTreeApplier" />. A patch the applier rejects, or
/// an applied result whose canonical JSON differs from the view's own tree, throws
/// <see cref="UiTestAssertionException" /> on the spot. So <see cref="Tree" /> is the <i>applied</i> tree, and
/// every assertion in every test also asserts that the producer emits only patches it could apply itself -
/// which is the single highest-value property this package provides and the one no individual test would
/// otherwise state.
/// </para>
///
/// <para>
/// <b>No assertion helpers, deliberately.</b> This is a published package: referencing a test framework would
/// force every consumer onto it, and a helper that re-describes what a framework's own constraints already
/// assert earns nothing. What the host does own is failing loudly when the thing a test is about to assert on
/// is not there - see <see cref="ById" /> - and bounding every wait, because the runtime owns no clock.
/// </para>
/// </summary>
public sealed class UiTestHost
{
	/// <summary>
	/// How long <see cref="SettleAsync" /> waits when the caller names no budget. Bounded rather than infinite:
	/// a stuck loader has to fail the test that hit it, not hang the run.
	/// </summary>
	private static readonly TimeSpan _defaultSettleTimeout = TimeSpan.FromSeconds(5);

	/// <summary>The property keys <see cref="ByText" /> searches, in the order it searches them.</summary>
	private static readonly string[] _textBearingKeys =
	[
		UiConfigProperties.Text,
		UiConfigProperties.Label,
		UiConfigProperties.Title,
		UiConfigProperties.Description,
		UiConfigProperties.ValidationMessage,
	];

	private readonly Dictionary<string, UiTestNode> _byId = new(StringComparer.Ordinal);
	private readonly List<UiTestNode> _documentOrder = [];
	private readonly List<UiPatch> _patches = [];
	private readonly UiView _view;
	private UiTree _appliedTree;
	private UiTestNode? _root;

	private UiTestHost(UiView view)
	{
		_view = view;
		_appliedTree = view.Tree;

		// A render that flushed - a value provider that started the load it is waiting for - already produced
		// the tree the host is holding, so the patch describing the way there has nothing left to apply to.
		// Starting with an empty patch list is the same contract ClearPatches offers afterwards.
		view.DrainPatches();
	}

	/// <summary>Renders <paramref name="root" /> on a configuration surface in an exclusive session - the
	/// ordinary case for a plugin's configuration flow.</summary>
	public static UiTestHost Render(UiElement root)
		=> Render(root, new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive });

	/// <summary>Renders <paramref name="root" /> on <paramref name="surface" />.</summary>
	public static UiTestHost Render(UiElement root, UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(surface);

		return new UiTestHost(new UiView(surface, root));
	}

	/// <summary>Hosts <paramref name="view" />, which the caller built itself - for a test that has to hold the
	/// view to subscribe to <see cref="UiView.HandlerFaulted" /> or to open a batch of its own.</summary>
	public static UiTestHost Render(UiView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		return new UiTestHost(view);
	}

	/// <summary>The view being hosted.</summary>
	public UiView View => _view;

	/// <summary>The revision <see cref="Tree" /> is at.</summary>
	public int Revision
	{
		get
		{
			Sync();

			return _appliedTree.Revision;
		}
	}

	/// <summary>The tree, as reconstructed by applying every patch the view produced - see this type's
	/// remarks.</summary>
	public UiTree Tree
	{
		get
		{
			Sync();

			return _appliedTree;
		}
	}

	/// <summary>Every patch the view produced since the last <see cref="ClearPatches" />, in order.</summary>
	public IReadOnlyList<UiPatch> Patches
	{
		get
		{
			Sync();

			return _patches.ToArray();
		}
	}

	/// <summary>The most recent patch.</summary>
	/// <exception cref="UiTestAssertionException">Nothing has been produced since the last
	/// <see cref="ClearPatches" />.</exception>
	public UiPatch LastPatch
	{
		get
		{
			Sync();

			if (_patches.Count == 0)
			{
				throw new UiTestAssertionException(
					$"No patch has been produced since the last ClearPatches; the view is at revision " +
					$"{_appliedTree.Revision.ToString(CultureInfo.InvariantCulture)}.");
			}

			return _patches[^1];
		}
	}

	/// <summary>The tree's root.</summary>
	public UiTestNode Root
	{
		get
		{
			EnsureIndex();

			return _root!;
		}
	}

	/// <summary>Forgets the patches produced so far, so the next assertion sees only what the next change
	/// produces. The tree is untouched - the host keeps applying to what it already has.</summary>
	public void ClearPatches()
	{
		Sync();
		_patches.Clear();
	}

	/// <summary>
	/// Waits until every asynchronous handler and every load the view started has finished, then verifies the
	/// patches that resulted.
	/// </summary>
	/// <param name="timeout">How long to wait, or <c>null</c> for the host's own bounded default.</param>
	/// <exception cref="UiTestTimeoutException">The view was still busy when the budget ran out.</exception>
	/// <exception cref="UiTestAssertionException">A handler faulted while settling, or a patch produced while
	/// settling does not apply.</exception>
	public async Task SettleAsync(TimeSpan? timeout = null)
	{
		var budget = timeout ?? _defaultSettleTimeout;

		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);

		var faults = new List<UiHandlerFaultEventArgs>();

		void OnFaulted(object? sender, UiHandlerFaultEventArgs args) => faults.Add(args);

		using var cancellation = new CancellationTokenSource(budget);

		_view.HandlerFaulted += OnFaulted;

		try
		{
			await _view.WhenIdleAsync(cancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException exception)
		{
			throw new UiTestTimeoutException("The view had not settled after " +
				$"{budget.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms: " +
				$"{_view.PendingWorkCount.ToString(CultureInfo.InvariantCulture)} piece(s) of asynchronous work " +
				"registered on it - an event handler or a load started at or before revision " +
				$"{_appliedTree.Revision.ToString(CultureInfo.InvariantCulture)} - never completed. Complete the " +
				"work the test owns, or raise the settle budget if the work is genuinely that slow.",
				exception);
		}
		finally
		{
			_view.HandlerFaulted -= OnFaulted;
		}

		Sync();

		if (faults.Count > 0)
		{
			var first = faults[0];

			throw new UiTestAssertionException(
				$"The '{first.EventName}' handler on '{first.NodeId}' faulted while settling: " +
				$"{first.Exception.GetType().Name}: {first.Exception.Message}",
				first.Exception);
		}
	}

	/// <summary>The node with <paramref name="id" />.</summary>
	/// <exception cref="UiTestAssertionException">No node has that id. The message names the id and the closest
	/// candidates, so a mistyped or wrongly composed id reads as itself rather than as a
	/// <see cref="NullReferenceException" /> in whatever asserted on the result.</exception>
	public UiTestNode ById(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);

		if (FindById(id) is { } node)
		{
			return node;
		}

		throw new UiTestAssertionException($"No node with id '{id}' is in the tree at revision " +
			$"{_appliedTree.Revision.ToString(CultureInfo.InvariantCulture)}. {DescribeNearMisses(id)}");
	}

	/// <summary>The node with <paramref name="id" />, or <c>null</c> - the probing counterpart of
	/// <see cref="ById" />, for a test whose expectation is that the node is absent.</summary>
	public UiTestNode? FindById(string id)
	{
		ArgumentException.ThrowIfNullOrEmpty(id);

		EnsureIndex();

		return _byId.GetValueOrDefault(id);
	}

	/// <summary>Every node whose <see cref="UiTestNode.Type" /> is <paramref name="type" />, in document order -
	/// a pre-order walk of each node, then its children in render order, then its fallback subtree.</summary>
	public IReadOnlyList<UiTestNode> ByType(string type)
	{
		ArgumentException.ThrowIfNullOrEmpty(type);

		EnsureIndex();

		var matches = new List<UiTestNode>();

		foreach (var node in _documentOrder)
		{
			if (string.Equals(node.Type, type, StringComparison.Ordinal))
			{
				matches.Add(node);
			}
		}

		return matches;
	}

	/// <summary>The one node of <paramref name="type" />.</summary>
	/// <exception cref="UiTestAssertionException">No node or more than one node has that type.</exception>
	public UiTestNode SingleByType(string type)
	{
		var matches = ByType(type);

		if (matches.Count == 1)
		{
			return matches[0];
		}

		var found = matches.Count == 0
			? "none"
			: string.Join(", ", matches.Select(node => node.Id));

		throw new UiTestAssertionException($"Expected exactly one node of type '{type}' in the tree at revision " +
			$"{_appliedTree.Revision.ToString(CultureInfo.InvariantCulture)}, found " +
			$"{matches.Count.ToString(CultureInfo.InvariantCulture)}: {found}.");
	}

	/// <summary>
	/// Every node carrying <paramref name="text" /> as an ordinal substring of one of its text-bearing
	/// properties, in document order. Those properties are exactly <c>text</c>, <c>label</c>, <c>title</c>,
	/// <c>description</c> and <c>validationMessage</c>.
	///
	/// <para>
	/// Ordinal and substring on purpose: culture-aware matching would make a test's result depend on the
	/// machine's locale, and an exact match would not find the message a test knows only part of.
	/// </para>
	/// </summary>
	public IReadOnlyList<UiTestNode> ByText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		EnsureIndex();

		var matches = new List<UiTestNode>();

		foreach (var node in _documentOrder)
		{
			foreach (var key in _textBearingKeys)
			{
				if (node.Text(key) is { } value && value.Contains(text, StringComparison.Ordinal))
				{
					matches.Add(node);

					break;
				}
			}
		}

		return matches;
	}

	/// <summary>The tree as indented, diffable plain text - see <see cref="UiTreeText" /> for the
	/// grammar.</summary>
	public string Describe() => UiTreeText.Render(Tree);

	/// <summary>The patches since the last <see cref="ClearPatches" /> as indented plain text - see
	/// <see cref="UiPatchText" /> for the grammar.</summary>
	public string DescribePatches() => UiPatchText.Render(Patches);

	/// <summary>The tree in canonical JSON - the exact bytes a renderer would receive.</summary>
	public string ToCanonicalJson() => UiCanonicalJson.Serialize(Tree);

	/// <summary>Dispatches an event on behalf of <see cref="UiTestNode.Raise" />, verifying the patches it
	/// produced before returning.</summary>
	internal UiDispatchResult RaiseEvent(string nodeId, string name, object? data)
	{
		Sync();

		var result = _view.Dispatch(new UiEvent
		{
			NodeId = nodeId,
			Name = name,
			Data = data is null ? null : UiCanonicalJson.ToElement(data),
			Revision = _appliedTree.Revision,
		});

		Sync();

		return result;
	}

	/// <summary>Drains whatever the view has produced and folds it onto the host's own tree, failing rather
	/// than letting an unapplicable patch pass unnoticed - see this type's remarks.</summary>
	private void Sync()
	{
		var drained = _view.DrainPatches();

		if (drained.Count == 0)
		{
			return;
		}

		foreach (var patch in drained)
		{
			var result = UiTreeApplier.Apply(_appliedTree, patch);

			if (!result.IsApplied)
			{
				throw new UiTestAssertionException("The view produced a patch it could not apply itself. The patch " +
					$"{patch.FromRevision.ToString(CultureInfo.InvariantCulture)}->" +
					$"{patch.ToRevision.ToString(CultureInfo.InvariantCulture)} was rejected: " +
					$"{result.RejectionReason}\n{UiPatchText.Render([patch])}");
			}

			_appliedTree = result.Tree;
			_patches.Add(patch);
		}

		var applied = UiCanonicalJson.Serialize(_appliedTree);
		var produced = UiCanonicalJson.Serialize(_view.Tree);

		if (!string.Equals(applied, produced, StringComparison.Ordinal))
		{
			throw new UiTestAssertionException(
				"Applying the view's own patches did not reproduce the view's own tree, so the patch stream and " +
				$"the producer disagree at revision {_appliedTree.Revision.ToString(CultureInfo.InvariantCulture)}." +
				$"\napplied:  {applied}\nproduced: {produced}");
		}

		_root = null;
	}

	/// <summary>Rebuilds the queryable node index when the tree moved on.</summary>
	private void EnsureIndex()
	{
		Sync();

		if (_root is not null)
		{
			return;
		}

		_byId.Clear();
		_documentOrder.Clear();
		_root = BuildNode(_appliedTree.Root, parent: null, parentPath: null);
	}

	private UiTestNode BuildNode(UiNode node, UiTestNode? parent, string? parentPath)
	{
		var path = parentPath is null ? node.Id : $"{parentPath}/{node.Id}";
		var wrapper = new UiTestNode(this, node, parent, path);

		_documentOrder.Add(wrapper);

		// Uniqueness is the producer's obligation and the DSL enforces it, so a duplicate here can only come from
		// a hand-written tree. First wins, and the walk still lists both.
		_byId.TryAdd(node.Id, wrapper);

		var children = new List<UiTestNode>(node.Children.Count);

		foreach (var child in node.Children)
		{
			children.Add(BuildNode(child, wrapper, path));
		}

		wrapper.SetChildren(children);

		if (node.Fallback is not null)
		{
			wrapper.SetFallback(BuildNode(node.Fallback, wrapper, path));
		}

		return wrapper;
	}

	/// <summary>The ids worth mentioning when <paramref name="id" /> was not found: the ones that contain it or
	/// are contained by it, and otherwise whatever the tree does have.</summary>
	private string DescribeNearMisses(string id)
	{
		var candidates = new List<string>();

		foreach (var node in _documentOrder)
		{
			if (node.Id.Contains(id, StringComparison.OrdinalIgnoreCase) ||
				id.Contains(node.Id, StringComparison.OrdinalIgnoreCase))
			{
				candidates.Add(node.Id);
			}
		}

		if (candidates.Count > 0)
		{
			return $"Did you mean {string.Join(", ", candidates)}?";
		}

		foreach (var node in _documentOrder)
		{
			candidates.Add(node.Id);
		}

		const int shown = 12;

		return candidates.Count <= shown
			? $"The tree has {string.Join(", ", candidates)}."
			: $"The tree has {string.Join(", ", candidates.Take(shown))} and " +
			$"{(candidates.Count - shown).ToString(CultureInfo.InvariantCulture)} more.";
	}
}
