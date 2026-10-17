using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;

public class DeviceEntityConfig : BaseEntityConfig<DeviceEntity>
{
	public DeviceEntityConfig()
		: base("device", "d_")
	{
	}

	public override void Configure(EntityTypeBuilder<DeviceEntity> builder)
	{
		base.Configure(builder);

		builder.Property(x => x.SecretHash).HasColumnName(ColumnPrefix + "secret_hash");
		builder.Property(x => x.Name).HasColumnName(ColumnPrefix + "name");
		builder.Property(x => x.NameIsCustom)
			.HasColumnName(ColumnPrefix + "name_is_custom")
			.HasDefaultValue(false)
			.ValueGeneratedNever();
		builder.Property(x => x.ProposedName).HasColumnName(ColumnPrefix + "proposed_name");
		builder.Property(x => x.ClientType)
			.HasColumnName(ColumnPrefix + "client_type")
			.HasDefaultValue(default(DeviceClientType))
			.ValueGeneratedNever();
		builder.Property(x => x.FormFactor)
			.HasColumnName(ColumnPrefix + "form_factor")
			.HasDefaultValue(default(DeviceFormFactor))
			.ValueGeneratedNever();
		builder.Property(x => x.Platform).HasColumnName(ColumnPrefix + "platform");
		builder.Property(x => x.Browser).HasColumnName(ColumnPrefix + "browser");
		builder.Property(x => x.AppVersion).HasColumnName(ColumnPrefix + "app_version");
		builder.Property(x => x.LastSeenAt).HasColumnName(ColumnPrefix + "last_seen_at");
		builder.Property(x => x.SessionsRevokedAt).HasColumnName(ColumnPrefix + "sessions_revoked_at");
		builder.Property(x => x.StartupProfileId).HasColumnName(ColumnPrefix + "startup_profile_id");
		builder.Property(x => x.ScreenSaverEnabled)
			.HasColumnName(ColumnPrefix + "screensaver_enabled")
			.HasDefaultValue(false)
			.ValueGeneratedNever();
		builder.Property(x => x.ScreenSaverIdleSeconds)
			.HasColumnName(ColumnPrefix + "screensaver_idle_seconds")
			.HasDefaultValue(300)
			.ValueGeneratedNever();
		builder.Property(x => x.ScreenSaverId).HasColumnName(ColumnPrefix + "screensaver_id");
		builder.Property(x => x.ScreenSaverConfiguration).HasColumnName(ColumnPrefix + "screensaver_configuration");
		builder.Property(x => x.SettingsButtonHidden)
			.HasColumnName(ColumnPrefix + "settings_button_hidden")
			.HasDefaultValue(false)
			.ValueGeneratedNever();
		builder.Property(x => x.ProviderId).HasColumnName(ColumnPrefix + "provider_id");
		builder.Property(x => x.ProviderDeviceId).HasColumnName(ColumnPrefix + "provider_device_id");
		builder.Property(x => x.Model).HasColumnName(ColumnPrefix + "model");
		builder.Property(x => x.Manufacturer).HasColumnName(ColumnPrefix + "manufacturer");
		builder.Property(x => x.LayoutReference).HasColumnName(ColumnPrefix + "layout_reference");
		builder.Property(x => x.Capabilities).HasColumnName(ColumnPrefix + "capabilities");
		builder.Property(x => x.LayoutSnapshot).HasColumnName(ColumnPrefix + "layout_snapshot");
		builder.Ignore(x => x.IsProviderDevice);

		builder.HasIndex(x => x.LastSeenAt).HasDatabaseName("ix_device_last_seen");
		builder.HasIndex(x => new { x.ProviderId, x.ProviderDeviceId })
			.IsUnique()
			.HasDatabaseName("ux_device_provider_identity");
	}
}
