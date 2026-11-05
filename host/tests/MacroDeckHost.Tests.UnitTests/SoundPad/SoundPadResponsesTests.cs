using MacroDeckHost.Integrations.SoundPad.Protocol;
using SoundpadConnector.Response;

namespace MacroDeckHost.Tests.UnitTests.SoundPad;

[TestFixture]
public class SoundPadResponsesTests
{
	[TestCase("1", true)]
	[TestCase("0", false)]
	[TestCase("True", true)]
	[TestCase("False", false)]
	public void TheMuteReply_IsReadAsSoundPadSendsIt(string reply, bool muted)
	{
		Assert.That(SoundPadResponses.Muted(Reply(reply)), Is.EqualTo(muted));
	}

	[TestCase("R-500: Internal error")]
	[TestCase("")]
	public void ARejectedMuteQuery_IsAnError(string reply)
	{
		Assert.That(() => SoundPadResponses.Muted(Reply(reply)), Throws.TypeOf<SoundPadCommandException>());
	}

	private static BooleanResponse Reply(string wire)
	{
		var response = new BooleanResponse();
		response.Parse(wire);
		return response;
	}
}
