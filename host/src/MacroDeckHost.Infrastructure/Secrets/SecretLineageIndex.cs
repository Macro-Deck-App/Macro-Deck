using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Infrastructure.Persistence;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Secrets;

// The file travels in the Integrations backup group together with the secret rows it describes.
public sealed class SecretLineageIndex : ISecretLineage
{
	public const string FileName = "secret-lineage.json";
	private const int MaxDepth = 64;

	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly string _path;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _file;
	private readonly Lock _gate = new();
	private Dictionary<Guid, Guid>? _parents;

	public SecretLineageIndex(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_path = Path.Combine(paths.DataDirectory, FileName);
		_logger = logger;
		_file = new DurableJsonFile("secret lineage",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public Guid Root(Guid secretId)
	{
		lock (_gate)
		{
			var parents = Parents();
			var current = secretId;
			for (var depth = 0; depth < MaxDepth && parents.TryGetValue(current, out var parent); depth++)
			{
				current = parent;
			}

			return current;
		}
	}

	public void RecordClone(Guid sourceId, Guid cloneId)
	{
		lock (_gate)
		{
			var parents = Parents();
			parents[cloneId] = sourceId;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
				_file.Write(_path, parents);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				_logger.Error(ex, "Failed to persist the lineage of secret {SecretId}", cloneId);
			}
		}
	}

	private Dictionary<Guid, Guid> Parents()
	{
		return _parents ??= _file.Read<Dictionary<Guid, Guid>>(_path) ?? new Dictionary<Guid, Guid>();
	}
}
