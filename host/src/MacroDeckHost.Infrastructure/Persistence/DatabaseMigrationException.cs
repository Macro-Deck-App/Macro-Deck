namespace MacroDeckHost.Infrastructure.Persistence;

internal sealed class DatabaseMigrationException : Exception
{
	public DatabaseMigrationException(string message)
		: base(message)
	{
	}

	public DatabaseMigrationException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
