using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntellectCRM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_PushMessageId",
                table: "UserNotifications",
                column: "PushMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Date",
                table: "JournalEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_StudentId",
                table: "JournalEntries",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_StudentId",
                table: "FinanceTransactions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_TeacherId",
                table: "FinanceTransactions",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserNotifications_PushMessageId",
                table: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_Date",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_StudentId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_StudentId",
                table: "FinanceTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_TeacherId",
                table: "FinanceTransactions");
        }
    }
}
