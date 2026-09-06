using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public class CachingInitializeBackgroundService : HostReadyBackgroundService
{
	private readonly IProfileCache _profileCache;
	private readonly IFolderCache _folderCache;
	private readonly IScriptCache _scriptCache;
	private readonly IAutomationCache _automationCache;
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;

	public CachingInitializeBackgroundService(
		IHostApplicationLifetime lifetime,
		IProfileCache profileCache,
		IFolderCache folderCache,
		IScriptCache scriptCache,
		IAutomationCache automationCache,
		IServiceScopeFactory serviceScopeFactory,
		StartupReadiness readiness,
		ILogger logger)
		: base(lifetime)
	{
		_profileCache = profileCache;
		_folderCache = folderCache;
		_scriptCache = scriptCache;
		_automationCache = automationCache;
		_serviceScopeFactory = serviceScopeFactory;
		_readiness = readiness;
		_logger = logger;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _profileCache.InitializeCache();
		await _folderCache.InitializeCache();
		await _scriptCache.InitializeCache();
		await _automationCache.InitializeCache();

		if (_profileCache.GetAll().Count == 0)
		{
			if (_profileCache.HadUnreadableProfiles)
			{
				_logger.Error(
					"No profiles could be loaded but unreadable profile files were found; refusing to create a " +
					"Default Profile to avoid masking data loss");
			}
			else
			{
				await using var scope = _serviceScopeFactory.CreateAsyncScope();

				// Resolved to text here rather than kept as a reference: from this point on the name is a
				// profile the reader can rename, not a label the product owns, so it stays whatever it was
				// created as even after the language changes.
				var name = await ActiveLocalization.Resolve(scope.ServiceProvider, AppStrings.Profiles.DefaultName());
				await scope.ServiceProvider.GetRequiredService<IProfileService>().Create(name);
			}
		}

		_readiness.MarkCachesReady();
	}
}
