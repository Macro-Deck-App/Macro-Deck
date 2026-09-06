using System.Text.Json;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class CreateWidgetFromApplicationRequestMessageHandlerTests
{
	private StubFolderCache _folders = null!;
	private FolderEntity _folder = null!;
	private FakeApplicationPathResolver _resolver = null!;
	private FakeIntegrationRegistry _integrations = null!;
	private StubAppIconExtractor _extractor = null!;
	private StubIconImportService _iconImports = null!;
	private StubIconPackCache _iconCache = null!;
	private RecordingWidgetService _widgets = null!;
	private CreateWidgetFromApplicationRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new StubFolderCache();
		_folder = _folders.AddFolder();
		_resolver = new FakeApplicationPathResolver
		{
			Result = new ResolvedApplication("/Applications/Calculator.app", "Calculator")
		};
		_integrations = new FakeIntegrationRegistry();
		_integrations.Add(new SystemLikeIntegration());
		_extractor = new StubAppIconExtractor();
		_iconImports = new StubIconImportService();
		_iconCache = new StubIconPackCache();
		_widgets = new RecordingWidgetService(_folders);

		_handler = new CreateWidgetFromApplicationRequestMessageHandler(_resolver,
			_integrations,
			_extractor,
			_iconImports,
			_iconCache,
			_folders,
			_widgets,
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			new LoggerConfiguration().CreateLogger());
	}

	private CreateWidgetFromApplicationRequest Request(int x = 2,
		int y = 1,
		string path = "/Applications/Calculator.app")
		=> new() { FolderId = _folder.Id.ToString(), PositionX = x, PositionY = y, Path = path };

	[Test]
	public async Task Handle_BuildsAnActionButtonThatLaunchesTheApplication()
	{
		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.That(response.Success, Is.True);
		var created = _widgets.Created.Single();
		using var data = JsonDocument.Parse(created.Data!);

		Assert.Multiple(() =>
		{
			Assert.That(created.Type, Is.EqualTo(WidgetTypeIds.ActionButton));
			Assert.That(created.PositionX, Is.EqualTo(2));
			Assert.That(created.PositionY, Is.EqualTo(1));
			Assert.That(data.RootElement.GetProperty("label").GetString(), Is.EqualTo("Calculator"));
			Assert.That(data.RootElement.GetProperty("mode").GetString(), Is.EqualTo("momentary"));
			Assert.That(data.RootElement.GetProperty("labelPosition").GetString(), Is.EqualTo("bottom"));
		});

		using var flows = JsonDocument.Parse(data.RootElement.GetProperty("flows").GetString()!);
		var flow = flows.RootElement.EnumerateArray().Single();
		var block = flow.GetProperty("children").EnumerateArray().Single();

		Assert.Multiple(() =>
		{
			Assert.That(flow.GetProperty("triggerType").GetString(), Is.EqualTo("onShortPress"));
			Assert.That(flow.GetProperty("triggerId").GetString(), Is.EqualTo("onShortPress"));
			Assert.That(block.GetProperty("type").GetString(), Is.EqualTo("action"));
			Assert.That(block.GetProperty("integrationId").GetString(), Is.EqualTo("app.macro-deck.system"));
			Assert.That(block.GetProperty("actionId").GetString(), Is.EqualTo("launch-application"));
			Assert.That(block.GetProperty("blockType").GetString(),
				Is.EqualTo("app.macro-deck.system.launch-application"));
		});

		var parameters = block.GetProperty("parameters").EnumerateArray().ToList();
		var path = parameters.Single(p => p.GetProperty("name").GetString() == "path");
		var mode = parameters.Single(p => p.GetProperty("name").GetString() == "mode");

		Assert.Multiple(() =>
		{
			Assert.That(path.GetProperty("value").GetString(), Is.EqualTo("/Applications/Calculator.app"));
			Assert.That(path.GetProperty("type").GetString(), Is.EqualTo("file"));
			Assert.That(mode.GetProperty("type").GetString(), Is.EqualTo("choice"));
			Assert.That(mode.GetProperty("value").GetString(), Is.EqualTo("start"));
			Assert.That(mode.GetProperty("options").EnumerateArray().Count(), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Handle_CarriesTheArgumentsTheSourceDeclared()
	{
		_resolver.Result = new ResolvedApplication("/usr/bin/firefox", "Firefox", "--new-window");

		await _handler.Handle(Request(), CancellationToken.None);

		using var data = JsonDocument.Parse(_widgets.Created.Single().Data!);
		using var flows = JsonDocument.Parse(data.RootElement.GetProperty("flows").GetString()!);
		var parameters = flows.RootElement[0].GetProperty("children")[0].GetProperty("parameters");
		var arguments = parameters.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "arguments");

		Assert.That(arguments.GetProperty("value").GetString(), Is.EqualTo("--new-window"));
	}

	[Test]
	public async Task Handle_AssignsTheExtractedIconOnceItIsReady()
	{
		_extractor.Icon = new ExtractedAppIcon([1, 2, 3], "Calculator.png");
		var iconId = _iconImports.WillImport(IconProcessingState.Ready, _iconCache);

		await _handler.Handle(Request(), CancellationToken.None);

		using var data = JsonDocument.Parse(_widgets.Created.Single().Data!);
		var icon = data.RootElement.GetProperty("icon");
		Assert.Multiple(() =>
		{
			Assert.That(icon.GetProperty("type").GetString(), Is.EqualTo("icon-pack"));
			Assert.That(icon.GetProperty("reference").GetString(), Is.EqualTo(iconId.ToString()));
			Assert.That(data.RootElement.TryGetProperty("iconId", out _), Is.False);
			Assert.That(data.RootElement.TryGetProperty("states", out _), Is.False);
		});
	}

	// A failed conversion must not leave the button pointing at an icon that will never render.
	[Test]
	public async Task Handle_FailedIcon_StillCreatesTheButton()
	{
		_extractor.Icon = new ExtractedAppIcon([1, 2, 3], "Calculator.png");
		_iconImports.WillImport(IconProcessingState.Failed, _iconCache);

		var response = await _handler.Handle(Request(), CancellationToken.None);

		using var data = JsonDocument.Parse(_widgets.Created.Single().Data!);
		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(data.RootElement.TryGetProperty("iconId", out _), Is.False);
			Assert.That(data.RootElement.TryGetProperty("states", out _), Is.False);
		});
	}

	[Test]
	public async Task Handle_UnsupportedFile_CreatesNothing()
	{
		_resolver.Result = null;

		var response = await _handler.Handle(Request(path: "/tmp/notes.md"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.ValidationError)));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Does.Contain("notes.md"));
			Assert.That(_widgets.Created, Is.Empty);
			Assert.That(_extractor.Requested, Is.Empty);
		});
	}

	[Test]
	public async Task Handle_OccupiedCell_CreatesNothing()
	{
		_folder.Widgets.Add(new WidgetEntity
		{
			Id = Guid.NewGuid(), Type = WidgetTypeIds.ActionButton, PositionX = 2, PositionY = 1, Width = 1, Height = 1
		});

		var response = await _handler.Handle(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.PositionOccupied)));
			Assert.That(_widgets.Created, Is.Empty);
		});
	}

	[Test]
	public async Task Handle_UnknownFolder_CreatesNothing()
	{
		var response = await _handler.Handle(new CreateWidgetFromApplicationRequest
				{ FolderId = Guid.NewGuid().ToString(), Path = "/a.app" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(WidgetError.FolderNotFound)));
			Assert.That(_widgets.Created, Is.Empty);
		});
	}

	private sealed class FakeApplicationPathResolver : IApplicationPathResolver
	{
		public ResolvedApplication? Result { get; set; }

		public ResolvedApplication? Resolve(string path) => Result;
	}

	private sealed class SystemLikeIntegration : IIntegration
	{
		public string Id => "app.macro-deck.system";
		public LocalizedText Name => "System";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions { get; } = [new LaunchApplicationLikeAction()];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class LaunchApplicationLikeAction : IActionDefinition
	{
		public string Id => "launch-application";
		public LocalizedText Name => "Launch Application";
		public LocalizedText Description => "Starts an application.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			ActionParameter.File("path", label: "Application", required: true, fileExtensions: ["exe", "app"]),
			ActionParameter.Text("arguments", label: "Arguments"),
			ActionParameter.Folder("workingDirectory", label: "Working Directory"),
			ActionParameter.Choice("mode",
				options:
				[
					new ActionParameterOption { Value = "start", Label = "Start" },
					new ActionParameterOption { Value = "start-stop", Label = "Start / Stop Toggle" }
				],
				label: "Mode",
				defaultValue: "start")
		];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}

	private sealed class StubAppIconExtractor : IAppIconExtractor
	{
		public ExtractedAppIcon? Icon { get; set; }

		public List<string> Requested { get; } = [];

		public List<string> RequestedContent { get; } = [];

		public bool CanExtract(string path) => Icon is not null;

		public Task<Result<ExtractedAppIcon, IconError>> Extract(string path, CancellationToken cancellationToken)
		{
			Requested.Add(path);
			return Task.FromResult(Icon is null
				? Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat, "no icon")
				: Result.Ok<ExtractedAppIcon, IconError>(Icon));
		}

		public bool CanExtractContent(string fileName) => Icon is not null;

		public Task<Result<ExtractedAppIcon, IconError>> ExtractFromContent(string fileName,
			Stream content,
			CancellationToken cancellationToken)
		{
			RequestedContent.Add(fileName);
			return Task.FromResult(Icon is null
				? Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat, "no icon")
				: Result.Ok<ExtractedAppIcon, IconError>(Icon));
		}
	}

	private sealed class StubIconImportService : IIconImportService
	{
		private IconEntity? _icon;

		public Guid WillImport(IconProcessingState state, StubIconPackCache cache)
		{
			_icon = new IconEntity
			{
				Id = Guid.NewGuid(),
				PackId = Guid.NewGuid(),
				Name = "Calculator",
				ProcessingState = state
			};

			cache.Add(_icon);
			return _icon.Id;
		}

		public Task<Result<SingleIconImportResult, IconError>> ImportSingle(Guid? packId,
			IconImportFile file,
			CancellationToken cancellationToken)
			=> Task.FromResult(_icon is null
				? Result.Fail<SingleIconImportResult, IconError>(IconError.ValidationError, "nothing staged")
				: Result.Ok<SingleIconImportResult, IconError>(new SingleIconImportResult(_icon, Reused: false)));

		public Task<Result<IconImportBatchEntity, IconError>> Import(Guid? packId,
			string? sourceName,
			IAsyncEnumerable<IconImportFile> files,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<IconImportBatchEntity, IconError>> ImportFromPath(Guid? packId,
			IReadOnlyList<string> paths,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<IconPackImportResult, IconError>> ImportPacks(IAsyncEnumerable<IconImportFile> files,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<bool> CancelBatch(Guid batchId, CancellationToken cancellationToken)
			=> throw new NotSupportedException();
	}

	private sealed class StubIconPackCache : IIconPackCache
	{
		private readonly Dictionary<Guid, IconEntity> _icons = [];

		public void Add(IconEntity icon) => _icons[icon.Id] = icon;

		public IconEntity? GetIconById(Guid iconId) => _icons.GetValueOrDefault(iconId);

		public Task InitializeCache() => Task.CompletedTask;

		public IconPackEntity? GetPackById(Guid id) => null;

		public List<IconPackEntity> GetAllPacks() => [];

		public IconPackEntity? GetDefaultPack() => null;

		public Task AddOrUpdatePack(IconPackEntity pack) => Task.CompletedTask;

		public Task RemovePack(Guid id) => Task.CompletedTask;

		public List<IconEntity> GetIconsByPackId(Guid packId) => [];

		public List<IconEntity> GetIconsByBatchId(Guid batchId) => [];

		public List<IconEntity> GetIconsByState(params IconProcessingState[] states) => [];

		public int GetIconCount(Guid packId) => 0;

		public IconEntity? FindBySourceContentHash(SourceContentHash hash, Guid? withinPackId = null) => null;

		public IconEntity? FindByMasterContentHash(MasterContentHash hash, Guid? withinPackId = null) => null;

		public List<IconEntity> GetIconsMissingMasterContentHash() => [];

		public Task AddIcons(Guid packId, IReadOnlyList<IconEntity> icons) => Task.CompletedTask;

		public Task UpdateIcon(IconEntity icon) => Task.CompletedTask;

		public Task RemoveIcon(Guid iconId) => Task.CompletedTask;

		public Task RemoveIcons(Guid packId, IReadOnlyList<Guid> iconIds) => Task.CompletedTask;

		public Task FlushPendingWrites() => Task.CompletedTask;
	}

	private sealed class RecordingWidgetService : IWidgetService
	{
		private readonly StubFolderCache _folders;

		public RecordingWidgetService(StubFolderCache folders)
		{
			_folders = folders;
		}

		public List<WidgetEntity> Created { get; } = [];

		public Task<Result<WidgetEntity, WidgetError>> Create(Guid folderId,
			WidgetEntity widget,
			Guid? sourceWidgetId = null)
		{
			if (_folders.GetFolderById(folderId) is null)
			{
				return Task.FromResult(Result.Fail<WidgetEntity, WidgetError>(WidgetError.FolderNotFound, "no folder"));
			}

			widget.Id = Guid.NewGuid();
			widget.FolderId = folderId;
			Created.Add(widget);
			return Task.FromResult(Result.Ok<WidgetEntity, WidgetError>(widget));
		}

		public Task<Result<WidgetEntity, WidgetError>> Update(WidgetEntity widget) => throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> UpdatePositions(Guid folderId,
			IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> Delete(Guid widgetId, Guid folderId) => throw new NotSupportedException();

		public Task<Result<WidgetEntity, WidgetError>> SetPinned(Guid folderId,
			Guid widgetId,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> CreateMany(Guid folderId,
			IReadOnlyList<WidgetEntity> widgets,
			IReadOnlyList<Guid>? replaceIds = null,
			IReadOnlyList<Guid?>? sourceWidgetIds = null)
			=> throw new NotSupportedException();

		public Task<Result<WidgetError>> DeleteMany(Guid folderId, IReadOnlyList<Guid> widgetIds)
			=> throw new NotSupportedException();

		public Task<Result<List<WidgetEntity>, WidgetError>> SetPinnedMany(
			Guid folderId,
			IReadOnlyList<Guid> widgetIds,
			bool pinned,
			PinScope? scope = null)
			=> throw new NotSupportedException();
	}
}
