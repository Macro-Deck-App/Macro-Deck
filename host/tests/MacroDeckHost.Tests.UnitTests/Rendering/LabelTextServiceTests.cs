using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public class LabelTextServiceTests
{
	private static WidgetEntity Button(Guid id, string? data) => new()
	{
		Id = id,
		FolderId = Guid.NewGuid(),
		Type = WidgetTypeIds.ActionButton,
		Data = data
	};

	private static StartupReadiness ReadySignal()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private static LabelTextService Service(StartupReadiness readiness,
		VariableRegistry registry,
		params WidgetEntity[] widgets)
		=> new(new FakeFolderCache(widgets), new VariableTemplateRenderer(registry), readiness);

	[Test]
	public async Task ResolveText_ReturnsTheRootLabel_ForAStateModeOffButton()
	{
		// The exact stored shape a freshly created Action Button has: stateMode off, no states, a plain
		// root label. The deck's tile is served from this, so a null here is a button with no label.
		var widget = Button(Guid.NewGuid(),
			"{\"label\":\"E2E Action\",\"flows\":\"[]\",\"backgroundColor\":\"var(--color-accent)\",\"stateMode\":false}");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var text = await service.ResolveText(widget.Id, "off");

		Assert.That(text, Is.EqualTo("E2E Action"));
	}

	[Test]
	public async Task ResolveText_ResolvesTemplateAgainstRegistry()
	{
		var widget = Button(Guid.NewGuid(), "{\"label\":\"Hi {{ vars.greeting }}\"}");
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Name = "greeting",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Hello"
		});
		var service = Service(ReadySignal(), registry, widget);

		var text = await service.ResolveText(widget.Id, "off");

		Assert.That(text, Is.EqualTo("Hi Hello"));
	}

	[Test]
	public async Task ResolveText_PlainTextFilter_FoldsStyledUnicodeVariable()
	{
		// The exact styled string from issue #290: Mathematical Alphanumeric Symbols spelling
		// "CJHACKERYT". Left un-folded, the label mixes typefaces per character; the filter fixes it.
		var styled
			= "\U0001D49E\U0001D4A5\U0001D43B\U0001D49C\U0001D49E\U0001D4A6\U0001D438\U0001D445\U0001D4B4\U0001D4AF";
		var widget = Button(Guid.NewGuid(), "{\"label\":\"Server: {{ vars.guild | plain_text }}\"}");
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Name = "guild",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = styled
		});
		var service = Service(ReadySignal(), registry, widget);

		var text = await service.ResolveText(widget.Id, "off");

		Assert.That(text, Is.EqualTo("Server: CJHACKERYT"));
	}

	[Test]
	public async Task ResolveText_UnknownWidget_ReturnsNull()
	{
		var service = Service(ReadySignal(), new VariableRegistry());

		var text = await service.ResolveText(Guid.NewGuid(), "off");

		Assert.That(text, Is.Null);
	}

	[Test]
	public async Task ResolveText_WaitsForStartupReadiness()
	{
		var widget = Button(Guid.NewGuid(), "{\"label\":\"static\"}");
		var readiness = new StartupReadiness();
		var service = Service(readiness, new VariableRegistry(), widget);

		var pending = service.ResolveText(widget.Id, "off");
		await Task.Yield();
		Assert.That(pending.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		Assert.That(await pending, Is.EqualTo("static"));
	}

	[Test]
	public void ResolveText_CancelledBeforeReady_Throws()
	{
		var service = Service(new StartupReadiness(), new VariableRegistry());
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.ThrowsAsync<TaskCanceledException>(() => service.ResolveText(Guid.NewGuid(), "off", cts.Token));
	}

	[Test]
	public async Task ResolveText_ToggleMode_ResolvesEachStateFromStates()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"toggle","states":{"off":{"label":"Off"},"on":{"label":"On"}},"label":"Root"}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var on = await service.ResolveText(widget.Id, "on");
		var off = await service.ResolveText(widget.Id, "off");
		Assert.Multiple(() =>
		{
			Assert.That(on, Is.EqualTo("On"));
			Assert.That(off, Is.EqualTo("Off"));
		});
	}

	[Test]
	public async Task ResolveText_ToggleMode_OnStateWithAnEmptyLabel_FallsBackToRoot()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"toggle","states":{"off":{"label":"Off"},"on":{}},"label":"Root"}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var text = await service.ResolveText(widget.Id, "on");

		Assert.That(text, Is.EqualTo("Root"));
	}

	// The legacy upgrade normalizes a missing "on" key the same way it normalizes a present-but-empty
	// one (BuildLegacyPair treats both as "no appearance"), so this must resolve identically to
	// ResolveText_ToggleMode_OnStateWithAnEmptyLabel_FallsBackToRoot - the root label, not the other
	// face's label. Falling back to "off" would mean an id-absent and an id-empty face resolve
	// differently even though the upgraded data is byte-for-byte the same either way.
	[Test]
	public async Task ResolveText_ToggleMode_WithNoOnStateAtAll_FallsBackToRoot()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"toggle","states":{"off":{"label":"Off"}},"label":"Root"}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var text = await service.ResolveText(widget.Id, "on");

		Assert.That(text, Is.EqualTo("Root"));
	}

	[Test]
	public async Task ResolveText_ToggleMode_BothStatesEmpty_FallsBackToRoot()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"toggle","states":{"off":{"label":""},"on":{"label":""}},"label":"Root"}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var text = await service.ResolveText(widget.Id, "off");

		Assert.That(text, Is.EqualTo("Root"));
	}

	[Test]
	public async Task ResolveText_ToggleMode_LegacyAliases_StillResolve()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"toggle","offState":{"label":"Off"},"onState":{"label":"On"}}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var off = await service.ResolveText(widget.Id, "off");
		var on = await service.ResolveText(widget.Id, "on");
		Assert.Multiple(() =>
		{
			Assert.That(off, Is.EqualTo("Off"));
			Assert.That(on, Is.EqualTo("On"));
		});
	}

	[Test]
	public async Task ResolveText_ToggleMode_StatesWinsOverLegacyAliases()
	{
		var widget = Button(Guid.NewGuid(),
			"""
			{"mode":"toggle","states":{"off":{"label":"NEW"},"on":{"label":"NEW"}},
			"offState":{"label":"OLD"},"onState":{"label":"OLD"}}
			""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var off = await service.ResolveText(widget.Id, "off");
		var on = await service.ResolveText(widget.Id, "on");
		Assert.Multiple(() =>
		{
			Assert.That(off, Is.EqualTo("NEW"));
			Assert.That(on, Is.EqualTo("NEW"));
		});
	}

	[Test]
	public async Task ResolveText_MomentaryMode_IgnoresStates_UsesRootLabel()
	{
		var widget = Button(Guid.NewGuid(),
			"""{"mode":"momentary","label":"Root","states":{"off":{"label":"Ignored"},"on":{}}}""");
		var service = Service(ReadySignal(), new VariableRegistry(), widget);

		var text = await service.ResolveText(widget.Id, "off");

		Assert.That(text, Is.EqualTo("Root"));
	}

	// E1 - the issue's own case: a label guarded by "vars.artist.state.is_not_empty" must read availability,
	// not the rendered value. Case 3 is the one that matters: an implementation that instead checked whether
	// the *rendered* value was non-empty would answer "By n/v" here, because the stale value is still stored
	// alongside the unavailable flag.
	[Test]
	public async Task ResolveText_StateGuardedLabel_RendersOnlyWhenTheVariableIsAvailableAndNonEmpty()
	{
		const string label = "{% if vars.artist.state.is_not_empty %}By {{ vars.artist }}{% endif %}";

		async Task<string?> ResolveWith(VariableRegistry registry)
		{
			var widget = Button(Guid.NewGuid(), JsonSerializer.Serialize(new { label }));
			var service = Service(ReadySignal(), registry, widget);
			return await service.ResolveText(widget.Id, "off");
		}

		var availableRegistry = new VariableRegistry();
		availableRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "artist",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "Radiohead"
		});

		var emptyRegistry = new VariableRegistry();
		emptyRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "artist",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = string.Empty
		});

		var staleRegistry = new VariableRegistry();
		staleRegistry.Upsert(new VariableEntity
		{
			Id = Guid.CreateVersion7(),
			Name = "artist",
			Scope = VariableScope.Global,
			Type = VariableType.Text,
			Classification = VariableClassification.Integration,
			OwnerIntegrationId = "com.example.music",
			DefinitionId = "declared-var",
			Value = "Radiohead",
			UpdateMode = VariableUpdateMode.Polled,
			UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5),
			CreatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5)
		});

		var undeclaredRegistry = new VariableRegistry();

		var byAvailable = await ResolveWith(availableRegistry);
		var byEmpty = await ResolveWith(emptyRegistry);
		var byStale = await ResolveWith(staleRegistry);
		var byUndeclared = await ResolveWith(undeclaredRegistry);

		// ResolveText normalises an empty-rendered label to null throughout this service (see
		// ResolveText_UnknownWidget_ReturnsNull above) rather than returning "" - that is the "no label"
		// contract this button falls into for every case but the first.
		Assert.Multiple(() =>
		{
			Assert.That(byAvailable, Is.EqualTo("By Radiohead"));
			Assert.That(byEmpty, Is.Null);
			Assert.That(byStale,
				Is.Null,
				"reading the rendered value instead of availability would give \"By n/v\" here");
			Assert.That(byUndeclared, Is.Null);
		});
	}

	[Test]
	public void StartupReadiness_RequiresBothParts()
	{
		var readiness = new StartupReadiness();
		Assert.That(readiness.WhenReady.IsCompleted, Is.False);

		readiness.MarkCachesReady();
		Assert.That(readiness.WhenReady.IsCompleted, Is.False);

		readiness.MarkVariablesReady();
		Assert.That(readiness.WhenReady.IsCompleted, Is.True);
	}

	private sealed class FakeFolderCache : IFolderCache
	{
		private readonly FolderEntity _folder;

		public FakeFolderCache(params WidgetEntity[] widgets)
		{
			_folder = new FolderEntity { Name = "f", Order = 0, Widgets = widgets.ToList() };
		}

		public List<FolderEntity> GetAllFolders() => [_folder];

		public Task InitializeCache() => Task.CompletedTask;
		public FolderEntity? GetFolderById(Guid id) => _folder;
		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => [_folder];
		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => [_folder];
		public Task AddOrUpdate(FolderEntity folder) => Task.CompletedTask;

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => Task.CompletedTask;

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId)
			=> Task.FromResult(new FolderSubtreeRemoval(false, [], []));

		public void AddWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidget(Guid folderId, WidgetEntity widget)
		{
		}

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
		{
		}

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
		{
		}

		public void RemoveWidget(Guid folderId, Guid widgetId)
		{
		}

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds)
		{
		}

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
		{
		}
	}
}
