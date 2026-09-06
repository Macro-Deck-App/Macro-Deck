using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Domain.Enums;
using DomainFolderFocusRule = MacroDeckHost.Domain.Entities.FolderFocusRule;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetFolderFocusRuleRequestMessageHandler
	: IUiTransportMessageHandler<SetFolderFocusRuleRequest, SetFolderFocusRuleResponse>
{
	private readonly IFolderService _folderService;
	private readonly IFolderCache _folderCache;
	private readonly IDeviceRepository _deviceRepository;

	public SetFolderFocusRuleRequestMessageHandler(
		IFolderService folderService,
		IFolderCache folderCache,
		IDeviceRepository deviceRepository)
	{
		_folderService = folderService;
		_folderCache = folderCache;
		_deviceRepository = deviceRepository;
	}

	public async ValueTask<SetFolderFocusRuleResponse> Handle(SetFolderFocusRuleRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return Fail(FolderError.ValidationError, "A valid folder id is required");
		}

		if (!Guid.TryParse(request.DeviceId, out var deviceId))
		{
			return Fail(FolderError.ValidationError, "A valid device id is required");
		}

		var ruleId = Guid.Empty;
		if (!string.IsNullOrEmpty(request.RuleId) && !Guid.TryParse(request.RuleId, out ruleId))
		{
			return Fail(FolderError.ValidationError, "A valid rule id is required");
		}

		var device = await _deviceRepository.GetById(deviceId);
		if (device is null)
		{
			return Fail(FolderError.ValidationError, "Device not found");
		}

		var rule = new DomainFolderFocusRule
		{
			Id = ruleId,
			Enabled = request.Enabled,
			ApplicationIdentity = request.ApplicationIdentity,
			IdentityKind = request.IdentityKind,
			DeviceId = deviceId,
			ReturnOnFocusLoss = request.ReturnOnFocusLoss
		};

		var result = await _folderService.SetFocusRule(folderId, rule);
		if (!result.Success)
		{
			return Fail(result.Error!.Value, result.ErrorMessage);
		}

		var folder = _folderCache.GetFolderById(folderId)!;

		return new SetFolderFocusRuleResponse
		{
			Success = true,
			Rule = FolderFocusRuleDtoMapper.MapToDto(folder, result.Data!, device)
		};
	}

	private static SetFolderFocusRuleResponse Fail(FolderError error, string? message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = error.ToString(), Message = message ?? string.Empty }
		};
}
