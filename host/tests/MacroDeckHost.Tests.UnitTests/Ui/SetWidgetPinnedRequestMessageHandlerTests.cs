using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class SetWidgetPinnedRequestMessageHandlerTests
{
	private PinScopeFixture _fixture = null!;
	private SetWidgetPinnedRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_fixture = new PinScopeFixture();
		_handler = new SetWidgetPinnedRequestMessageHandler(_fixture.WidgetService);
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task AbsentScope_OnAFreshPinRequest_ResolvesToProfileNotSubtree()
	{
		var w = _fixture.AddWidget(_fixture.Main, 0, 0);
		_fixture.AddWidget(_fixture.Mail, 0, 0);

		var response = await _handler.Handle(new SetWidgetPinnedRequest
			{
				WidgetId = w.Id.ToString(),
				FolderId = _fixture.Main.Id.ToString(),
				Pinned = true,
				Scope = null
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(WidgetError.PositionOccupied.ToString()));
			Assert.That(TestLocalization.Resolve(response.Error!.Message), Does.Contain("Mail"));
		});
		Assert.That(_fixture.Reload(_fixture.Main).Widgets.Single().IsPinned, Is.False);
	}
}
