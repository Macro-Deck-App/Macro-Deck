using System.Globalization;
using System.Text;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// Renders a <see cref="UiTree" /> as indented plain text - the form behind
/// <see cref="UiTestHost.Describe" />.
///
/// <para>
/// The grammar, one line per node: two spaces of indent per level, then <c>type#id</c>, then <c>@version</c>
/// when the node declared a <see cref="UiNode.RequiredComponentVersion" />, then one <c> key=value</c> pair per
/// property with the keys sorted <see cref="StringComparer.Ordinal" /> ascending and each value written as its
/// raw JSON. The <c>events</c> property is what carries the event names a node accepts, so it appears among
/// them in its sorted position. A <see cref="UiNode.Fallback" /> subtree is rendered one level in, after the
/// children, with its first line prefixed <c>fallback </c>.
/// </para>
///
/// <para>
/// Readable and diffable is the whole point: the canonical JSON form is a single line of bytes, which answers
/// "are these two trees identical" and nothing about where they differ.
/// </para>
/// </summary>
internal static class UiTreeText
{
	/// <summary>Renders <paramref name="tree" />'s nodes, one per line, each line terminated with
	/// <c>\n</c>.</summary>
	internal static string Render(UiTree tree)
	{
		var builder = new StringBuilder();

		Append(builder, tree.Root, depth: 0, isFallback: false);

		return builder.ToString();
	}

	private static void Append(StringBuilder builder, UiNode node, int depth, bool isFallback)
	{
		builder.Append(' ', depth * 2);

		if (isFallback)
		{
			builder.Append("fallback ");
		}

		builder.Append(node.Type).Append('#').Append(node.Id);

		if (node.RequiredComponentVersion is { } version)
		{
			builder.Append('@').Append(version.ToString(CultureInfo.InvariantCulture));
		}

		var keys = new List<string>(node.Properties.Keys);
		keys.Sort(StringComparer.Ordinal);

		foreach (var key in keys)
		{
			builder.Append(' ').Append(key).Append('=').Append(node.Properties[key].GetRawText());
		}

		builder.Append('\n');

		foreach (var child in node.Children)
		{
			Append(builder, child, depth + 1, isFallback: false);
		}

		if (node.Fallback is not null)
		{
			Append(builder, node.Fallback, depth + 1, isFallback: true);
		}
	}
}
