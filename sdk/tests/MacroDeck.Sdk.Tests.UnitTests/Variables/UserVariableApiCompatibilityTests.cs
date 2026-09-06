using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableApiCompatibilityTests
{
	/// <summary>
	/// The compatibility commitment for a plugin-facing interface: an implementation compiled against the
	/// SDK that had only <c>ApplyAsync</c> still builds and still runs. It must also not claim to have
	/// created anything, which a default returning <c>Created()</c> would.
	/// </summary>
	[Test]
	public async Task An_implementation_written_before_CreateAsync_reports_that_it_does_not_support_it()
	{
		IUserVariableApi api = new ApplyOnlyUserVariableApi();

		var result = await api.CreateAsync("greeting", null, VariableType.Text);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableCreateStatus.NotSupported));
			Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
		});
	}

	private sealed class ApplyOnlyUserVariableApi : IUserVariableApi
	{
		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(UserVariableWriteResult.Applied());
	}
}
