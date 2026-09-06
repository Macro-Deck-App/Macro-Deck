using System.Diagnostics.CodeAnalysis;

namespace MacroDeckHost.Application.Plugins.Trust;

/// <summary>The only two strings a <c>PluginTrustRecordEntity.AdmittedVerdict</c> ever carries - a
/// signature failure never reaches installation, so nothing else is ever admitted.</summary>
public static class PluginTrustRecordVerdicts
{
	public const string Trusted = "Trusted";

	[SuppressMessage("Naming",
		"CA1720:Identifier contains type name",
		Justification = "'Unsigned' names the trust tier, not the numeric type - see PluginTrustVerdict.")]
	public const string Unsigned = "Unsigned";

	public static string From(PluginTrustVerdict verdict)
		=> verdict == PluginTrustVerdict.Trusted ? Trusted : Unsigned;

	public static PluginTrustVerdict? Parse(string? admittedVerdict) => admittedVerdict switch
	{
		Trusted => PluginTrustVerdict.Trusted,
		Unsigned => PluginTrustVerdict.Unsigned,
		_ => null
	};
}
