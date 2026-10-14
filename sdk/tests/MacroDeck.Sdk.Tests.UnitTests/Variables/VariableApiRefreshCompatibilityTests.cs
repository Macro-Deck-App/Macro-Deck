using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class VariableApiRefreshCompatibilityTests
{
	[Test]
	public void Refresh_requests_on_an_api_written_before_them_do_nothing()
	{
		IVariableApi api = new ApiWrittenBeforeRefresh();

		Assert.Multiple(() =>
		{
			Assert.That(() => api.RequestRefreshAsync(), Throws.Nothing);
			Assert.That(() => api.NotifyDeclarationsChangedAsync(), Throws.Nothing);
			Assert.That(api.RequestRefreshAsync().IsCompletedSuccessfully, Is.True);
		});
	}

	private sealed class ApiWrittenBeforeRefresh : IVariableApi
	{
		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle?> GetByNameAsync(string name)
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			throw new NotSupportedException();
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			throw new NotSupportedException();
		}

		public Task DeleteAsync(Guid variableId)
		{
			throw new NotSupportedException();
		}
	}
}
