using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceTakenAt",
                table: "LessonNotes",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceTakenById",
                table: "LessonNotes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbsenceStreakReasonId",
                table: "CenterMeta",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbsentNoticeApproval",
                table: "CenterMeta",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AbsentNoticeDelayMinutes",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceDeadlineMinutes",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceDisciplineEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceEscalateEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AttendanceFineAmount",
                table: "CenterMeta",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceFineEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceReminderMinutes",
                table: "CenterMeta",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsExcused",
                table: "AbsenceReasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AbsenceStreakAlerts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GroupId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StreakStart = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    ContactRequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbsenceStreakAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAttendanceFines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TeacherId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    GroupId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    LessonDate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    LessonStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Deadline = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttendanceTakenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    WaivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    WaivedById = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WaivedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WaiveReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAttendanceFines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonNotes_ClassId_Date",
                table: "LessonNotes",
                columns: new[] { "ClassId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceStreakAlerts_StudentId_GroupId_StreakStart_Level",
                table: "AbsenceStreakAlerts",
                columns: new[] { "StudentId", "GroupId", "StreakStart", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_GroupId_LessonDate",
                table: "TeacherAttendanceFines",
                columns: new[] { "GroupId", "LessonDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_LessonDate",
                table: "TeacherAttendanceFines",
                column: "LessonDate");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAttendanceFines_TeacherId_Month",
                table: "TeacherAttendanceFines",
                columns: new[] { "TeacherId", "Month" });

            // Data (docs/migrations/Phase3.sql).
            // 1) Excused flag from today's heuristic (late reasons and names with "sababsiz" stay unexcused).
            migrationBuilder.Sql(@"UPDATE ""AbsenceReasons"" SET ""IsExcused"" = TRUE
 WHERE ""IsLate"" = FALSE AND lower(""Name"") NOT LIKE '%sababsiz%';");
            // 2) Attendance settings defaults for the existing row (discipline and fines stay OFF until an admin enables them).
            migrationBuilder.Sql(@"UPDATE ""CenterMeta"" SET
    ""AttendanceDeadlineMinutes"" = 15,
    ""AttendanceReminderMinutes"" = '5,10',
    ""AttendanceEscalateEnabled"" = TRUE,
    ""AbsentNoticeDelayMinutes""  = 1,
    ""AbsentNoticeApproval""      = 'late_only'
 WHERE ""AttendanceDeadlineMinutes"" = 0;");
            // 3) AttendanceTakenAt for the last 60 days (lesson date 00:00) — no fines/monitor gaps for past lessons.
            //    Only real calendar days of that window: a malformed legacy date must not fail the cast (and the deploy).
            migrationBuilder.Sql(@"UPDATE ""LessonNotes"" n
   SET ""AttendanceTakenAt"" = CAST(n.""Date"" AS date)::timestamp
 WHERE n.""AttendanceTakenAt"" IS NULL
   AND n.""Conducted"" = TRUE
   AND n.""Date"" IN (SELECT to_char(d, 'YYYY-MM-DD')
                        FROM generate_series(current_date - 60, current_date, interval '1 day') d)
   AND (n.""AttendanceTaken"" = TRUE
        OR EXISTS (SELECT 1 FROM ""JournalEntries"" e
                    WHERE e.""ClassId"" = n.""ClassId"" AND e.""SubjectId"" = n.""SubjectId""
                      AND e.""Date"" = n.""Date"" AND e.""Period"" = n.""Period""
                      AND (e.""Present"" = TRUE OR e.""ReasonId"" IS NOT NULL)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbsenceStreakAlerts");

            migrationBuilder.DropTable(
                name: "TeacherAttendanceFines");

            migrationBuilder.DropIndex(
                name: "IX_LessonNotes_ClassId_Date",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "AttendanceTakenAt",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "AttendanceTakenById",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "AbsenceStreakReasonId",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AbsentNoticeApproval",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AbsentNoticeDelayMinutes",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceDeadlineMinutes",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceDisciplineEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceEscalateEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceFineAmount",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceFineEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceReminderMinutes",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "IsExcused",
                table: "AbsenceReasons");
        }
    }
}
