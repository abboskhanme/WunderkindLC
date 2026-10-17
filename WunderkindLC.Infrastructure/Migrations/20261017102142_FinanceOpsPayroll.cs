using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceOpsPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpenseRequestId",
                table: "FinanceTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryPayKind",
                table: "FinanceTransactions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffUserId",
                table: "FinanceTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpenseApprovalThreshold",
                table: "CenterMeta",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Cashboxes",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "cash");

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Cashboxes",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OpeningDate",
                table: "Cashboxes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Date = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TypeId = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
                    TeacherId = table.Column<string>(type: "text", nullable: true),
                    StaffUserId = table.Column<string>(type: "text", nullable: true),
                    Month = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: true),
                    CashboxId = table.Column<string>(type: "text", nullable: true),
                    SalaryPayKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<string>(type: "text", nullable: true),
                    DecidedById = table.Column<string>(type: "text", nullable: true),
                    DecidedBy = table.Column<string>(type: "text", nullable: true),
                    DecisionNote = table.Column<string>(type: "text", nullable: true),
                    TransactionId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffSalaries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EffectiveFrom = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffSalaries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_StaffUserId_Month",
                table: "FinanceTransactions",
                columns: new[] { "StaffUserId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseRequests_Status_CreatedAt",
                table: "ExpenseRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffSalaries_UserId_EffectiveFrom",
                table: "StaffSalaries",
                columns: new[] { "UserId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseRequests");

            migrationBuilder.DropTable(
                name: "StaffSalaries");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_StaffUserId_Month",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "ExpenseRequestId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "SalaryPayKind",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "StaffUserId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "ExpenseApprovalThreshold",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Cashboxes");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "Cashboxes");

            migrationBuilder.DropColumn(
                name: "OpeningDate",
                table: "Cashboxes");
        }
    }
}
