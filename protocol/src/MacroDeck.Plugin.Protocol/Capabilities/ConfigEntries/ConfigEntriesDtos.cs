using System.Text.Json;
using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;

/// <summary>One stored configuration entry as the <c>config-entries</c> operations carry it. A secret field
/// holds the reference to the encrypted value, as the plugin's own <c>config</c> host api reads it.</summary>
public sealed record ConfigEntryDto
{
	public required Guid Id { get; init; }

	public required string Title { get; init; }

	public DateTime CreatedAt { get; init; }

	public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
		new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

/// <summary>Arguments of <c>config-entries/prepare</c>.</summary>
public sealed record ConfigEntryPrepareArguments
{
	public required Guid EntryId { get; init; }

	public required string Title { get; init; }

	public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }

	public ConfigEntryDto? Existing { get; init; }

	public IReadOnlyList<ConfigEntryDto> Siblings { get; init; } = [];

	public bool TitleChanged { get; init; }

	/// <summary>Reserved variable names the host could not hold in earlier attempts of the same change, all of
	/// them so far. Sent only on a repeated prepare, which the host makes only after a result with
	/// <see cref="ConfigEntryPrepareResult.RetryOnConflict" />. Additive within protocol v1: a host that
	/// predates it never sends it.</summary>
	public IReadOnlyList<string>? RejectedVariableNames { get; init; }
}

/// <summary>A global variable an entry being prepared will create, so the host can hold its name.</summary>
public sealed record ConfigEntryReservedVariableDto
{
	public required string Name { get; init; }

	public required string DefinitionId { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name.</summary>
	public required string Type { get; init; }
}

/// <summary>Result of <c>config-entries/prepare</c>.</summary>
public sealed record ConfigEntryPrepareResult
{
	public required bool Success { get; init; }

	/// <summary>The values to store instead of the prepared ones; absent stores them unchanged.</summary>
	public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

	public LocalizedText? Error { get; init; }

	public IReadOnlyList<ConfigEntryReservedVariableDto> ReservedVariables { get; init; } = [];

	/// <summary><c>true</c> asks the host to prepare again with the names it could not hold, instead of refusing
	/// the entry on a reservation conflict. Additive within protocol v1: absent reads as <c>false</c>, and a
	/// host that predates it refuses on the first conflict.</summary>
	public bool? RetryOnConflict { get; init; }
}

/// <summary>Arguments of <c>config-entries/status</c> and <c>config-entries/usable</c>.</summary>
public sealed record ConfigEntryArguments
{
	public required ConfigEntryDto Entry { get; init; }
}

/// <summary>Result of <c>config-entries/status</c>.</summary>
public sealed record ConfigEntryStatusResult
{
	/// <summary>The <c>MacroDeck.Sdk.ConfigFlow.ConfigEntryStatus</c> enumerant name. A name the host does not
	/// know reads as <c>Ready</c>.</summary>
	public required string Status { get; init; }
}

/// <summary>Result of <c>config-entries/usable</c>.</summary>
public sealed record ConfigEntryUsableResult
{
	public required bool Usable { get; init; }
}

/// <summary>Result of <c>config-entries/reload</c>.</summary>
public sealed record ConfigEntryReloadResult
{
	/// <summary>Whether the plugin applied the change itself; <c>false</c> makes the host re-initialize the
	/// integration.</summary>
	public required bool Reloaded { get; init; }
}

/// <summary>Arguments of <c>config-entries/synchronize</c>: every stored entry.</summary>
public sealed record ConfigEntrySynchronizeArguments
{
	public required IReadOnlyList<ConfigEntryDto> Entries { get; init; }
}

/// <summary>Arguments of <c>config-entries/release</c>.</summary>
public sealed record ConfigEntryReleaseArguments
{
	public required Guid EntryId { get; init; }
}
