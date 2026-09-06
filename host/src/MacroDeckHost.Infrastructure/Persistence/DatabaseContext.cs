using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;
using MacroDeckHost.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public class DatabaseContext : DbContext
{
	private static readonly ILoggerFactory _loggerFactory = new LoggerFactory().AddSerilog();
	private static readonly SaveChangesSetTimestampsInterceptor _timestampsInterceptor = new();
	private static readonly string _migrationsAssembly = typeof(DatabaseContext).Assembly.GetName().Name!;
	private static int _pathLogged;

	private readonly ILogger _logger;
	private readonly IMacroDeckPaths _paths;

	public DatabaseContext(ILogger logger, IMacroDeckPaths paths)
	{
		_logger = logger;
		_paths = paths;
	}

	public DbSet<SecretEntity> Secrets => Set<SecretEntity>();

	public DbSet<IntegrationConfigEntryEntity> IntegrationConfigEntries => Set<IntegrationConfigEntryEntity>();

	public DbSet<AppPreferenceEntity> AppPreferences => Set<AppPreferenceEntity>();

	public DbSet<UserEntity> Users => Set<UserEntity>();

	public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

	public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

	public DbSet<PluginAccessTokenEntity> PluginAccessTokens => Set<PluginAccessTokenEntity>();

	public DbSet<PluginRegistrationEntity> PluginRegistrations => Set<PluginRegistrationEntity>();

	public DbSet<PluginTrustRecordEntity> PluginTrustRecords => Set<PluginTrustRecordEntity>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		var databasePath = _paths.DatabasePath;

		if (Interlocked.Exchange(ref _pathLogged, 1) == 0)
		{
			_logger.Information("Database path: {DbPath}", databasePath);
		}

		var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
		optionsBuilder.UseSqlite(connectionString, b => b.MigrationsAssembly(_migrationsAssembly))
			.UseLoggerFactory(_loggerFactory)
			.AddInterceptors(_timestampsInterceptor);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
		=> modelBuilder.ApplyConfigurationsFromAssembly(typeof(BaseEntityConfig<>).Assembly);
}
