using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MacroDeckHost.DatabaseMigrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_preference",
                columns: table => new
                {
                    ap_key = table.Column<string>(type: "TEXT", nullable: false),
                    ap_value = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    ap_created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ap_updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_preference", x => x.ap_key);
                });

            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    u_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    u_username = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    u_password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    u_updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    u_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.u_id);
                });

            migrationBuilder.CreateTable(
                name: "device",
                columns: table => new
                {
                    d_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    d_secret_hash = table.Column<string>(type: "TEXT", nullable: false),
                    d_name = table.Column<string>(type: "TEXT", nullable: false),
                    d_name_is_custom = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    d_proposed_name = table.Column<string>(type: "TEXT", nullable: true),
                    d_client_type = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    d_form_factor = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    d_platform = table.Column<string>(type: "TEXT", nullable: true),
                    d_browser = table.Column<string>(type: "TEXT", nullable: true),
                    d_app_version = table.Column<string>(type: "TEXT", nullable: true),
                    d_last_seen_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    d_sessions_revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    d_startup_profile_id = table.Column<string>(type: "TEXT", nullable: true),
                    d_screensaver_enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    d_screensaver_idle_seconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 300),
                    d_screensaver_id = table.Column<string>(type: "TEXT", nullable: true),
                    d_screensaver_configuration = table.Column<string>(type: "TEXT", nullable: true),
                    d_settings_button_hidden = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    d_provider_id = table.Column<string>(type: "TEXT", nullable: true),
                    d_provider_device_id = table.Column<string>(type: "TEXT", nullable: true),
                    d_model = table.Column<string>(type: "TEXT", nullable: true),
                    d_manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                    d_layout_reference = table.Column<string>(type: "TEXT", nullable: true),
                    d_capabilities = table.Column<string>(type: "TEXT", nullable: true),
                    d_layout_snapshot = table.Column<string>(type: "TEXT", nullable: true),
                    d_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device", x => x.d_id);
                });

            migrationBuilder.CreateTable(
                name: "integration_config_entry",
                columns: table => new
                {
                    ice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ice_integration_id = table.Column<string>(type: "TEXT", nullable: false),
                    ice_title = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    ice_values_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    ice_updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ice_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_config_entry", x => x.ice_id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_access_token",
                columns: table => new
                {
                    pat_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pat_name = table.Column<string>(type: "TEXT", nullable: false),
                    pat_token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    pat_scopes = table.Column<string>(type: "TEXT", nullable: false),
                    pat_expires_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pat_last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pat_revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pat_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plugin_access_token", x => x.pat_id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_registration",
                columns: table => new
                {
                    pr_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    pr_plugin_id = table.Column<string>(type: "TEXT", nullable: false),
                    pr_display_name = table.Column<string>(type: "TEXT", nullable: false),
                    pr_secret_hash = table.Column<string>(type: "TEXT", nullable: false),
                    pr_access_token_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    pr_origin = table.Column<string>(type: "TEXT", nullable: false),
                    pr_last_seen_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pr_revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    pr_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plugin_registration", x => x.pr_id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_trust_record",
                columns: table => new
                {
                    ptr_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ptr_plugin_id = table.Column<string>(type: "TEXT", nullable: false),
                    ptr_version = table.Column<string>(type: "TEXT", nullable: false),
                    ptr_admitted_verdict = table.Column<string>(type: "TEXT", nullable: false),
                    ptr_certificate_id = table.Column<string>(type: "TEXT", nullable: true),
                    ptr_installed_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ptr_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plugin_trust_record", x => x.ptr_id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    rt_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rt_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rt_token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    rt_family_id = table.Column<Guid>(type: "TEXT", nullable: false, defaultValueSql: "''"),
                    rt_device_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rt_scope = table.Column<int>(type: "INTEGER", nullable: false),
                    rt_expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    rt_revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    rt_replaced_by_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rt_rotated_by_grace = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    rt_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.rt_id);
                });

            migrationBuilder.CreateTable(
                name: "secret",
                columns: table => new
                {
                    s_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    s_kind = table.Column<int>(type: "INTEGER", nullable: false),
                    s_encrypted_value = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    s_updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    s_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secret", x => x.s_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_app_user_username",
                table: "app_user",
                column: "u_username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_last_seen",
                table: "device",
                column: "d_last_seen_at");

            migrationBuilder.CreateIndex(
                name: "ux_device_provider_identity",
                table: "device",
                columns: new[] { "d_provider_id", "d_provider_device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_integration_config_entry_integration_id",
                table: "integration_config_entry",
                column: "ice_integration_id");

            migrationBuilder.CreateIndex(
                name: "ux_plugin_access_token_hash",
                table: "plugin_access_token",
                column: "pat_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plugin_registration_access_token",
                table: "plugin_registration",
                column: "pr_access_token_id");

            migrationBuilder.CreateIndex(
                name: "ux_plugin_registration_plugin_id",
                table: "plugin_registration",
                column: "pr_plugin_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plugin_trust_record_plugin_version",
                table: "plugin_trust_record",
                columns: new[] { "ptr_plugin_id", "ptr_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_device",
                table: "refresh_token",
                column: "rt_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_family",
                table: "refresh_token",
                column: "rt_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user",
                table: "refresh_token",
                column: "rt_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_token_hash",
                table: "refresh_token",
                column: "rt_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_preference");

            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "device");

            migrationBuilder.DropTable(
                name: "integration_config_entry");

            migrationBuilder.DropTable(
                name: "plugin_access_token");

            migrationBuilder.DropTable(
                name: "plugin_registration");

            migrationBuilder.DropTable(
                name: "plugin_trust_record");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "secret");
        }
    }
}
