using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogicFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "InactivePeriods",
                table: "Teachers",
                type: "text[]",
                nullable: false,
                // MAVJUD o'qituvchilar bo'sh massiv oladi (arxivdan qaytarilganda to'ldiriladi).
                // Default'siz PostgreSQL NOT NULL ustunni qatorlari bor jadvalga qo'sha olmaydi.
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<bool>(
                name: "GradeFromCriteria",
                table: "JournalEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // MAVJUD mezonli baholarni belgilash (bir martalik): shu kungacha mezon belgisi jurnal bahosini
            // HAR SAFAR bosib yozardi (baho = o'sha kun bajarilgan, guruhga biriktirilgan mezonlar soni).
            // Belgilanmasa bu baholar "qo'lda qo'yilgan" deb o'qilib, ball ikki marta sanalardi va sinxron
            // ularni hech qachon yangilamasdi. Qo'lda qo'yilgan baho tasodifan shu songa teng bo'lsa ham
            // belgilanadi — ilgari u baribir keyingi belgilashda bosib ketilardi (`journal.md`).
            migrationBuilder.Sql(@"
UPDATE ""JournalEntries"" je SET ""GradeFromCriteria"" = TRUE
WHERE je.""Grade"" > 0 AND je.""Grade"" = (
    SELECT COUNT(*) FROM ""CriterionGrades"" cg
    JOIN ""GroupGradingCriteria"" gg ON gg.""GroupId"" = cg.""GroupId"" AND gg.""CriterionId"" = cg.""CriterionId""
    WHERE cg.""GroupId"" = je.""ClassId"" AND cg.""StudentId"" = je.""StudentId""
      AND cg.""Date"" = je.""Date"" AND cg.""Done"");");

            migrationBuilder.AddColumn<List<string>>(
                name: "PastLeadIds",
                table: "IgConversations",
                type: "text[]",
                nullable: false,
                // MAVJUD suhbatlar bo'sh tarix bilan boshlanadi (qayta bog'lash faqat shu o'zgarishdan keyin).
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<string>(
                name: "SplitFromId",
                table: "FinanceTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PayAtPickup",
                table: "BookOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceNotifyItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClassId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    ReasonId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceNotifyItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackgroundJobRuns",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastRunDate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobRuns", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "LessonReminderMarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonReminderMarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsQueueItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    LeadId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsQueueItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsQueueJobs",
                columns: table => new
                {
                    BatchId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: true),
                    CallbackUrl = table.Column<string>(type: "text", nullable: true),
                    LeadNote = table.Column<string>(type: "text", nullable: true),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResumeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsQueueJobs", x => x.BatchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_SplitFromId",
                table: "FinanceTransactions",
                column: "SplitFromId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotifyItems_Status_CreatedAt",
                table: "AttendanceNotifyItems",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonReminderMarks_Date",
                table: "LessonReminderMarks",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_LessonReminderMarks_Key",
                table: "LessonReminderMarks",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmsQueueItems_BatchId_Seq",
                table: "SmsQueueItems",
                columns: new[] { "BatchId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsQueueJobs_State",
                table: "SmsQueueJobs",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceNotifyItems");

            migrationBuilder.DropTable(
                name: "BackgroundJobRuns");

            migrationBuilder.DropTable(
                name: "LessonReminderMarks");

            migrationBuilder.DropTable(
                name: "SmsQueueItems");

            migrationBuilder.DropTable(
                name: "SmsQueueJobs");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_SplitFromId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "InactivePeriods",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "GradeFromCriteria",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "PastLeadIds",
                table: "IgConversations");

            migrationBuilder.DropColumn(
                name: "SplitFromId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "PayAtPickup",
                table: "BookOrders");
        }
    }
}
