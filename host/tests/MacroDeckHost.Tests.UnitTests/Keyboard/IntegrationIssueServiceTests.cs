using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class IntegrationIssueServiceTests
{
	private static readonly string[] _expectedReinitializeCalls = ["plain"];

	private static IntegrationIssueService CreateService(
		FakeIntegrationRegistry registry,
		FakeIntegrationHostIssueStore? hostIssueStore = null,
		FakeIntegrationLifecycle? lifecycle = null)
		=> new(registry,
			hostIssueStore ?? new FakeIntegrationHostIssueStore(),
			lifecycle ?? new FakeIntegrationLifecycle());

	[Test]
	public async Task Returns_issues_for_an_enabled_issue_provider()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new IssueIntegration());
		var service = CreateService(registry);

		var issues = await service.GetIssuesAsync("issues");

		Assert.That(issues, Has.Count.EqualTo(1));
		Assert.That(issues[0].Id, Is.EqualTo("problem"));
	}

	[Test]
	public async Task Returns_empty_for_unknown_integration()
	{
		var service = CreateService(new FakeIntegrationRegistry());
		Assert.That(await service.GetIssuesAsync("nope"), Is.Empty);
	}

	[Test]
	public async Task Returns_empty_for_integration_without_issue_capability()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new PlainIntegration());
		var service = CreateService(registry);

		Assert.That(await service.GetIssuesAsync("plain"), Is.Empty);
	}

	[Test]
	public async Task Resolve_delegates_to_provider()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new IssueIntegration());
		var service = CreateService(registry);

		var resolution = await service.ResolveAsync("issues", "problem");

		Assert.That(resolution, Is.Not.Null);
		Assert.That(resolution!.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
	}

	[Test]
	public async Task A_startup_failure_is_reported_for_an_integration_without_an_issue_provider()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new PlainIntegration());
		registry.Add(new IssueIntegration());
		registry.Add(new PlainIntegration { Id = "plain-success" });
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		hostIssueStore.RaiseStartupFailure("plain", "boom");
		hostIssueStore.RaiseStartupFailure("issues", "boom");
		var service = CreateService(registry, hostIssueStore);

		var plainIssues = await service.GetIssuesAsync("plain");
		var issuesIssues = await service.GetIssuesAsync("issues");
		var successIssues = await service.GetIssuesAsync("plain-success");

		Assert.Multiple(() =>
		{
			Assert.That(plainIssues.Select(i => i.Id), Is.EquivalentTo(new[] { IntegrationHostIssueIds.Startup }));
			Assert.That(issuesIssues.Select(i => i.Id),
				Is.EquivalentTo(new[] { IntegrationHostIssueIds.Startup, "problem" }));
			Assert.That(successIssues, Is.Empty);
		});
	}

	[Test]
	public async Task Try_again_reinitializes_the_integration_and_clears_the_startup_issue()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new PlainIntegration());
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		hostIssueStore.RaiseStartupFailure("plain", "boom");
		var lifecycle = new FakeIntegrationLifecycle
		{
			OnReinitialize = _ =>
			{
				hostIssueStore.Clear("plain");
				return Task.CompletedTask;
			}
		};
		var service = CreateService(registry, hostIssueStore, lifecycle);

		var resolution = await service.ResolveAsync("plain", IntegrationHostIssueIds.Startup);

		Assert.That(resolution, Is.Not.Null);
		Assert.That(resolution!.Success, Is.True);
		Assert.That(lifecycle.ReinitializeCalls, Is.EqualTo(_expectedReinitializeCalls));
		Assert.That(await service.GetIssuesAsync("plain"), Is.Empty);
	}

	[Test]
	public async Task Try_again_leaves_the_issue_when_the_retry_fails_again()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new PlainIntegration());
		var hostIssueStore = new FakeIntegrationHostIssueStore();
		hostIssueStore.RaiseStartupFailure("plain", "boom");
		var lifecycle = new FakeIntegrationLifecycle
		{
			OnReinitialize = _ =>
			{
				hostIssueStore.RaiseStartupFailure("plain", "boom again");
				return Task.CompletedTask;
			}
		};
		var service = CreateService(registry, hostIssueStore, lifecycle);

		var resolution = await service.ResolveAsync("plain", IntegrationHostIssueIds.Startup);

		Assert.That(resolution, Is.Not.Null);
		Assert.That(resolution!.Success, Is.False);
		Assert.That(await service.GetIssuesAsync("plain"), Has.Count.EqualTo(1));
	}

	private sealed class IssueIntegration : IIntegration, IIntegrationIssueProvider
	{
		public string Id => "issues";
		public LocalizedText Name => "Issues";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
			=> Task.FromResult<IReadOnlyList<IntegrationIssue>>([
				new IntegrationIssue { Id = "problem", Title = "A problem", ActionLabel = "Fix" }
			]);

		public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
			=> Task.FromResult(IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow));
	}

	private sealed class PlainIntegration : IIntegration
	{
		public string Id { get; init; } = "plain";
		public LocalizedText Name => "Plain";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
