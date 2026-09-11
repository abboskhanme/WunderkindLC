using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// "BOG'LANISH KERAK" moduli — o'quvchi bilan bog'lanish navbati (`ContactRequest`) va uning
    /// hodisalari (`ContactAttempt`). Mavjud jadvallarga TEGMAYDI, faqat ikkita yangi jadval.
    /// </summary>
    /// <inheritdoc />
    public partial class AddContactRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    ReasonId = table.Column<string>(type: "text", nullable: false),
                    ReasonLabel = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastResponse = table.Column<string>(type: "text", nullable: false),
                    LastActorName = table.Column<string>(type: "text", nullable: false),
                    LastActionAt = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    ClosedAt = table.Column<string>(type: "text", nullable: false),
                    ClosedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    NextStatus = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactAttempts_Date",
                table: "ContactAttempts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ContactAttempts_RequestId",
                table: "ContactAttempts",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactRequests_StudentId",
                table: "ContactRequests",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactRequests_Status_DueDate",
                table: "ContactRequests",
                columns: new[] { "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactAttempts");

            migrationBuilder.DropTable(
                name: "ContactRequests");
        }
    }
}
