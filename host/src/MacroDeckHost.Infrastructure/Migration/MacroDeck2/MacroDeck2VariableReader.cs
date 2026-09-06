using MacroDeckHost.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

internal sealed record MacroDeck2Variable(string Name, string Value, string Creator, VariableType Type);

/// <summary>
/// Reads Macro Deck 2's <c>variables.db</c>. Only variables the user created are of interest: every other
/// row was produced by a plugin, and the corresponding Macro Deck 3 integration declares its own.
/// </summary>
internal static class MacroDeck2VariableReader
{
	private const string UserCreator = "User";

	public static IReadOnlyList<MacroDeck2Variable> ReadUserVariables(MacroDeck2Paths paths)
	{
		if (!File.Exists(paths.VariablesDatabase))
		{
			return [];
		}

		var variables = new List<MacroDeck2Variable>();
		try
		{
			var connectionString = new SqliteConnectionStringBuilder
			{
				DataSource = paths.VariablesDatabase,
				Mode = SqliteOpenMode.ReadOnly
			}.ToString();

			using var connection = new SqliteConnection(connectionString);
			connection.Open();

			using var command = connection.CreateCommand();
			command.CommandText = "SELECT Name, Value, Creator, Type FROM Variable";

			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var name = reader.IsDBNull(0) ? null : reader.GetString(0);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				var creator = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
				if (!string.Equals(creator, UserCreator, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				variables.Add(new MacroDeck2Variable(name,
					reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
					creator,
					MapType(reader.IsDBNull(3) ? null : reader.GetString(3))));
			}
		}
		catch (SqliteException)
		{
			return variables;
		}

		return variables;
	}

	/// <summary>
	/// Macro Deck 2 distinguishes Integer from Float; Macro Deck 3 has one numeric type and carries the
	/// precision separately, so both collapse onto <see cref="VariableType.Numeric" />.
	/// </summary>
	private static VariableType MapType(string? type)
		=> type?.Trim().ToLowerInvariant() switch
		{
			"bool" or "boolean" => VariableType.Boolean,
			"integer" or "int" or "float" or "double" or "number" => VariableType.Numeric,
			_ => VariableType.Text
		};
}
