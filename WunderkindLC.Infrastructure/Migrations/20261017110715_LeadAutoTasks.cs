using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>SPEC-backlog-2026-10-17 F4 L1: lid avto-topshirig'i (WorkTask.LeadId, CenterMeta.LeadAutoTask*) + F3 revyu:
    /// FinanceTransactions.ExpenseRequestId unikal (bitta tasdiq so'rovi — bitta tranzaksiya). Faqat qo'shimcha.</summary>
    public partial class LeadAutoTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LeadId",
                table: "WorkTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "LeadAutoTaskAssigneeIds",
                table: "CenterMeta",
                type: "text[]",
                nullable: false,
                // Mavjud CenterMeta qatori bo'sh ro'yxat oladi (modul o'chiq). Default'siz PostgreSQL NOT NULL
                // ustunni qatori bor jadvalga qo'sha olmaydi (AddStudentGroupPastPeriods dagi saboq).
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<string>(
                name: "LeadAutoTaskBoardId",
                table: "CenterMeta",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeadAutoTaskCursor",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LeadAutoTaskEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTasks_LeadId",
                table: "WorkTasks",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_ExpenseRequestId",
                table: "FinanceTransactions",
                column: "ExpenseRequestId",
                unique: true,
                filter: "\"ExpenseRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkTasks_LeadId",
                table: "WorkTasks");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_ExpenseRequestId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "LeadId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "LeadAutoTaskAssigneeIds",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LeadAutoTaskBoardId",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LeadAutoTaskCursor",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LeadAutoTaskEnabled",
                table: "CenterMeta");
        }
    }
}
