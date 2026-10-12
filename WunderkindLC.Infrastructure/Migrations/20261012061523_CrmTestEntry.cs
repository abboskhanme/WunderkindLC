using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmTestEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TestResults",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsSentAt",
                table: "TestResults",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultsSentById",
                table: "TestResults",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Data (docs/migrations/Phase4a.sql): existing tests are interim tests (kind is required from now on).
            migrationBuilder.Sql(@"UPDATE ""TestResults"" SET ""Kind"" = 'interim' WHERE ""Kind"" = '' OR ""Kind"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "ResultsSentAt",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "ResultsSentById",
                table: "TestResults");
        }
    }
}
