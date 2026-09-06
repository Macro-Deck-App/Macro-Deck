using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// Tracks every composed node id materialized so far, across the whole tree including
/// <see cref="UiElement.Fallback" /> subtrees, so a duplicate is reported with both declaration paths
/// instead of silently overwriting the first. Node ids are a producer obligation with no API in the
/// model itself; this registry is where <see cref="UiViewBuilder" /> enforces it.
/// </summary>
internal sealed class UiNodeIdRegistry
{
	private readonly Dictionary<string, string> _declarationPathsById = new(StringComparer.Ordinal);

	/// <summary>Registers <paramref name="id" /> as declared at <paramref name="declarationPath" />.
	/// Throws <see cref="UiViewException" /> naming <paramref name="id" /> and both declaration paths
	/// when it was already registered.</summary>
	public void Register(string id, string declarationPath)
	{
		if (_declarationPathsById.TryGetValue(id, out var existingPath))
		{
			throw new UiViewException(
				$"Duplicate node id '{id}': first declared at '{existingPath}', also declared at " +
				$"'{declarationPath}'. Node ids must be unique across the whole tree, including inside " +
				"Fallback subtrees.");
		}

		_declarationPathsById[id] = declarationPath;
	}
}
