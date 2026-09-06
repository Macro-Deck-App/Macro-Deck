using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class FolderServiceDuplicateVariableTests
{
	private ProfileCache _cache = null!;
	private VariableService _variables = null!;
	private FolderService _service = null!;
	private Guid _folderId;
	private Guid _widgetId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		var mediator = new RecordingMediator();
		_variables = TestVariableServices.Create(new VariableRegistry(), new NullUserVariableStore(), mediator);

		var secrets = new FakeSecretService();
		_service = new FolderService(new FolderCache(_cache),
			_cache,
			new InMemoryDeviceRepository(),
			mediator,
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new WidgetVariableCloner(_variables, new LoggerConfiguration().CreateLogger()),
			TestFolderViewProviders.Registry());

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
		_widgetId = Guid.NewGuid();
		await _cache.AddOrUpdate(new ProfileEntity { Id = profileId, Name = "P" });
		await _cache.AddOrUpdateFolder(new FolderEntity
		{
			Id = _folderId,
			ProfileId = profileId,
			Name = "F",
			Order = 0,
			Rows = 4,
			Columns = 4
		});

		_cache.AddWidget(_folderId,
			new WidgetEntity
			{
				Id = _widgetId,
				Type = WidgetTypeIds.ActionButton,
				PositionX = 0,
				PositionY = 0,
				Width = 1,
				Height = 1,
				FolderId = _folderId
			});
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Duplicate_CarriesEachWidgetsUserVariablesOntoItsDuplicate()
	{
		await _variables.CreateUserVariable("presses",
			VariableScope.Widget,
			_widgetId.ToString(),
			VariableType.Numeric,
			"12.50",
			2);

		var result = await _service.Duplicate(_folderId);

		Assert.That(result.Success, Is.True);
		var duplicateWidget = result.Data!.Widgets.Single();
		var duplicated = await _variables.GetByScope(VariableScope.Widget, duplicateWidget.Id.ToString());
		var original = await _variables.GetByScope(VariableScope.Widget, _widgetId.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(duplicateWidget.Id, Is.Not.EqualTo(_widgetId));
			Assert.That(duplicated, Has.Count.EqualTo(1));
			Assert.That(duplicated[0].Name, Is.EqualTo("presses"));
			Assert.That(duplicated[0].Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(duplicated[0].Value, Is.EqualTo("12.50"));
			Assert.That(duplicated[0].DecimalPlaces, Is.EqualTo(2));
			Assert.That(duplicated[0].Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(original, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Duplicate_DoesNotCarryHostDerivedWidgetState()
	{
		await _variables.UpsertWidgetVariable(VariableScope.Widget,
			_widgetId.ToString(),
			"state",
			VariableType.Text,
			"on");

		var result = await _service.Duplicate(_folderId);

		Assert.That(result.Success, Is.True);
		var duplicateWidget = result.Data!.Widgets.Single();
		var duplicated = await _variables.GetByScope(VariableScope.Widget, duplicateWidget.Id.ToString());
		Assert.That(duplicated, Is.Empty);
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
