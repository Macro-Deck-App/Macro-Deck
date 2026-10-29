using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MacroDeckHost.DatabaseMigrations
{
    /// <inheritdoc />
    public partial class AddMcpAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_access_token",
                columns: table => new
                {
                    mat_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    mat_name = table.Column<string>(type: "TEXT", nullable: false),
                    mat_token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    mat_last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    mat_revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    mat_created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_access_token", x => x.mat_id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_mcp_access_token_hash",
                table: "mcp_access_token",
                column: "mat_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_access_token");
        }
    }
}
