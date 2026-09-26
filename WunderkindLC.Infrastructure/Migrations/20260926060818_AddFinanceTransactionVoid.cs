using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceTransactionVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "FinanceTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "FinanceTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedAt",
                table: "FinanceTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedBy",
                table: "FinanceTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidedById",
                table: "FinanceTransactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "VoidedBy",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "VoidedById",
                table: "FinanceTransactions");
        }
    }
}
