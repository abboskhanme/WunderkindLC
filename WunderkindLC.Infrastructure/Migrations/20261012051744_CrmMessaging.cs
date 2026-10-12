using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedAt",
                table: "TelegramRegistrations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Relation",
                table: "TelegramRegistrations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "AckRequired",
                table: "Broadcasts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentId",
                table: "Broadcasts",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudienceJson",
                table: "Broadcasts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedAt",
                table: "Broadcasts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Broadcasts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "broadcast");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Broadcasts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "done");

            migrationBuilder.AddColumn<bool>(
                name: "AckButton",
                table: "AutoMessageRules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentId",
                table: "AutoMessageRules",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TelegramFileId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileDeletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BroadcastId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TeacherId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RegistrationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Relation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    AttachmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    AckRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AckAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedById = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentAchievements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Result = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AttachmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CongratulatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CongratulationCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAchievements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramRegistrations_StudentId_BlockedAt",
                table: "TelegramRegistrations",
                columns: new[] { "StudentId", "BlockedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageDeliveries_BroadcastId",
                table: "MessageDeliveries",
                column: "BroadcastId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDeliveries_DedupeKey",
                table: "MessageDeliveries",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageDeliveries_SourceId",
                table: "MessageDeliveries",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDeliveries_Status_NextAttemptAt",
                table: "MessageDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageDeliveries_StudentId_CreatedAt",
                table: "MessageDeliveries",
                columns: new[] { "StudentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAchievements_StudentId",
                table: "StudentAchievements",
                column: "StudentId");

            // Data (docs/migrations/Phase2.sql). Existing campaigns are finished broadcasts; the odd-day debt
            // cadence becomes a monthly day (1st, 09:00); birthday greetings keep 09:00 (now per rule).
            migrationBuilder.Sql(@"UPDATE ""Broadcasts"" SET ""Kind"" = 'broadcast', ""Status"" = 'done' WHERE ""Kind"" = '' OR ""Kind"" IS NULL;");
            migrationBuilder.Sql(@"UPDATE ""AutoMessageRules"" SET ""ScheduleType"" = 'monthly', ""ScheduleDayOfMonth"" = 1, ""ScheduleTime"" = '09:00' WHERE ""Trigger"" = 'payment_debt';");
            migrationBuilder.Sql(@"UPDATE ""AutoMessageRules"" SET ""ScheduleTime"" = '09:00' WHERE ""Trigger"" = 'birthday';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "MessageDeliveries");

            migrationBuilder.DropTable(
                name: "StudentAchievements");

            migrationBuilder.DropIndex(
                name: "IX_TelegramRegistrations_StudentId_BlockedAt",
                table: "TelegramRegistrations");

            migrationBuilder.DropColumn(
                name: "BlockedAt",
                table: "TelegramRegistrations");

            migrationBuilder.DropColumn(
                name: "Relation",
                table: "TelegramRegistrations");

            migrationBuilder.DropColumn(
                name: "AckRequired",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "AudienceJson",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Broadcasts");

            migrationBuilder.DropColumn(
                name: "AckButton",
                table: "AutoMessageRules");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                table: "AutoMessageRules");
        }
    }
}
