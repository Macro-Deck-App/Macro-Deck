namespace MacroDeckHost.Domain.Icons;

public readonly record struct MasterContentHash
{
	private MasterContentHash(string value)
	{
		Value = value;
	}

	public string Value { get; }

	public static MasterContentHash Compute(ReadOnlySpan<byte> content)
	{
		return new MasterContentHash(ContentHash.Compute(content));
	}

	public static MasterContentHash FromComputed(string value)
	{
		return new MasterContentHash(value);
	}

	public static bool TryParse(string? value, out MasterContentHash hash)
	{
		var normalized = ContentHash.Normalize(value);
		hash = normalized is null ? default : new MasterContentHash(normalized);
		return normalized is not null;
	}

	public override string ToString()
	{
		return Value;
	}
}
