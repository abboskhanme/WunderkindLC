using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContactServiceAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "ContactStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ContactAbsenceAutoEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContactAbsenceLessons",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "ContactDebtAutoEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ContactDebtDay",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "DebtContactReasonId",
                table: "CenterMeta",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DebtContactAlerts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ContactRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OpenedRequest = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtContactAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebtContactAlerts_OpenedRequest_ResolvedAt",
                table: "DebtContactAlerts",
                columns: new[] { "OpenedRequest", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtContactAlerts_StudentId_Month",
                table: "DebtContactAlerts",
                columns: new[] { "StudentId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtContactAlerts");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "ContactStages");

            migrationBuilder.DropColumn(
                name: "ContactAbsenceAutoEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "ContactAbsenceLessons",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "ContactDebtAutoEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "ContactDebtDay",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "DebtContactReasonId",
                table: "CenterMeta");
        }
    }
}
