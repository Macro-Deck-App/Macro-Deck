using System.Globalization;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Feedback;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Feedback;

public interface IGitHubStarPromptService
{
	Task<GetGitHubStarPromptResponse> GetPrompt();

	Task MarkShown(CancellationToken cancellationToken);
}

public sealed class GitHubStarPromptService : IGitHubStarPromptService, IDisposable
{
	public const string ShownAtKey = "githubStarPrompt.shownAt";
	public static readonly TimeSpan MinimumInstallAge = TimeSpan.FromDays(7);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public GitHubStarPromptService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
	{
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
	}

	public async Task<GetGitHubStarPromptResponse> GetPrompt()
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		if (!string.IsNullOrWhiteSpace((await repository.GetByKey(ShownAtKey))?.Value))
		{
			return new GetGitHubStarPromptResponse();
		}

		var installation = await repository.GetByKey(AppPreferenceService.InstallationIdKey);
		if (installation is null)
		{
			return new GetGitHubStarPromptResponse();
		}

		// SetValue stores local time without an offset and the early seed stores UTC with one;
		// ToUniversalTime reads both correctly because SQLite hands them back as Unspecified or Local.
		var installedAt = new DateTimeOffset(installation.CreatedAt.ToUniversalTime(), TimeSpan.Zero);
		return new GetGitHubStarPromptResponse
		{
			Due = _timeProvider.GetUtcNow() - installedAt >= MinimumInstallAge
		};
	}

	public async Task MarkShown(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var repository = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
			if (!string.IsNullOrWhiteSpace((await repository.GetByKey(ShownAtKey))?.Value))
			{
				return;
			}

			await repository.SetValue(ShownAtKey,
				_timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();
}
