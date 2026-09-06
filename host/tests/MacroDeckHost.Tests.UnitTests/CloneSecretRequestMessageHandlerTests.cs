using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Secrets;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
public class CloneSecretRequestMessageHandlerTests
{
	private FakeSecretService _secrets = null!;
	private CloneSecretRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_secrets = new FakeSecretService();
		_handler = new CloneSecretRequestMessageHandler(_secrets);
	}

	[Test]
	public async Task ExistingSecret_IsClonedToAnIndependentCopyWithTheSameValue()
	{
		var original = _secrets.Store("hunter2");

		var response = await _handler.Handle(new CloneSecretRequest { Id = original }, CancellationToken.None);

		Assert.That(response.Id, Is.Not.Null);
		Assert.That(response.Id, Is.Not.EqualTo(original));
		Assert.That(response.Error, Is.Null);
		Assert.That(await _secrets.Resolve(response.Id!.Value), Is.EqualTo("hunter2"));
	}

	[Test]
	public async Task MissingSecret_ReturnsNotFoundError()
	{
		var response = await _handler.Handle(new CloneSecretRequest { Id = Guid.NewGuid() }, CancellationToken.None);

		Assert.That(response.Id, Is.Null);
		Assert.That(response.Error, Is.Not.Null);
		Assert.That(response.Error!.Code, Is.EqualTo("SECRET_NOT_FOUND"));
	}
}
