using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKpiModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendedAt",
                table: "TrialLessons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssigneeUserId",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedAt",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedByUserId",
                table: "Leads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OutOfControl",
                table: "ActionReasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ChecklistEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Date = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistTemplateItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    No = table.Column<int>(type: "integer", nullable: false),
                    TimeBlock = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Norm = table.Column<string>(type: "text", nullable: false),
                    KpiTag = table.Column<string>(type: "text", nullable: true),
                    CriterionNo = table.Column<int>(type: "integer", nullable: true),
                    AutoCheckKey = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistTemplateItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RoleCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiMonthResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    Month = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoleCode = table.Column<string>(type: "text", nullable: false),
                    InputsJson = table.Column<string>(type: "text", nullable: false),
                    CoefsJson = table.Column<string>(type: "text", nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BonusTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GuaranteeApplied = table.Column<bool>(type: "boolean", nullable: false),
                    CapExceeded = table.Column<bool>(type: "boolean", nullable: false),
                    RuleSetId = table.Column<string>(type: "text", nullable: true),
                    SalaryVersionId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "text", nullable: true),
                    ConfirmedAt = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiMonthResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiMonthSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Month = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoleCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    TakenAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiMonthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleCode = table.Column<string>(type: "text", nullable: false),
                    StartMonth = table.Column<string>(type: "text", nullable: false),
                    GuaranteeUntilMonth = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiProfileSalaries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiProfileSalaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiRuleSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RoleCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiRuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KpiTickets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: false),
                    CriterionNo = table.Column<int>(type: "integer", nullable: true),
                    CallId = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    IssuedAt = table.Column<string>(type: "text", nullable: false),
                    DisputeNote = table.Column<string>(type: "text", nullable: true),
                    ResolvedBy = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentExtensions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    FromGroupId = table.Column<string>(type: "text", nullable: false),
                    FromGroupName = table.Column<string>(type: "text", nullable: false),
                    ToGroupId = table.Column<string>(type: "text", nullable: false),
                    ToGroupName = table.Column<string>(type: "text", nullable: false),
                    CourseId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ByUserId = table.Column<string>(type: "text", nullable: false),
                    ByUserName = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentExtensions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistEntries_UserId_Date_ItemId",
                table: "ChecklistEntries",
                columns: new[] { "UserId", "Date", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplateItems_TemplateId_Order",
                table: "ChecklistTemplateItems",
                columns: new[] { "TemplateId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplates_RoleCode",
                table: "ChecklistTemplates",
                column: "RoleCode");

            migrationBuilder.CreateIndex(
                name: "IX_KpiMonthResults_UserId_Month",
                table: "KpiMonthResults",
                columns: new[] { "UserId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiMonthSnapshots_Month_RoleCode_UserId",
                table: "KpiMonthSnapshots",
                columns: new[] { "Month", "RoleCode", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KpiProfiles_UserId",
                table: "KpiProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KpiProfileSalaries_UserId_EffectiveFrom",
                table: "KpiProfileSalaries",
                columns: new[] { "UserId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_KpiRuleSets_RoleCode_EffectiveFrom",
                table: "KpiRuleSets",
                columns: new[] { "RoleCode", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_KpiTickets_Status",
                table: "KpiTickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KpiTickets_UserId_Date",
                table: "KpiTickets",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentExtensions_Date",
                table: "StudentExtensions",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_StudentExtensions_StudentId",
                table: "StudentExtensions",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChecklistEntries");

            migrationBuilder.DropTable(
                name: "ChecklistTemplateItems");

            migrationBuilder.DropTable(
                name: "ChecklistTemplates");

            migrationBuilder.DropTable(
                name: "KpiMonthResults");

            migrationBuilder.DropTable(
                name: "KpiMonthSnapshots");

            migrationBuilder.DropTable(
                name: "KpiProfiles");

            migrationBuilder.DropTable(
                name: "KpiProfileSalaries");

            migrationBuilder.DropTable(
                name: "KpiRuleSets");

            migrationBuilder.DropTable(
                name: "KpiTickets");

            migrationBuilder.DropTable(
                name: "StudentExtensions");

            migrationBuilder.DropColumn(
                name: "AttendedAt",
                table: "TrialLessons");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "OutOfControl",
                table: "ActionReasons");
        }
    }
}
