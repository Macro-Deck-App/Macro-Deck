using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence;

public interface IVariableBindingStore
{
	/// <summary>Best-effort read for callers that only display bindings: returns an empty list both when
	/// there genuinely are none and when the store could not be read. Use <see cref="TryLoad"/> instead
	/// before a load-modify-save so a transient read failure cannot be mistaken for "no bindings" and
	/// overwrite what is actually on disk.</summary>
	IReadOnlyList<VariableBinding> Load();

	/// <summary>Reads the current bindings, distinguishing "there are none" from "the store could not be
	/// read". Returns <c>false</c> (with an empty list) when the underlying file exists but could not be
	/// recovered, so a caller about to overwrite the store can refuse instead of truncating it.</summary>
	bool TryLoad(out IReadOnlyList<VariableBinding> bindings);

	/// <summary>Persists the given bindings, replacing whatever was stored before. Returns <c>false</c> if
	/// the write failed - the caller must not treat the mutation as durable in that case.</summary>
	bool Save(IEnumerable<VariableBinding> bindings);
}
