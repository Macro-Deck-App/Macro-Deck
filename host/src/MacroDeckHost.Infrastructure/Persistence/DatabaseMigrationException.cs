namespace MacroDeckHost.Infrastructure.Persistence;

internal sealed class DatabaseMigrationException : Exception
{
	public bool FromNewerHost { get; init; }

	public DatabaseMigrationException(string message)
		: base(message)
	{
	}

	public DatabaseMigrationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
