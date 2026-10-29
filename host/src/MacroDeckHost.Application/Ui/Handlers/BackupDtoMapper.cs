using System.Runtime.InteropServices;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class BackupDtoMapper
{
	public static BackupSummary ToSummary(BackupDescriptor descriptor, string providerDisplayName)
	{
		return new BackupSummary
		{
			Id = descriptor.BackupId,
			ProviderId = descriptor.ProviderId,
			ProviderDisplayName = providerDisplayName,
			Name = descriptor.Name,
			CreatedAt = descriptor.CreatedAt,
			Trigger = descriptor.Trigger.ToString(),
			MacroDeckVersion = descriptor.MacroDeckVersion,
			SizeBytes = descriptor.SizeBytes,
			FormatVersion = descriptor.FormatVersion,
			IsRemote = descriptor.IsRemote,
			DecryptableLocally = descriptor.DecryptableLocally,
			Imported = descriptor.Imported,
			Note = descriptor.Note,
			Components = ToNames(descriptor.Components)
		};
	}

	public static List<BackupComponentCatalogEntry> ToCatalog()
	{
		return
		[
			.. BackupComponentGroups.All.Select(definition => new BackupComponentCatalogEntry
			{
				Id = definition.Id.ToString(),
				Requires = ToNames(definition.Requires)
			})
		];
	}

	public static List<BackupComponentGroupInfoDto> ToComponentInfo(IReadOnlyList<BackupComponentGroupInfo> components)
	{
		return
		[
			.. components.Select(component => new BackupComponentGroupInfoDto
			{
				Id = component.Id.ToString(),
				EntryCount = component.EntryCount,
				ByteSize = component.ByteSize,
				Requires = ToNames(component.Requires)
			})
		];
	}

	public static List<BackupDependencyWarningDto> ToWarnings(IReadOnlyList<BackupDependencyWarning> warnings)
	{
		return
		[
			.. warnings.Select(warning => new BackupDependencyWarningDto
			{
				Group = warning.Group.ToString(),
				MissingDependency = warning.MissingDependency.ToString()
			})
		];
	}

	public static InspectBackupResponse ToInspectResponse(BackupInspection inspection, string providerDisplayName)
	{
		return new InspectBackupResponse
		{
			Success = true,
			Backup = ToSummary(inspection.Backup, providerDisplayName),
			RecoveryKeyRequired = inspection.RecoveryKeyRequired,
			Components = ToComponentInfo(inspection.Components),
			Catalog = ToCatalog()
		};
	}

	public static GetBackupRecoveryKeyStateResponse ToStateResponse(BackupRecoveryKeyState state)
	{
		return new GetBackupRecoveryKeyStateResponse
		{
			Availability = state.Availability.ToString(),
			KeyId = state.KeyId,
			CreatedAt = state.CreatedAt,
			ExportedAt = state.ExportedAt
		};
	}

	public static BackupRecoveryKeyResponse ToRecoveryKeyResponse(BackupRecoveryKeyState state, string? key)
	{
		return new BackupRecoveryKeyResponse
		{
			Success = true,
			Availability = state.Availability.ToString(),
			KeyId = state.KeyId,
			CreatedAt = state.CreatedAt,
			ExportedAt = state.ExportedAt,
			Key = key
		};
	}

	public static BackupRecoveryKeyResponse ToRecoveryKeyFailure(BackupError error, string? message)
	{
		return new BackupRecoveryKeyResponse
		{
			Success = false,
			Error = ToTransportError(error, message)
		};
	}

	public static (bool Supported, string? Reason) PreUpdateBackupSupport()
	{
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
			? (true, null)
			: (false, "Macro Deck does not install updates itself on Linux.");
	}

	public static TransportError ToTransportError(BackupError error, string? message)
	{
		return new TransportError
		{
			Code = error.ToString(),
			Message = string.IsNullOrEmpty(message) ? DefaultMessage(error) : message
		};
	}

	private static LocalizedText DefaultMessage(BackupError error)
	{
		return error switch
		{
			BackupError.NotFound => AppStrings.Errors.Backup.NotFound(),
			BackupError.ProviderUnavailable => AppStrings.Errors.Backup.ProviderUnavailable(),
			BackupError.StorageFailure => AppStrings.Errors.Backup.StorageFailure(),
			BackupError.InsufficientDiskSpace => AppStrings.Errors.Backup.InsufficientDiskSpace(),
			BackupError.InvalidArchive => AppStrings.Errors.Backup.InvalidArchive(),
			BackupError.UnsupportedVersion => AppStrings.Errors.Backup.UnsupportedVersion(),
			BackupError.UnsupportedEncryption => AppStrings.Errors.Backup.UnsupportedEncryption(),
			BackupError.IntegrityFailure => AppStrings.Errors.Backup.IntegrityFailure(),
			BackupError.RecoveryKeyMissing => AppStrings.Errors.Backup.RecoveryKeyMissing(),
			BackupError.RecoveryKeyRequired => AppStrings.Errors.Backup.RecoveryKeyRequired(),
			BackupError.RecoveryKeyInvalid => AppStrings.Errors.Backup.RecoveryKeyInvalid(),
			BackupError.Busy => AppStrings.Errors.Backup.Busy(),
			BackupError.Cancelled => AppStrings.Errors.Backup.Cancelled(),
			BackupError.SnapshotFailed => AppStrings.Errors.Backup.SnapshotFailed(),
			BackupError.RestoreStagingFailed => AppStrings.Errors.Backup.RestoreStagingFailed(),
			BackupError.RestorePending => AppStrings.Errors.Backup.RestorePending(),
			BackupError.RestartUnavailable => AppStrings.Errors.Backup.RestartUnavailable(),
			BackupError.ValidationError => AppStrings.Errors.Backup.ValidationError(),
			BackupError.TooLarge => AppStrings.Errors.Backup.TooLarge(),
			BackupError.DesktopOnly => AppStrings.Errors.Backup.DesktopOnly(),
			BackupError.FileLocked => AppStrings.Errors.Backup.FileLocked(),
			BackupError.IncompatibleVersion => AppStrings.Errors.Backup.IncompatibleVersion(),
			_ => AppStrings.Errors.Backup.OperationFailed()
		};
	}

	/// <summary>
	/// Component groups cross the wire by name, like every other enum in these messages. Sending the
	/// numeric value would tie clients to the declaration order of the enum.
	/// </summary>
	public static List<string> ToNames(IEnumerable<BackupComponentGroup> groups)
	{
		return [.. groups.Select(group => group.ToString())];
	}

	public static bool TryParseGroups(IEnumerable<string> names, out List<BackupComponentGroup> groups)
	{
		groups = [];

		foreach (var name in names)
		{
			if (!Enum.TryParse<BackupComponentGroup>(name, true, out var parsed) || !Enum.IsDefined(parsed))
			{
				return false;
			}

			groups.Add(parsed);
		}

		return true;
	}
}
