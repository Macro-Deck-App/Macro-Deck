using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Sdk.Migration;

/// <summary>
/// One action as the source application stored it.
/// </summary>
/// <remarks>
/// <see cref="Configuration" /> is opaque: an application that let each plugin choose its own format has no
/// schema to offer here. A migration parses it itself and reports failure by returning null rather than
/// throwing, because one unreadable button must not end a migration.
/// </remarks>
public sealed record ForeignAction(
	string TypeName,
	string ActionSource,
	string? DisplayName,
	string? Configuration,
	string? ConfigurationSummary);

/// <summary>
/// A translated action: which of this integration's actions to run, and with what.
/// </summary>
/// <remarks>
/// <paramref name="Parameters" /> carries only the values the source knew. The rest of each parameter -
/// its type, its label, its options - is filled in against the action's own definition, in the language of
/// whoever opens it, so a migration never has to restate what the action already declares.
/// <paramref name="Warnings" /> names anything that could not be carried across exactly; a partial
/// translation is useful as long as it is honest about what it dropped.
/// </remarks>
public sealed record ActionMigrationResult(
	string IntegrationId,
	string ActionId,
	string Label,
	IReadOnlyDictionary<string, JsonElement> Parameters,
	IReadOnlyList<LocalizedText>? Warnings = null);

/// <summary>
/// A foreign plugin's stored settings and its credentials, already decrypted by the source.
/// </summary>
/// <remarks>
/// <see cref="Credentials" /> is empty when the user chose not to decrypt them, or when the key did not
/// open that file - which is routine, since an application that ties its credentials to the machine that
/// wrote them cannot open a folder copied off another one.
///
/// <see cref="Actions" /> carries every action of this plugin the source found in the setup being read,
/// translated or not, because an application need not have kept all of a plugin's configuration in its
/// settings file: Macro Deck 2 stored the SinusBot server login there but the bot instance to play on in
/// each button. Without it a migration could only produce an entry that looks configured and cannot work.
/// </remarks>
public sealed record ForeignPluginSettings(
	string SettingsSource,
	IReadOnlyDictionary<string, string> Settings,
	IReadOnlyList<IReadOnlyDictionary<string, string>> Credentials,
	IReadOnlyList<ForeignAction> Actions);

/// <summary>
/// A configuration entry to create for an integration. Everything in <paramref name="Secrets" /> becomes a
/// real secret first and is referenced from the stored entry, so a secret value never lands in the entry
/// itself.
/// </summary>
public sealed record MigratedConfiguration(
	string IntegrationId,
	string Title,
	IReadOnlyDictionary<string, JsonElement> Values,
	IReadOnlyDictionary<string, MigratedSecret> Secrets);

/// <summary>A credential taken over from the source application.</summary>
public sealed record MigratedSecret(string Value, MigratedSecretKind Kind);

public enum MigratedSecretKind
{
	/// <summary>A password the user chose, which they may be shown again.</summary>
	Password = 0,

	/// <summary>A token or key the user never typed and is never shown.</summary>
	Secret = 1
}
