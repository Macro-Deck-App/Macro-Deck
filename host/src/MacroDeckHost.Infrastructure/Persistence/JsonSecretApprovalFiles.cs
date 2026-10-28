using System.Text.Json;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Secrets.Approvals;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

// Each group keeps its records inside its own data directory so a backup group restores them together
// with the flows they approve; document stores enumerate only their top level, so the subfolder is safe.
public sealed class JsonSecretApprovalFiles : ISecretApprovalFiles
{
	public const string DirectoryName = "secret-approvals";
	private const string MarkerFileName = "backfilled";

	private static readonly JsonSerializerOptions _options = PersistenceJsonOptions.Default;

	private readonly IMacroDeckPaths _paths;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _files;
	private readonly Lock _gate = new();

	public JsonSecretApprovalFiles(
		IMacroDeckPaths paths,
		ILogger logger,
		IPersistenceRecoveryReporter? recoveryReporter = null)
	{
		_paths = paths;
		_logger = logger;
		_files = new DurableJsonFile("secret approval",
			_options,
			logger,
			PersistenceBackup.KeepLastKnownGood,
			recoveryReporter);
	}

	public bool IsBackfilled(SecretApprovalGroup group)
	{
		return File.Exists(Path.Combine(DirectoryOf(group), MarkerFileName));
	}

	public void MarkBackfilled(SecretApprovalGroup group)
	{
		lock (_gate)
		{
			var directory = DirectoryOf(group);
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, MarkerFileName), string.Empty);
		}
	}

	public IReadOnlyList<SecretApprovalPartitionLoad> LoadAll(SecretApprovalGroup group)
	{
		var loads = new List<SecretApprovalPartitionLoad>();
		lock (_gate)
		{
			foreach (var path in DurableJsonFile.EnumerateDocumentPaths(DirectoryOf(group), ".json"))
			{
				if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var partitionId))
				{
					continue;
				}

				var partition = _files.Read<SecretApprovalPartition>(path);
				loads.Add(new SecretApprovalPartitionLoad(partitionId, partition, partition is null));
			}
		}

		return loads;
	}

	public void Save(SecretApprovalGroup group, Guid partitionId, SecretApprovalPartition partition)
	{
		lock (_gate)
		{
			try
			{
				var path = PathFor(group, partitionId);
				if (partition.Owners.Count == 0)
				{
					if (File.Exists(path))
					{
						_files.Delete(path);
					}

					return;
				}

				Directory.CreateDirectory(DirectoryOf(group));
				_files.Write(path, partition);
			}
			catch (Exception ex)
			{
				_logger.Error(ex,
					"Failed to write secret approvals of {Group} partition {PartitionId}",
					group,
					partitionId);
			}
		}
	}

	public void Delete(SecretApprovalGroup group, Guid partitionId)
	{
		lock (_gate)
		{
			try
			{
				_files.Delete(PathFor(group, partitionId));
			}
			catch (Exception ex)
			{
				_logger.Error(ex,
					"Failed to delete secret approvals of {Group} partition {PartitionId}",
					group,
					partitionId);
			}
		}
	}

	private string DirectoryOf(SecretApprovalGroup group)
	{
		var root = group switch
		{
			SecretApprovalGroup.Profiles => _paths.ProfilesDirectory,
			SecretApprovalGroup.Automations => _paths.AutomationsDirectory,
			_ => _paths.ScriptsDirectory
		};
		return Path.Combine(root, DirectoryName);
	}

	private string PathFor(SecretApprovalGroup group, Guid partitionId)
	{
		return Path.Combine(DirectoryOf(group), partitionId + ".json");
	}
}
