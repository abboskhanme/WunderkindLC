using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHrTrialTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_trial_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lesson_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trial_ids = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_trial_tasks", x => x.id);
                    table.CheckConstraint("ck_hr_trial_task_kind", "kind in ('before','after')");
                    table.ForeignKey(
                        name: "fk_hr_trial_tasks_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_trial_tasks_task_id",
                table: "hr_trial_tasks",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "uq_hr_trial_task",
                table: "hr_trial_tasks",
                columns: new[] { "employee_id", "kind", "lesson_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_trial_tasks");
        }
    }
}
