using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Backups;

public enum BackupPathRuleKind
{
	Directory,
	File,
	Prefix
}

public sealed record BackupPathRule(string Value, BackupPathRuleKind Kind);

public sealed record BackupComponentGroupDefinition(
	BackupComponentGroup Id,
	IReadOnlyList<BackupComponentGroup> Requires,
	IReadOnlyList<BackupPathRule> Paths,
	IReadOnlyList<string> Tables);

public static class BackupComponentGroups
{
	/// <summary>
	/// Paths under the data root that are never archived. Derived state that the host rebuilds, plus the
	/// backup directories themselves - archiving those would nest a second full installation, key ring
	/// included, inside every backup taken while a restore is staged.
	/// </summary>
	public static readonly IReadOnlyList<string> ExcludedPrefixes =
	[
		"logs/",
		"backups/",
		"platform-tools/",
		"data/icons/staging/",
		"data/icons/fallback-cache/",
		"plugins/_staging/",
		"plugins/_cache/",
		"plugins/assets/"
	];

	public static readonly IReadOnlyList<BackupComponentGroupDefinition> All =
	[
		new(BackupComponentGroup.Icons, [], [Dir("data/icons")], []),
		// The binding file travels with the variables it materializes: a restored profile whose Slider is
		// bound to a catalog variable would otherwise come back permanently unbound.
		new(BackupComponentGroup.Variables,
			[],
			[File("data/user-variables.json"), File("data/dynamic-variable-bindings.json")],
			[]),
		new(BackupComponentGroup.Integrations,
			[],
			// The Data Protection key ring and the escrow that recovers the key wrapping it, and nothing
			// else under keys/. The secret rows are meaningless without the ring that protected them,
			// and on another machine the ring is meaningless without the escrow, so all three move
			// together or not at all. Owner() returning null here would drop the escrow from every
			// archive silently and leave a stale one behind on restore.
			[Prefix("keys/key-"), File("keys/kek.escrow"), File("data/integration-states.json")],
			["secret", "integration_config_entry"]),
		new(BackupComponentGroup.AppSettings,
			[],
			[File("config/adb-state.json"), Dir("resources"), Prefix("keys/public-tls.")],
			["app_preference"]),
		new(BackupComponentGroup.Plugins,
			[BackupComponentGroup.Integrations],
			[
				Dir("plugins"),
				File("config/plugin-runtime.json"),
				File("config/plugin-capability-snapshots.json")
			],
			["plugin_registration", "plugin_access_token", "plugin_trust_record"]),
		new(BackupComponentGroup.Scripts, [BackupComponentGroup.Integrations], [Dir("data/scripts")], []),
		new(BackupComponentGroup.Profiles,
			[
				BackupComponentGroup.Icons,
				BackupComponentGroup.Integrations,
				BackupComponentGroup.Plugins,
				BackupComponentGroup.Variables
			],
			[Dir("data/profiles")],
			[]),
		new(BackupComponentGroup.Automations,
			[BackupComponentGroup.Scripts, BackupComponentGroup.Integrations, BackupComponentGroup.Variables],
			[Dir("data/automations")],
			[]),
		new(BackupComponentGroup.Accounts,
			[BackupComponentGroup.Profiles],
			[File("keys/auth-signing.key")],
			["app_user", "refresh_token", "device"])
	];

	/// <summary>
	/// Preference keys and the recovery key row are never overwritten by a restore. Taking them from an
	/// archive would either point this installation at a secret row it does not have - orphaning every
	/// backup it owns - or give it the installation identity of the machine the archive came from, which
	/// is what the key ring's KEK store is keyed by.
	/// The onboarding flag is excluded for the same reason: it tracks what this installation has already
	/// shown its user, so a restore must neither resurrect a finished wizard nor erase one still owed.
	/// </summary>
	public static readonly IReadOnlyList<string> PreferenceKeyDenyPrefixes =
	[
		"backups.recoveryKey",
		"telemetry.installationId",
		"onboarding.",
		"connect."
	];

	public static IReadOnlyList<BackupComponentGroup> AllIds => [.. All.Select(definition => definition.Id)];

	public static BackupComponentGroupDefinition Definition(BackupComponentGroup id)
		=> All.First(definition => definition.Id == id);

	public static bool IsExcluded(string relativePath)
		=> ExcludedPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

	public static bool IsRestorablePreferenceKey(string key)
		=> !PreferenceKeyDenyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

	public static BackupComponentGroup? Owner(string relativePath)
	{
		foreach (var definition in All)
		{
			if (definition.Paths.Any(rule => Matches(rule, relativePath)))
			{
				return definition.Id;
			}
		}

		return null;
	}

	private static bool Matches(BackupPathRule rule, string relativePath)
		=> rule.Kind switch
		{
			BackupPathRuleKind.Directory => relativePath.StartsWith(rule.Value + "/",
				StringComparison.OrdinalIgnoreCase),
			// A durable JSON document can be published as its own .bak sidecar, so the sidecar belongs to
			// whichever group owns the document itself.
			BackupPathRuleKind.File => relativePath.Equals(rule.Value, StringComparison.OrdinalIgnoreCase) ||
				relativePath.Equals(rule.Value + ".bak", StringComparison.OrdinalIgnoreCase),
			BackupPathRuleKind.Prefix => relativePath.StartsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
			_ => false
		};

	private static BackupPathRule Dir(string value) => new(value, BackupPathRuleKind.Directory);

	private static BackupPathRule File(string value) => new(value, BackupPathRuleKind.File);

	private static BackupPathRule Prefix(string value) => new(value, BackupPathRuleKind.Prefix);
}
