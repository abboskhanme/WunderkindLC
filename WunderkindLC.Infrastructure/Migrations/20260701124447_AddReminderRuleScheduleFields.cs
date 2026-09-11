using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderRuleScheduleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "ReminderRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScheduleDayOfMonth",
                table: "ReminderRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleTime",
                table: "ReminderRules",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScheduleType",
                table: "ReminderRules",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Audience",
                table: "ReminderRules");

            migrationBuilder.DropColumn(
                name: "ScheduleDayOfMonth",
                table: "ReminderRules");

            migrationBuilder.DropColumn(
                name: "ScheduleTime",
                table: "ReminderRules");

            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "ReminderRules");
        }
    }
}
