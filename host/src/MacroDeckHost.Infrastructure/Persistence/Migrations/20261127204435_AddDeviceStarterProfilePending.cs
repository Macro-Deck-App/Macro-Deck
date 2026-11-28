using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MacroDeckHost.DatabaseMigrations
{
    /// <inheritdoc />
    public partial class AddDeviceStarterProfilePending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "d_starter_profile_pending",
                table: "device",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "d_starter_profile_pending",
                table: "device");
        }
    }
}
