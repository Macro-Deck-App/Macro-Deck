using MacroDeckHost.Domain.Entities;
using DomainFolderFocusRule = MacroDeckHost.Domain.Entities.FolderFocusRule;
using WireFolderFocusRule = MacroDeckHost.Application.Ui.Transport.Messages.Folders.FolderFocusRule;

namespace MacroDeckHost.Application.Ui.Handlers;

public static class FolderFocusRuleDtoMapper
{
	public static WireFolderFocusRule MapToDto(FolderEntity folder, DomainFolderFocusRule rule, DeviceEntity? device)
		=> new()
		{
			FolderId = folder.Id.ToString(),
			FolderName = folder.Name,
			ProfileId = folder.ProfileId.ToString(),
			RuleId = rule.Id.ToString(),
			Enabled = rule.Enabled,
			ApplicationIdentity = rule.ApplicationIdentity,
			IdentityKind = rule.IdentityKind,
			DeviceId = rule.DeviceId.ToString(),
			DeviceName = device?.Name,
			DeviceExists = device is not null,
			ReturnOnFocusLoss = rule.ReturnOnFocusLoss
		};
}
