namespace MacroDeckHost.Domain.Entities;

// The volatile attributes that travel with a reading. Three-state on purpose: a null VariableBounds means
// "not supplied, leave whatever the entity holds alone", while an instance whose three fields are null
// means "explicitly unbounded now". Collapsing the two would make a transient unavailable reading erase a
// range that is still correct.
public sealed record VariableBounds(double? Min, double? Max, double? Step)
{
	public static readonly VariableBounds None = new(null, null, null);
}
