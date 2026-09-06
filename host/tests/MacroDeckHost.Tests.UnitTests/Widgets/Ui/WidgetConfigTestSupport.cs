using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Testing;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// What every widget configuration test needs and no single widget owns: reading a node's
/// <c>visibleWhen</c> the same way a renderer would, against the sibling it names. <see cref="UiTestHost" />
/// deliberately carries no assertion helpers of its own (it is a published package - see its own remarks),
/// so this lives beside the tests that need it rather than in that package.
/// </summary>
internal static class WidgetConfigTestSupport
{
	/// <summary>
	/// Whether the user would see <paramref name="nodeId" /> right now, which is the question a test about a
	/// conditional field is actually asking. A field is unseen either because it is not in the tree at all -
	/// a provider may decide a field does not apply and simply not emit it - or because it carries a
	/// <c>visibleWhen</c> the renderer does not satisfy. Both are answered here so a test states the rule
	/// rather than the encoding a provider happened to choose for it.
	///
	/// <para>
	/// The <c>visibleWhen</c> half mirrors the renderer's own rule: no condition, or one naming a sibling
	/// that is not in the tree, means visible; otherwise the sibling's own current value has to be one of
	/// the declared values, compared as a string, ignoring case - exactly
	/// <c>ui/angular/.../parameter-visibility.util.ts</c>'s rule for the action-parameter counterpart.
	/// </para>
	/// </summary>
	public static bool IsVisible(UiTestHost host, string nodeId)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentException.ThrowIfNullOrEmpty(nodeId);

		var node = host.FindById(nodeId);

		if (node is null)
		{
			return false;
		}

		var condition = node.Property(UiConfigProperties.VisibleWhen);

		if (condition is not { ValueKind: JsonValueKind.Object } declared)
		{
			return true;
		}

		var parameterName = declared.GetProperty("parameterName").GetString();
		var sibling = parameterName is null ? null : host.FindById(parameterName);

		if (sibling is null)
		{
			return true;
		}

		var current = SiblingText(sibling.Property(UiConfigProperties.Value));
		var values = declared.GetProperty("values").EnumerateArray().Select(value => value.GetString() ?? string.Empty);

		return values.Any(value => string.Equals(value, current, StringComparison.OrdinalIgnoreCase));
	}

	private static string SiblingText(JsonElement? value) => value?.ValueKind switch
	{
		JsonValueKind.String => value.Value.GetString() ?? string.Empty,
		JsonValueKind.True => "true",
		JsonValueKind.False => "false",
		JsonValueKind.Number => value.Value.GetRawText(),
		_ => string.Empty,
	};
}
