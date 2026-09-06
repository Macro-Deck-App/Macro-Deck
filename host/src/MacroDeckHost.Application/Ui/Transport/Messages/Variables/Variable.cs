using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Variables;

public class Variable
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Readable label for <see cref="Name"/>, empty when the owning provider declares none. Sent unresolved:
	/// the client renders it in the reader's language.
	/// </summary>
	public LocalizedText DisplayName { get; set; }

	/// <summary>Identifies the configured instance this variable belongs to, within its integration.</summary>
	public string? ConfigurationKey { get; set; }

	/// <summary>Label for <see cref="ConfigurationKey"/>'s group.</summary>
	public LocalizedText ConfigurationName { get; set; }

	public string Scope { get; set; } = "global";

	public string? ScopeRefId { get; set; }

	public string Type { get; set; } = "text";

	public string Classification { get; set; } = "user";

	public string? OwnerIntegrationId { get; set; }

	public string Value { get; set; } = string.Empty;

	public int? DecimalPlaces { get; set; }

	/// <summary>The unit symbol the value is expressed in - <c>%</c>, <c>GB</c>, <c>dB</c> - or null when
	/// the variable is a bare count.</summary>
	public string? Unit { get; set; }

	/// <summary>How the value should be rendered beyond its unit. An open string: a kind the client does
	/// not recognise is shown as a plain number with its unit.</summary>
	public string? SemanticKind { get; set; }

	/// <summary>Provider-defined attributes, uninterpreted by the host.</summary>
	public IReadOnlyDictionary<string, string>? Attributes { get; set; }

	/// <summary>The range a control should offer for this variable right now, when its owner reports one.
	/// Volatile - a seek position's maximum changes with the track - so a client re-reads it on change
	/// rather than caching it with the definition.</summary>
	public double? Min { get; set; }

	public double? Max { get; set; }

	public double? Step { get; set; }

	/// <summary>Whether a client may offer an editing control at all. Decided by the owner's declaration,
	/// not by the classification: an integration variable whose provider accepts writes is editable and a
	/// widget-owned one never is.</summary>
	public bool CanWrite { get; set; }

	/// <summary>Whether a continuous control should send the value once the user lets go instead of
	/// streaming intermediate values while dragging.</summary>
	public bool CommitOnRelease { get; set; }

	public bool Available { get; set; } = true;

	/// <summary>The catalog resource id this variable is bound to, set only when it was created by binding
	/// one. Lets a variable row offer Unbind without a second round trip.</summary>
	public string? DynamicResourceId { get; set; }
}
