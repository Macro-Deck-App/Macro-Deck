using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Api;

[TestFixture]
public class IntegrationIconControllerTests
{
	private const string IntegrationId = "test.plugin";

	private sealed class IconIntegration : IIntegration, IIntegrationIconProvider
	{
		public string Id { get; init; } = IntegrationId;
		public LocalizedText Name => "Icon Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public byte[] Icon { get; set; } = [1, 2, 3, 4];

		public string IconMimeType => "image/svg+xml";

		public byte[] GetIcon() => Icon;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;
		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private static IntegrationIconController CreateController(IIntegration integration,
		string? ifNoneMatch = null)
	{
		var controller = new IntegrationIconController(new ConfigurableIntegrationRegistry([integration]));
		var httpContext = new DefaultHttpContext();
		if (ifNoneMatch is not null)
		{
			httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;
		}

		controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
		return controller;
	}

	private static string ETagOf(IIntegration integration)
	{
		var controller = CreateController(integration);
		controller.GetIcon(integration.Id);
		return controller.Response.Headers.ETag.ToString();
	}

	// Issue #754: a plugin that ships a new icon keeps its id, so nothing in the request identifies
	// the icon - only the response may, and it must never let a client reuse the old bytes untold.
	[Test]
	public void A_changed_icon_is_served_under_a_new_identity_that_a_client_must_revalidate()
	{
		var integration = new IconIntegration();
		var before = ETagOf(integration);

		integration.Icon = [9, 8, 7, 6, 5];
		var controller = CreateController(integration);
		var result = controller.GetIcon(IntegrationId);

		Assert.That(result, Is.InstanceOf<FileContentResult>());
		Assert.Multiple(() =>
		{
			Assert.That(((FileContentResult)result).FileContents, Is.EqualTo(new byte[] { 9, 8, 7, 6, 5 }));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.Not.EqualTo(before));
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"));
		});
	}

	// The revalidation a changed icon needs must not become a re-download for an unchanged one.
	[Test]
	public void An_unchanged_icon_is_not_sent_again_to_a_client_that_already_has_it()
	{
		var integration = new IconIntegration();
		var etag = ETagOf(integration);

		var controller = CreateController(integration, ifNoneMatch: etag);
		var result = controller.GetIcon(IntegrationId);

		Assert.That(result, Is.InstanceOf<StatusCodeResult>());
		Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status304NotModified));
	}

	[Test]
	public void A_client_holding_the_previous_icon_is_sent_the_new_one()
	{
		var integration = new IconIntegration();
		var stale = ETagOf(integration);

		integration.Icon = [42];
		var controller = CreateController(integration, ifNoneMatch: stale);
		var result = controller.GetIcon(IntegrationId);

		Assert.That(result, Is.InstanceOf<FileContentResult>());
		Assert.That(((FileContentResult)result).FileContents, Is.EqualTo(new byte[] { 42 }));
	}

	[Test]
	public void An_integration_without_an_icon_has_none_to_serve()
	{
		var controller = CreateController(new FakeIntegration { Id = IntegrationId });

		Assert.That(controller.GetIcon(IntegrationId), Is.InstanceOf<NotFoundResult>());
	}
}
