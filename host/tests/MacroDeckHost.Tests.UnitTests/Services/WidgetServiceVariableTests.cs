using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Mediator;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Services;

[TestFixture]
public class WidgetServiceVariableTests
{
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private VariableService _variables = null!;
	private WidgetVariableDeletingMediator _mediator = null!;
	private WidgetService _service = null!;
	private Guid _folderId;

	[SetUp]
	public async Task SetUp()
	{
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_mediator = new WidgetVariableDeletingMediator();
		_variables = TestVariableServices.Create(new VariableRegistry(), new NullUserVariableStore(), _mediator);
		_mediator.Variables = _variables;

		var secretService = new FakeSecretService();
		_service = new WidgetService(_folderCache,
			_cache,
			_mediator,
			new WidgetSecretScrubber(secretService),
			new WidgetSecretCloner(secretService),
			new WidgetVariableCloner(_variables, new LoggerConfiguration().CreateLogger()));

		var profileId = Guid.NewGuid();
		_folderId = Guid.NewGuid();
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
	}

	[TearDown]
	public void TearDown() => _cache.Dispose();

	[Test]
	public async Task Create_WithSourceWidgetId_CopiesUserVariables_AndLeavesTheSourcesIntact()
	{
		var source = Widget(0, 0);
		_folderCache.AddWidget(_folderId, source);
		await _variables.CreateUserVariable("counter",
			VariableScope.Widget,
			source.Id.ToString(),
			VariableType.Numeric,
			5,
			null);

		var created = await _service.Create(_folderId, Widget(1, 0), source.Id);

		Assert.That(created.Success, Is.True);
		var targetVariables = await _variables.GetByScope(VariableScope.Widget, created.Data!.Id.ToString());
		var sourceVariables = await _variables.GetByScope(VariableScope.Widget, source.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(targetVariables, Has.Count.EqualTo(1));
			Assert.That(targetVariables[0].Name, Is.EqualTo("counter"));
			Assert.That(targetVariables[0].Type, Is.EqualTo(VariableType.Numeric));
			Assert.That(targetVariables[0].Value, Is.EqualTo("5"));
			Assert.That(sourceVariables, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Create_WithASourceWidgetIdThatNoLongerExists_Succeeds_WithNoVariables()
	{
		var result = await _service.Create(_folderId, Widget(0, 0), Guid.NewGuid());

		Assert.That(result.Success, Is.True);
		var variables = await _variables.GetByScope(VariableScope.Widget, result.Data!.Id.ToString());
		Assert.That(variables, Is.Empty);
	}

	[Test]
	public async Task Create_OnlyClonesUserClassifiedVariables()
	{
		var source = Widget(0, 0);
		_folderCache.AddWidget(_folderId, source);
		var scopeRefId = source.Id.ToString();
		await _variables.CreateUserVariable("user_var", VariableScope.Widget, scopeRefId, VariableType.Text, "u", null);
		await _variables.UpsertWidgetVariable(VariableScope.Widget, scopeRefId, "widget_var", VariableType.Text, "w");
		await _variables.CreateIntegrationVariable("some-integration",
			"integration_var",
			VariableScope.Widget,
			scopeRefId,
			VariableType.Text,
			"i",
			null);

		var created = await _service.Create(_folderId, Widget(1, 0), source.Id);

		Assert.That(created.Success, Is.True);
		var targetVariables = await _variables.GetByScope(VariableScope.Widget, created.Data!.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(targetVariables, Has.Count.EqualTo(1));
			Assert.That(targetVariables[0].Name, Is.EqualTo("user_var"));
		});
	}

	[Test]
	public async Task CreateMany_WithReplaceIdsNamingTheSameFolderSources_StillGivesTheNewWidgetsTheirVariables()
	{
		var sourceA = Widget(0, 0);
		var sourceB = Widget(1, 0);
		_folderCache.AddWidget(_folderId, sourceA);
		_folderCache.AddWidget(_folderId, sourceB);
		await _variables.CreateUserVariable("a_var",
			VariableScope.Widget,
			sourceA.Id.ToString(),
			VariableType.Text,
			"a",
			null);
		await _variables.CreateUserVariable("b_var",
			VariableScope.Widget,
			sourceB.Id.ToString(),
			VariableType.Text,
			"b",
			null);

		var result = await _service.CreateMany(_folderId,
			[Widget(0, 0), Widget(1, 0)],
			[sourceA.Id, sourceB.Id],
			[sourceA.Id, sourceB.Id]);

		Assert.That(result.Success, Is.True);
		var created = result.Data!;
		var createdA = created.Single(w => w.PositionX == 0);
		var createdB = created.Single(w => w.PositionX == 1);

		var variablesA = await _variables.GetByScope(VariableScope.Widget, createdA.Id.ToString());
		var variablesB = await _variables.GetByScope(VariableScope.Widget, createdB.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That(variablesA.Select(v => v.Name), Is.EquivalentTo(new[] { "a_var" }));
			Assert.That(variablesB.Select(v => v.Name), Is.EquivalentTo(new[] { "b_var" }));
		});
	}

	[Test]
	public async Task CreateMany_SourceWidgetIdsCountMismatch_FailsWithValidationError_AndCreatesNothing()
	{
		var result = await _service.CreateMany(_folderId,
			[Widget(0, 0), Widget(1, 0)],
			null,
			[Guid.NewGuid()]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(WidgetError.ValidationError));
			Assert.That(_folderCache.GetFolderById(_folderId)!.Widgets, Is.Empty);
		});
	}

	private static WidgetEntity Widget(int x, int y) => new()
	{
		Id = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		PositionX = x,
		PositionY = y,
		Width = 1,
		Height = 1
	};

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}

	// Mirrors ActionButtonStateVariableWidgetsDeletedHandler's real effect (deleting the widget-scoped
	// variables synchronously on WidgetsDeletedNotification), so the group cut+paste test actually
	// exercises the race the snapshot-before-publish ordering guards against.
	private sealed class WidgetVariableDeletingMediator : IMediator
	{
		public VariableService Variables { get; set; } = null!;

		public List<object> Published { get; } = new();

		public async ValueTask Publish<TNotification>(TNotification notification,
			CancellationToken cancellationToken = default)
			where TNotification : INotification
		{
			if (notification is not null)
			{
				Published.Add(notification);
			}

			if (notification is WidgetsDeletedNotification deleted)
			{
				foreach (var widgetId in deleted.WidgetIds)
				{
					await Variables.DeleteByScopeInstance(VariableScope.Widget, widgetId.ToString());
				}
			}
		}

		public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
		{
			Published.Add(notification);
			return default;
		}

		public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<object?> Send(object message, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamRequest<TResponse> request,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamCommand<TResponse> command,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
			IStreamQuery<TResponse> query,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();
	}
}
