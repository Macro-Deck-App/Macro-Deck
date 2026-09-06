using System.Text.RegularExpressions;

namespace MacroDeck.Ui.Model.Identity;

/// <summary>
/// Validates a node id or a resource id: an ASCII alphanumeric character followed by up to 127 more
/// ASCII alphanumeric characters, dots, hyphens or underscores. Carried as a plain <c>string</c> on
/// every record rather than a wrapper type, so a bad id is a validation failure at the boundary, never
/// a deserializer exception - the same reasoning the plugin protocol's <c>PluginSessionId</c> applies,
/// for the same reason. This package has no reference to that package; the precedent is documentation,
/// not a dependency.
///
/// <para>
/// One grammar serves both node ids and resource ids. It is deliberately not composed with the
/// plugin protocol's qualified capability identity (ADR 0004): a node id is author-assigned and
/// session-local, so qualification adds no uniqueness on the hottest path in the system, and the
/// kebab-only grammar there would make a generated id such as <c>item.3</c> or <c>field.apiKey</c>
/// lossy, destroying the stability node ids exist to have. <c>:</c> is excluded so a node id can never
/// contain ADR 0004's reserved <c>::</c> separator.
/// </para>
///
/// <para>
/// Stability is a contract stated here for <see cref="Nodes.UiNode.Id" />: a producer emits the same
/// id for the same logical node on every rebuild, the model addresses nodes by id and never by
/// position, and an id derived from array position is a defect.
/// </para>
/// </summary>
public static partial class UiIdentifier
{
	/// <summary>The longest value <see cref="IsValid" /> accepts.</summary>
	public const int MaxLength = 128;

	[GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)]
	private static partial Regex Pattern();

	/// <summary>True when <paramref name="value" /> matches the grammar. Never throws, including on
	/// <c>null</c>.</summary>
	public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

	/// <summary>Validates <paramref name="value" /> and reports why it failed. <paramref name="error" />
	/// is non-null exactly when the return value is <c>false</c>.</summary>
	public static bool TryValidate(string? value, out string? error)
	{
		if (IsValid(value))
		{
			error = null;
			return true;
		}

		error = value switch
		{
			null => "The identifier is null.",
			{ Length: 0 } => "The identifier is empty.",
			{ Length: > MaxLength } => $"The identifier is longer than {MaxLength} characters.",
			_ => "The identifier does not match the required grammar: an ASCII alphanumeric character " +
				"followed by ASCII alphanumeric characters, dots, hyphens or underscores.",
		};

		return false;
	}
}
