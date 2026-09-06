using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// One node of the tree a <see cref="UiTestHost" /> rendered, as a test reads it: its identity, its place in
/// the tree, the properties it carries, and the events a client could raise on it.
///
/// <para>
/// <b>A node is a snapshot.</b> It describes the tree at the revision it was queried from, so a test asserts on
/// the node it just fetched and fetches it again after a dispatch or a settle. Reading a stale node is never
/// wrong-but-silent - <see cref="UiTestHost.Revision" /> says which revision the host is at - but it is stale,
/// and re-querying is one call.
/// </para>
///
/// <para>
/// The value readers are all nullable-returning rather than throwing, because the model treats a property value
/// as unvalidated data: a property documented as a number can arrive as a JSON string, so
/// <see cref="Number" /> answering <c>null</c> for a string is the honest reading of what is there rather than
/// an exception about what should have been.
/// </para>
/// </summary>
public sealed class UiTestNode
{
	private readonly UiTestHost _host;
	private readonly UiNode _node;
	private IReadOnlyList<UiTestNode> _children = [];

	internal UiTestNode(UiTestHost host, UiNode node, UiTestNode? parent, string path)
	{
		_host = host;
		_node = node;
		Parent = parent;
		Path = path;

		var keys = new List<string>(node.Properties.Keys);
		keys.Sort(StringComparer.Ordinal);
		PropertyKeys = keys;
	}

	/// <summary>The node's id - for an input, the field name it submits as.</summary>
	public string Id => _node.Id;

	/// <summary>The node's component type name, for example <c>string</c> or <c>advanced-section</c>.</summary>
	public string Type => _node.Type;

	/// <summary>The component version this node declared, or <c>null</c> for version 1.</summary>
	public int? RequiredComponentVersion => _node.RequiredComponentVersion;

	/// <summary>
	/// The chain of ids from the root to this node, joined with <c>/</c> - the chrome path, for diagnostics
	/// only.
	///
	/// <para>
	/// Deliberately not the same thing as <see cref="Id" />: an input's id is its bare key with no chrome
	/// prefix, so the path is the only way to say where in the chrome a field with a flat id actually sits.
	/// Nothing on the wire carries it.
	/// </para>
	/// </summary>
	public string Path { get; }

	/// <summary>The node this one hangs under, or <c>null</c> for the root. A node inside a
	/// <see cref="Fallback" /> subtree reports the node it is the fallback for.</summary>
	public UiTestNode? Parent { get; }

	/// <summary>The node's children, in render order.</summary>
	public IReadOnlyList<UiTestNode> Children => _children;

	/// <summary>What a renderer shows in this node's place when its component is unsupported, or
	/// <c>null</c>.</summary>
	public UiTestNode? Fallback { get; private set; }

	/// <summary>The property keys this node carries, sorted <see cref="StringComparer.Ordinal" />
	/// ascending.</summary>
	public IReadOnlyList<string> PropertyKeys { get; }

	/// <summary>Whether <paramref name="key" /> is present. A present key whose value is JSON null counts as
	/// present: the model defines that as "explicitly null", which is a different thing from absent.</summary>
	public bool HasProperty(string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);

		return _node.Properties.ContainsKey(key);
	}

	/// <summary>The raw value of <paramref name="key" />, or <c>null</c> when the key is absent.</summary>
	public JsonElement? Property(string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);

		return _node.Properties.TryGetValue(key, out var value) ? value : null;
	}

	/// <summary>The value of <paramref name="key" /> when it is a JSON string, otherwise <c>null</c>.</summary>
	public string? Text(string key)
		=> Property(key) is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

	/// <summary>The value of <paramref name="key" /> when it is a JSON boolean, otherwise <c>null</c>.</summary>
	public bool? Flag(string key)
		=> Property(key) is { } element && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: null;

	/// <summary>The value of <paramref name="key" /> when it is a JSON number, otherwise <c>null</c>.</summary>
	public double? Number(string key)
		=> Property(key) is { ValueKind: JsonValueKind.Number } element && element.TryGetDouble(out var value)
			? value
			: null;

	/// <summary>
	/// Raises <paramref name="name" /> on this node exactly as a renderer would: <paramref name="data" /> is
	/// serialized through <see cref="Model.Serialization.UiCanonicalJson.ToElement{T}" /> into the event's
	/// payload and the event carries the host's current revision, so a test exercises the same decode path a
	/// real client's event takes rather than a shortcut around it. A <c>null</c> <paramref name="data" /> means
	/// the event carries no payload at all, which is what an event such as <c>submit</c> sends.
	/// </summary>
	/// <returns>What the view answered - accepted, ignored, or rejected with a reason.</returns>
	public UiDispatchResult Raise(string name, object? data = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		return _host.RaiseEvent(Id, name, data);
	}

	/// <summary>Raises <c>change</c> carrying <paramref name="value" /> - the event that writes through this
	/// input's binding.</summary>
	public UiDispatchResult Change(object? value) => Raise(UiConfigEvents.Change, value);

	/// <summary>Raises <c>activate</c> - what following a link is.</summary>
	public UiDispatchResult Activate() => Raise(UiConfigEvents.Activate);

	/// <summary>Raises <c>submit</c> - what a step's or a flow's continue affordance is.</summary>
	public UiDispatchResult Submit() => Raise(UiConfigEvents.Submit);

	/// <summary>The model node this wraps, so the host can render and compare without a second walk.</summary>
	internal UiNode Node => _node;

	internal void SetChildren(IReadOnlyList<UiTestNode> children) => _children = children;

	internal void SetFallback(UiTestNode fallback) => Fallback = fallback;
}
