using System.Globalization;
using System.Text;
using MacroDeck.Ui.Model.Patches;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// Renders a patch sequence as indented plain text - the form behind
/// <see cref="UiTestHost.DescribePatches" />.
///
/// <para>
/// The grammar: one <c>patch from-&gt;to</c> header per patch, then one indented line per operation reading
/// <c>op nodeId</c> followed by the fields that operation carries - <c>parent=</c>, <c>index=</c>,
/// <c>node=type</c>, one <c>+key=value</c> per set property with the keys sorted
/// <see cref="StringComparer.Ordinal" /> ascending, and one <c>-key</c> per removed property. A removed
/// property is spelled differently from a set one on purpose: the model defines a JSON null value as
/// "explicitly null" rather than removed, and a diagnostic that rendered them alike would hide exactly that
/// confusion.
/// </para>
/// </summary>
internal static class UiPatchText
{
	/// <summary>Renders <paramref name="patches" /> in order, each line terminated with <c>\n</c>.</summary>
	internal static string Render(IReadOnlyList<UiPatch> patches)
	{
		var builder = new StringBuilder();

		foreach (var patch in patches)
		{
			builder
				.Append("patch ")
				.Append(patch.FromRevision.ToString(CultureInfo.InvariantCulture))
				.Append("->")
				.Append(patch.ToRevision.ToString(CultureInfo.InvariantCulture))
				.Append('\n');

			foreach (var operation in patch.Operations)
			{
				Append(builder, operation);
			}
		}

		return builder.ToString();
	}

	private static void Append(StringBuilder builder, UiPatchOperation operation)
	{
		builder.Append("  ").Append(operation.Op).Append(' ').Append(operation.NodeId);

		if (operation.ParentId is { } parentId)
		{
			builder.Append(" parent=").Append(parentId);
		}

		if (operation.Index is { } index)
		{
			builder.Append(" index=").Append(index.ToString(CultureInfo.InvariantCulture));
		}

		if (operation.Node is { } node)
		{
			builder.Append(" node=").Append(node.Type);
		}

		if (operation.Properties is { } properties)
		{
			var keys = new List<string>(properties.Keys);
			keys.Sort(StringComparer.Ordinal);

			foreach (var key in keys)
			{
				builder.Append(" +").Append(key).Append('=').Append(properties[key].GetRawText());
			}
		}

		if (operation.RemovedProperties is { } removed)
		{
			var keys = new List<string>(removed);
			keys.Sort(StringComparer.Ordinal);

			foreach (var key in keys)
			{
				builder.Append(" -").Append(key);
			}
		}

		builder.Append('\n');
	}
}
