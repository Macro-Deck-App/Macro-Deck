using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>A configuration entry about to be stored, created or renamed, handed to
/// <see cref="IConfigEntryLifecycle.PrepareEntryAsync" />.</summary>
public sealed record ConfigEntryPreparation
{
	/// <summary>The entry's id: the id it will be stored under when it is new.</summary>
	public required Guid EntryId { get; init; }

	/// <summary>The title the entry will be stored with.</summary>
	public required string Title { get; init; }

	/// <summary>The values about to be stored.</summary>
	public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }

	/// <summary>The entry as it is stored now, or <c>null</c> when it is being created.</summary>
	public StoredConfigEntry? Existing { get; init; }

	/// <summary>The integration's other stored entries.</summary>
	public IReadOnlyList<StoredConfigEntry> Siblings { get; init; } = [];

	/// <summary>Whether the title differs from the stored one: a rename.</summary>
	public bool TitleChanged { get; init; }

	/// <summary>
	/// The names of <see cref="ConfigEntryPreparationResult.ReservedVariables" /> Macro Deck could not hold
	/// in earlier attempts of this same change, all of them so far. Empty on the first attempt. Macro Deck
	/// prepares the entry again only when the last result asked for it with
	/// <see cref="ConfigEntryPreparationResult.RetryOnConflict" />; avoid these names, for example by
	/// choosing another variable key, or refuse the entry.
	/// </summary>
	/// <remarks>A Macro Deck that predates it never sets it and never prepares an entry again.</remarks>
	public IReadOnlyList<string> RejectedVariableNames { get; init; } = [];
}

/// <summary>What <see cref="IConfigEntryLifecycle.PrepareEntryAsync" /> decided about an entry.</summary>
public sealed record ConfigEntryPreparationResult
{
	/// <summary>Whether the entry may be stored. When <c>false</c>, nothing is stored and the user sees
	/// <see cref="Error" />.</summary>
	public required bool Success { get; init; }

	/// <summary>The values to store instead of the prepared ones, for example with a derived key added.
	/// <c>null</c> stores the values unchanged.</summary>
	public IReadOnlyDictionary<string, JsonElement>? Values { get; init; }

	/// <summary>Shown to the user when <see cref="Success" /> is <c>false</c>.</summary>
	public LocalizedText Error { get; init; }

	/// <summary>
	/// Global variables this entry will create, by <see cref="VariableDefinition.Name" />,
	/// <see cref="VariableDefinition.Id" /> and <see cref="VariableDefinition.Type" />. Macro Deck holds the
	/// names for the entry until it is stored, or the preparation is released, and refuses the entry when a
	/// name is held by another integration's variable or another entry's preparation. A variable this
	/// integration already owns under the same id and type is not a conflict.
	/// </summary>
	public IReadOnlyList<VariableDefinition> ReservedVariables { get; init; } = [];

	/// <summary>
	/// Whether Macro Deck should prepare the entry again, with the names it could not hold added to
	/// <see cref="ConfigEntryPreparation.RejectedVariableNames" />, instead of refusing it when a name in
	/// <see cref="ReservedVariables" /> is taken. Macro Deck gives up after a fixed number of attempts and
	/// refuses the entry. The default, <c>false</c>, refuses it on the first conflict.
	/// </summary>
	/// <remarks>A Macro Deck that predates it ignores it and refuses the entry on the first conflict.</remarks>
	public bool RetryOnConflict { get; init; }

	/// <summary>Stores the entry, with <paramref name="values" /> when given.</summary>
	public static ConfigEntryPreparationResult Accept(
		IReadOnlyDictionary<string, JsonElement>? values = null,
		IReadOnlyList<VariableDefinition>? reservedVariables = null)
	{
		return new ConfigEntryPreparationResult
			{ Success = true, Values = values, ReservedVariables = reservedVariables ?? [] };
	}

	/// <summary>Refuses the entry and shows <paramref name="error" />.</summary>
	public static ConfigEntryPreparationResult Refuse(LocalizedText error)
	{
		return new ConfigEntryPreparationResult { Success = false, Error = error };
	}
}
