using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons;

public enum DerivedVariantOutcome
{
	Derived,
	NotApplicable,
	Unavailable
}

public sealed record DerivedVariant(DerivedVariantOutcome Outcome, string? StorageName = null, Stream? Content = null)
{
	public static readonly DerivedVariant NotApplicable = new(DerivedVariantOutcome.NotApplicable);
	public static readonly DerivedVariant Unavailable = new(DerivedVariantOutcome.Unavailable);
}

public interface IIconVariantDeriver
{
	Task<DerivedVariant> GetOrCreate(IconEntity icon,
		string masterContentHash,
		int size,
		CancellationToken cancellationToken);
}
