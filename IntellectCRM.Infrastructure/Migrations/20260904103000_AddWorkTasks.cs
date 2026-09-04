using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntellectCRM.Infrastructure.Migrations
{
    /// <summary>
    /// "Topshiriqlar" moduli (Kanban): doskalar, ustunlar, topshiriqlar, qadamlar, izohlar va
    /// harakatlar tarixi + markaz sozlamalariga kunlik eslatma vaqti.
    ///
    /// <para>⚠️ Mavjud "Adminga topshiriq" (StaffTasks / StaffTaskLogs) jadvallariga TEGILMAYDI —
    /// u kunlik checklist sifatida eskicha ishlashda davom etadi.</para>
    /// </summary>
    public partial class AddWorkTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kunlik eslatma sozlamasi. Ustunlar EF odatdagicha CLR standart qiymati bilan
            // qo'shiladi (model snapshot'da `HasDefaultValue` yo'q — ular ayrilib qolmasin),
            // MAVJUD markaz qatori esa quyida entity standartlariga (yoqilgan, 09:30) keltiriladi:
            // aks holda modul yoqilgan bo'lsa-yu, eslatma soati 00:00 bo'lib qolardi.
            migrationBuilder.AddColumn<bool>(
                name: "WorkTaskReminderEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WorkTaskReminderHour",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkTaskReminderMinute",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                @"UPDATE ""CenterMeta"" SET ""WorkTaskReminderEnabled"" = TRUE, "
                + @"""WorkTaskReminderHour"" = 9, ""WorkTaskReminderMinute"" = 30;");

            migrationBuilder.CreateTable(
                name: "WorkTaskBoards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskBoards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskColumns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BoardId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsDone = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskColumns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BoardId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColumnId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    AssigneeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DueDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DueTime = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedById = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Repeat = table.Column<string>(type: "text", nullable: false),
                    RepeatOfId = table.Column<string>(type: "text", nullable: false),
                    ReminderSentDate = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorId = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskColumns_BoardId_Order",
                table: "WorkTaskColumns",
                columns: new[] { "BoardId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskComments_TaskId",
                table: "WorkTaskComments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskEvents_TaskId",
                table: "WorkTaskEvents",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskItems_TaskId",
                table: "WorkTaskItems",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_AssigneeId",
                table: "WorkTasks",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_BoardId_ColumnId",
                table: "WorkTasks",
                columns: new[] { "BoardId", "ColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_DueDate",
                table: "WorkTasks",
                column: "DueDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkTaskBoards");
            migrationBuilder.DropTable(name: "WorkTaskColumns");
            migrationBuilder.DropTable(name: "WorkTaskComments");
            migrationBuilder.DropTable(name: "WorkTaskEvents");
            migrationBuilder.DropTable(name: "WorkTaskItems");
            migrationBuilder.DropTable(name: "WorkTasks");

            migrationBuilder.DropColumn(name: "WorkTaskReminderEnabled", table: "CenterMeta");
            migrationBuilder.DropColumn(name: "WorkTaskReminderHour", table: "CenterMeta");
            migrationBuilder.DropColumn(name: "WorkTaskReminderMinute", table: "CenterMeta");
        }
    }
}
