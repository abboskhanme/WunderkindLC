using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelTestInvite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LevelTestInvites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    TestId = table.Column<string>(type: "text", nullable: false),
                    LeadId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    SmsStatus = table.Column<string>(type: "text", nullable: false),
                    SmsRequestId = table.Column<string>(type: "text", nullable: false),
                    UsedAt = table.Column<string>(type: "text", nullable: false),
                    SubmissionId = table.Column<string>(type: "text", nullable: false),
                    Percent = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelTestInvites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LevelTestInvites_LeadId",
                table: "LevelTestInvites",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelTestInvites_TestId_CreatedAt",
                table: "LevelTestInvites",
                columns: new[] { "TestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LevelTestInvites_Token",
                table: "LevelTestInvites",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelTestInvites");
        }
    }
}
