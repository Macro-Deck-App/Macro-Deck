using MacroDeckHost.Application.Paths;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MacroDeckHost.Infrastructure.Persistence;

public static class EarlyAppPreferenceReader
{
	public static string? TryRead(IMacroDeckPaths paths, string key) => TryRead(paths.DatabasePath, key);

	public static string? TryRead(string databasePath, string key)
		=> TryReadMany(databasePath, key).GetValueOrDefault(key);

	public static IReadOnlyDictionary<string, string> TryReadMany(IMacroDeckPaths paths, params string[] keys)
		=> TryReadMany(paths.DatabasePath, keys);

	/// <summary>
	/// Reads a preference, writing the supplied value first when the row does not exist yet. Used where
	/// a value has to be stable from the very first boot rather than from whenever a hosted service gets
	/// around to seeding it - the key ring's keystore account is derived from the installation id, and an
	/// id that changed after the key was stored would leave the entry unfindable.
	/// </summary>
	public static string EnsureValue(IMacroDeckPaths paths, string key, string value)
	{
		ArgumentNullException.ThrowIfNull(paths);

		var existing = TryRead(paths, key);
		if (!string.IsNullOrWhiteSpace(existing))
		{
			return existing;
		}

		try
		{
			var connectionString = new SqliteConnectionStringBuilder
			{
				DataSource = paths.DatabasePath,
				Mode = SqliteOpenMode.ReadWrite
			}.ToString();

			using var connection = new SqliteConnection(connectionString);
			connection.Open();

			using var command = connection.CreateCommand();
			var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
			command.CommandText =
				"INSERT INTO app_preference (ap_key, ap_value, ap_created_at, ap_updated_at) " +
				"VALUES ($key, $value, $now, $now) ON CONFLICT (ap_key) DO NOTHING";
			command.Parameters.AddWithValue("$key", key);
			command.Parameters.AddWithValue("$value", value);
			command.Parameters.AddWithValue("$now", now);
			command.ExecuteNonQuery();
		}
		catch (Exception e)
		{
			Log.Warning(e, "Could not seed the preference {Key} during startup", key);
		}

		return TryRead(paths, key) ?? value;
	}

	public static IReadOnlyDictionary<string, string> TryReadMany(string databasePath, params string[] keys)
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		if (keys.Length == 0)
		{
			return values;
		}

		try
		{
			if (!File.Exists(databasePath))
			{
				return values;
			}

			var connectionString = new SqliteConnectionStringBuilder
			{
				DataSource = databasePath,
				Mode = SqliteOpenMode.ReadOnly
			}.ToString();

			using var connection = new SqliteConnection(connectionString);
			connection.Open();

			using var command = connection.CreateCommand();
			command.CommandText = "SELECT ap_value FROM app_preference WHERE ap_key = $key";
			var parameter = command.Parameters.Add("$key", SqliteType.Text);

			foreach (var key in keys)
			{
				parameter.Value = key;
				if (command.ExecuteScalar() is string value)
				{
					values[key] = value;
				}
			}

			return values;
		}
		catch (Exception e)
		{
			Log.Warning(e, "Could not read the preferences {Keys} during startup", keys);
			return values;
		}
	}
}
