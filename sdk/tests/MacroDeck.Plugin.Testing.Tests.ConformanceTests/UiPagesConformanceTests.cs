using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

[TestFixture]
internal sealed class UiPagesConformanceTests
{
	[Test]
	public async Task Pages_without_a_page_surface_make_the_page_check_fail()
	{
		var outcome = await RunAsync(new PagesIntegration(UiSurfaceKinds.Widget, "scripts"));

		Assert.That(outcome, Is.EqualTo(ConformanceOutcome.Failed));
	}

	[Test]
	public async Task A_malformed_page_id_makes_the_page_check_fail()
	{
		var outcome = await RunAsync(new PagesIntegration(UiSurfaceKinds.Page, "my scripts"));

		Assert.That(outcome, Is.EqualTo(ConformanceOutcome.Failed));
	}

	[Test]
	public async Task Pages_with_a_page_surface_pass_the_page_check()
	{
		var outcome = await RunAsync(new PagesIntegration(UiSurfaceKinds.Page, "scripts"));

		Assert.That(outcome, Is.EqualTo(ConformanceOutcome.Passed));
	}

	private static async Task<ConformanceOutcome> RunAsync(PagesIntegration integration)
	{
		var subject = ConformanceSubject.InProcess(builder => builder.RegisterIntegration(_ => integration),
			new PluginTestManifest("app.macro-deck.pages-test-plugin", "Pages Test Plugin", "1.0.0"));
		var runner = new ConformanceRunner(new ConformanceOptions { Ids = ["MDC0316"] });

		try
		{
			var report = await runner.RunAsync(subject);

			return report.Results.Single(result => result.Id == "MDC0316").Result.Outcome;
		}
		finally
		{
			await subject.DisposeAsync();
		}
	}

	private sealed class PagesIntegration(string surfaceKind, string pageId) : IPluginIntegration, IUiProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
			[new() { Kind = surfaceKind, SessionMode = UiSessionModes.Exclusive }];

		public IReadOnlyList<UiPageDeclaration> Pages => [new() { Id = pageId, Title = "Scripts" }];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(null);
		}
	}
}
