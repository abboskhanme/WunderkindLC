using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEskizSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EskizEmail",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizFrom",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizPassword",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizToken",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EskizTokenExpiresAt",
                table: "CenterMeta",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SmsBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Audience = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    SenderUserId = table.Column<string>(type: "text", nullable: false),
                    SenderName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    SentCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BatchId = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    RecipientName = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_BatchId",
                table: "SmsLogs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_RequestId",
                table: "SmsLogs",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsBatches");

            migrationBuilder.DropTable(
                name: "SmsLogs");

            migrationBuilder.DropColumn(
                name: "EskizEmail",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizFrom",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizPassword",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizToken",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizTokenExpiresAt",
                table: "CenterMeta");
        }
    }
}
