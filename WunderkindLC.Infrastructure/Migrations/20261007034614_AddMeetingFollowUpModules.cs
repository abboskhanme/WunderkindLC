using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingFollowUpModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LostAt",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LostByName",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LostByUserId",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LostComment",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LostReason",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LostReasonId",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookOrderId",
                table: "FinanceTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashboxTransfers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromCashboxId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ToCashboxId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    CreatedByName = table.Column<string>(type: "text", nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedAt = table.Column<string>(type: "text", nullable: true),
                    VoidedById = table.Column<string>(type: "text", nullable: true),
                    VoidedByName = table.Column<string>(type: "text", nullable: true),
                    VoidReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashboxTransfers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeOnboardings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    JobApplicationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApplicationNumber = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<string>(type: "text", nullable: false),
                    StaffUserId = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MentorId = table.Column<string>(type: "text", nullable: false),
                    MentorName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<string>(type: "text", nullable: false),
                    CancelledAt = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOnboardings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeOnboardingTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OnboardingId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StepId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DayOffset = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DoneAt = table.Column<string>(type: "text", nullable: false),
                    DoneBy = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOnboardingTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MembershipReasonLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GroupId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ReasonId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReasonLabel = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipReasonLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OnboardingSteps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DayOffset = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSteps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeedMarkers",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AppliedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedMarkers", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "StudentIntakeMarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Item = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    MarkedById = table.Column<string>(type: "text", nullable: false),
                    MarkedByName = table.Column<string>(type: "text", nullable: false),
                    MarkedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentIntakeMarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_BookOrderId",
                table: "FinanceTransactions",
                column: "BookOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransfers_FromCashboxId",
                table: "CashboxTransfers",
                column: "FromCashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_CashboxTransfers_ToCashboxId",
                table: "CashboxTransfers",
                column: "ToCashboxId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOnboardings_JobApplicationId",
                table: "EmployeeOnboardings",
                column: "JobApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOnboardings_Status_StartDate",
                table: "EmployeeOnboardings",
                columns: new[] { "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOnboardingTasks_OnboardingId_Order",
                table: "EmployeeOnboardingTasks",
                columns: new[] { "OnboardingId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipReasonLogs_Date",
                table: "MembershipReasonLogs",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipReasonLogs_StudentId_GroupId",
                table: "MembershipReasonLogs",
                columns: new[] { "StudentId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSteps_Order",
                table: "OnboardingSteps",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_StudentIntakeMarks_StudentId_Item",
                table: "StudentIntakeMarks",
                columns: new[] { "StudentId", "Item" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashboxTransfers");

            migrationBuilder.DropTable(
                name: "EmployeeOnboardings");

            migrationBuilder.DropTable(
                name: "EmployeeOnboardingTasks");

            migrationBuilder.DropTable(
                name: "MembershipReasonLogs");

            migrationBuilder.DropTable(
                name: "OnboardingSteps");

            migrationBuilder.DropTable(
                name: "SeedMarkers");

            migrationBuilder.DropTable(
                name: "StudentIntakeMarks");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_BookOrderId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "LostAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LostByName",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LostByUserId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LostComment",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LostReason",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LostReasonId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "BookOrderId",
                table: "FinanceTransactions");
        }
    }
}
