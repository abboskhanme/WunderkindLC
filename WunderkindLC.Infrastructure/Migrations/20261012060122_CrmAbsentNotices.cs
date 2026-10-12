using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmAbsentNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "AttendanceNotifyItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "AttendanceNotifyItems",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedById",
                table: "AttendanceNotifyItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedByName",
                table: "AttendanceNotifyItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "AttendanceNotifyItems",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LessonEnd",
                table: "AttendanceNotifyItems",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LessonStart",
                table: "AttendanceNotifyItems",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "SendAfter",
                table: "AttendanceNotifyItems",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "TakenLate",
                table: "AttendanceNotifyItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotifyItems_Date_ClassId",
                table: "AttendanceNotifyItems",
                columns: new[] { "Date", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceNotifyItems_Status_SendAfter",
                table: "AttendanceNotifyItems",
                columns: new[] { "Status", "SendAfter" });

            // Data (docs/migrations/Phase3b.sql): existing queue rows are due from their creation time (they are
            // older than the 6 h expiry, so the worker expires them instead of sending stale notices).
            migrationBuilder.Sql(@"UPDATE ""AttendanceNotifyItems"" SET ""SendAfter"" = ""CreatedAt"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceNotifyItems_Date_ClassId",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceNotifyItems_Status_SendAfter",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "DecidedById",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "DecidedByName",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "LessonEnd",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "LessonStart",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "SendAfter",
                table: "AttendanceNotifyItems");

            migrationBuilder.DropColumn(
                name: "TakenLate",
                table: "AttendanceNotifyItems");
        }
    }
}
