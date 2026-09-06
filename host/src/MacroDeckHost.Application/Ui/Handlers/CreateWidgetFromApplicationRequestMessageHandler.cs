using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateWidgetFromApplicationRequestMessageHandler
	: IUiTransportMessageHandler<CreateWidgetFromApplicationRequest, CreateWidgetResponse>
{
	private const string SystemIntegrationId = "app.macro-deck.system";

	private const string LaunchApplicationActionId = "launch-application";

	private static readonly TimeSpan _iconReadyTimeout = TimeSpan.FromSeconds(15);

	private static readonly TimeSpan _iconPollInterval = TimeSpan.FromMilliseconds(100);

	private readonly IApplicationPathResolver _pathResolver;
	private readonly IIntegrationRegistry _integrations;
	private readonly IAppIconExtractor _appIconExtractor;
	private readonly IIconImportService _iconImportService;
	private readonly IIconPackCache _iconPackCache;
	private readonly IFolderCache _folderCache;
	private readonly IWidgetService _widgetService;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;

	public CreateWidgetFromApplicationRequestMessageHandler(
		IApplicationPathResolver pathResolver,
		IIntegrationRegistry integrations,
		IAppIconExtractor appIconExtractor,
		IIconImportService iconImportService,
		IIconPackCache iconPackCache,
		IFolderCache folderCache,
		IWidgetService widgetService,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		ILogger logger)
	{
		_pathResolver = pathResolver;
		_integrations = integrations;
		_appIconExtractor = appIconExtractor;
		_iconImportService = iconImportService;
		_iconPackCache = iconPackCache;
		_folderCache = folderCache;
		_widgetService = widgetService;
		_preferences = preferences;
		_localization = localization;
		_logger = logger.ForContext<CreateWidgetFromApplicationRequestMessageHandler>();
	}

	public async ValueTask<CreateWidgetResponse> Handle(
		CreateWidgetFromApplicationRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.FolderId, out var folderId))
		{
			return Failed(WidgetError.FolderNotFound, AppStrings.Errors.Folders.FolderIdRequired());
		}

		var folder = _folderCache.GetFolderById(folderId);
		if (folder is null)
		{
			return Failed(WidgetError.FolderNotFound, AppStrings.Errors.Folders.NotFound());
		}

		if (request.PositionX < 0 || request.PositionY < 0 || IsOccupied(folder, request))
		{
			return Failed(WidgetError.PositionOccupied, AppStrings.Errors.Widgets.PositionOccupied());
		}

		var application = _pathResolver.Resolve(request.Path);
		if (application is null)
		{
			return Failed(WidgetError.ValidationError,
				AppStrings.Errors.Applications.NotAnApplication(
					name: Path.GetFileName(IconImportFiles.TrimTrailingSeparators(request.Path))));
		}

		var action = _integrations.FindAction(SystemIntegrationId,
			LaunchApplicationActionId);
		if (action is null)
		{
			return Failed(WidgetError.ValidationError, AppStrings.Errors.Applications.LaunchActionUnavailable());
		}

		var iconId = await ImportIcon(request.Path, cancellationToken);
		var culture = (await _preferences.GetLocalization()).Culture;

		var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = application.Path };
		if (!string.IsNullOrWhiteSpace(application.Arguments))
		{
			values["arguments"] = application.Arguments;
		}

		var widget = new WidgetEntity
		{
			Type = WidgetTypeIds.ActionButton,
			PositionX = request.PositionX,
			PositionY = request.PositionY,
			Width = 1,
			Height = 1,
			Data = LaunchApplicationButtonJson.Build(SystemIntegrationId,
				action,
				values,
				application.Name,
				iconId,
				_localization,
				culture)
		};

		var result = await _widgetService.Create(folderId, widget);
		if (!result.Success)
		{
			return new CreateWidgetResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = result.Error.ToString()!,
					Message = result.ErrorMessage ?? string.Empty
				}
			};
		}

		return new CreateWidgetResponse { Success = true, Widget = FolderDtoMapper.MapWidgetToDto(result.Data!) };
	}

	private static bool IsOccupied(FolderEntity folder, CreateWidgetFromApplicationRequest request)
		=> folder.Widgets.Any(widget =>
			request.PositionX >= widget.PositionX &&
			request.PositionX < widget.PositionX + widget.Width &&
			request.PositionY >= widget.PositionY &&
			request.PositionY < widget.PositionY + widget.Height);

	private async Task<Guid?> ImportIcon(string path, CancellationToken cancellationToken)
	{
		if (!_appIconExtractor.CanExtract(path))
		{
			return null;
		}

		var extracted = await _appIconExtractor.Extract(path, cancellationToken);
		if (!extracted.Success)
		{
			_logger.Debug("No icon extracted from '{Path}': {Error}", path, extracted.ErrorMessage);
			return null;
		}

		var icon = extracted.Data!;
		await using var content = new MemoryStream(icon.Content, writable: false);
		var imported = await _iconImportService.ImportSingle(null,
			new IconImportFile(icon.FileName, content),
			cancellationToken);

		if (!imported.Success)
		{
			_logger.Debug("Failed to import the icon of '{Path}': {Error}", path, imported.ErrorMessage);
			return null;
		}

		return await WaitUntilReady(imported.Data!.Icon.Id, cancellationToken);
	}

	private async Task<Guid?> WaitUntilReady(Guid iconId, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + _iconReadyTimeout;
		while (DateTime.UtcNow < deadline)
		{
			var state = _iconPackCache.GetIconById(iconId)?.ProcessingState;
			switch (state)
			{
				case IconProcessingState.Ready:
					return iconId;
				case null:
				case IconProcessingState.Failed:
					return null;
			}

			try
			{
				await Task.Delay(_iconPollInterval,
					cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// The caller gave up mid-import. The icon still lands in the pack; what must not happen
				// is the whole request failing after the point where it could have created a button.
				return null;
			}
		}

		_logger.Warning("Icon {IconId} was still converting; creating the button without it", iconId);
		return null;
	}

	private static CreateWidgetResponse Failed(WidgetError error, LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = error.ToString(), Message = message }
		};
}
