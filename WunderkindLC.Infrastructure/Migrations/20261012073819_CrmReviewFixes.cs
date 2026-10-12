using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceDisciplineSince",
                table: "CenterMeta",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceFineSince",
                table: "CenterMeta",
                type: "timestamp without time zone",
                nullable: true);

            // Data (docs/migrations/ReviewFixes.sql): switches already ON count from the deploy moment — no fines,
            // reminders or escalations for lessons before it.
            migrationBuilder.Sql(@"UPDATE ""CenterMeta"" SET ""AttendanceDisciplineSince"" = (now() AT TIME ZONE 'Asia/Tashkent')
 WHERE ""AttendanceDisciplineEnabled"" = TRUE AND ""AttendanceDisciplineSince"" IS NULL;");
            migrationBuilder.Sql(@"UPDATE ""CenterMeta"" SET ""AttendanceFineSince"" = (now() AT TIME ZONE 'Asia/Tashkent')
 WHERE ""AttendanceFineEnabled"" = TRUE AND ""AttendanceFineSince"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceDisciplineSince",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AttendanceFineSince",
                table: "CenterMeta");
        }
    }
}
