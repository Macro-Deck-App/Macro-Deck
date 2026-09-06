using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.Migration;

/// <summary>
/// The result of the <c>migration</c> capability's <c>describe</c> operation: which applications this
/// plugin takes a setup over from, and what it claims in each.
/// </summary>
/// <remarks>
/// A plugin declares this once, so the host can answer "which migrations does this integration support"
/// without invoking anything - the same question ADR 0004's declared/available split asks of every other
/// capability.
/// </remarks>
public sealed record MigrationDescribePayload
{
	public required IReadOnlyList<MigrationDescriptorDto> Migrations { get; init; }
}

/// <summary>One application a plugin migrates from.</summary>
public sealed record MigrationDescriptorDto
{
	/// <summary>
	/// The source application, as one of the SDK's <c>MigrationSource</c> names. A name this host does not
	/// know is ignored rather than refused: reading that application's files is the host's own work, so a
	/// migration for a source it cannot read has nothing to translate anyway.
	/// </summary>
	public required string Source { get; init; }

	public required IReadOnlyList<string> ClaimedActionSources { get; init; }

	public required IReadOnlyList<string> ClaimedSettingsSources { get; init; }
}

/// <summary>One action as the source application stored it, as it travels on the wire.</summary>
public sealed record ForeignActionDto
{
	public required string TypeName { get; init; }

	public required string ActionSource { get; init; }

	public string? DisplayName { get; init; }

	/// <summary>Opaque to the host - only the plugin that wrote it knows its shape.</summary>
	public string? Configuration { get; init; }

	public string? ConfigurationSummary { get; init; }
}

/// <summary>Arguments of <c>migrate-action</c>: one action as the source application stored it.</summary>
public sealed record MigrateActionRequestPayload
{
	public required string Source { get; init; }

	public required string TypeName { get; init; }

	public required string ActionSource { get; init; }

	public string? DisplayName { get; init; }

	/// <summary>Opaque to the host - only the plugin that wrote it knows its shape.</summary>
	public string? Configuration { get; init; }

	public string? ConfigurationSummary { get; init; }
}

/// <summary>
/// The result of <c>migrate-action</c>. <see cref="Translated" /> is false when the plugin has no
/// equivalent, which is a normal answer: the host then keeps the action as a placeholder carrying its
/// original configuration, rather than the plugin inventing something that would behave differently.
/// </summary>
public sealed record MigrateActionResultPayload
{
	public required bool Translated { get; init; }

	public string? IntegrationId { get; init; }

	public string? ActionId { get; init; }

	public string? Label { get; init; }

	public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

	/// <summary>Anything the plugin could not carry across exactly.</summary>
	public IReadOnlyList<LocalizedText>? Warnings { get; init; }
}

/// <summary>
/// Arguments of <c>migrate-configuration</c>. <see cref="Credentials" /> is empty when the user declined
/// to decrypt them or the key did not open them, which is routine - a plugin must then either build an
/// entry that works without them or return none.
/// </summary>
public sealed record MigrateConfigurationRequestPayload
{
	public required string Source { get; init; }

	public required string SettingsSource { get; init; }

	public required IReadOnlyDictionary<string, string> Settings { get; init; }

	public required IReadOnlyList<IReadOnlyDictionary<string, string>> Credentials { get; init; }

	/// <summary>
	/// Every action of this plugin the host found in the setup being read, translated or not - the
	/// source application may have kept part of a plugin's configuration there rather than in its
	/// settings file. Absent from a request built by a host that predates this field, which a plugin
	/// reads as none.
	/// </summary>
	public IReadOnlyList<ForeignActionDto>? Actions { get; init; }
}

public sealed record MigrateConfigurationResultPayload
{
	public required IReadOnlyList<MigratedConfigurationDto> Configurations { get; init; }
}

/// <summary>
/// A configuration entry to create. Secret values travel separately from <see cref="Values" /> so the host
/// can put each one in the secret store and leave only a reference behind in the stored entry.
/// </summary>
public sealed record MigratedConfigurationDto
{
	public required string IntegrationId { get; init; }

	public required string Title { get; init; }

	public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }

	public required IReadOnlyDictionary<string, MigratedSecretDto> Secrets { get; init; }
}

public sealed record MigratedSecretDto
{
	public required string Value { get; init; }

	/// <summary>One of the SDK's <c>MigratedSecretKind</c> names.</summary>
	public required string Kind { get; init; }
}
