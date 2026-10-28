using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MacroDeckHost.DatabaseMigrations
{
    /// <inheritdoc />
    public partial class AddPluginTrustRecordSigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ptr_signer_id",
                table: "plugin_trust_record",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ptr_signer_kind",
                table: "plugin_trust_record",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ptr_signer_id",
                table: "plugin_trust_record");

            migrationBuilder.DropColumn(
                name: "ptr_signer_kind",
                table: "plugin_trust_record");
        }
    }
}
