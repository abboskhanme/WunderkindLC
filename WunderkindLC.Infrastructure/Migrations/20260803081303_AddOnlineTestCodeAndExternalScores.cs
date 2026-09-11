using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineTestCodeAndExternalScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "TestResults",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // DIQQAT: defaultValue ATAYIN true (EF o'zi false qo'yardi). Mavjud testlar guruhga
            // E'LON QILINGAN holida qolishi shart — false bo'lsa barcha eski onlayn testlar
            // o'quvchilarning bot/ilova ro'yxatidan birdaniga yo'qolib ketardi.
            migrationBuilder.AddColumn<bool>(
                name: "GroupOpen",
                table: "TestResults",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalName",
                table: "TestBotSessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "TestBotSessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ExternalTestScores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TestResultId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Answers = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalTestScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalTestScores_TestResults_TestResultId",
                        column: x => x.TestResultId,
                        principalTable: "TestResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestResults_Code",
                table: "TestResults",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTestScores_TestResultId_ChatId",
                table: "ExternalTestScores",
                columns: new[] { "TestResultId", "ChatId" },
                unique: true);

            // MAVJUD onlayn testlarga ham TEST KODI beramiz — aks holda ular kodsiz qolib, markazdan
            // tashqari ishtirokchi ularga qo'shila olmasdi (kod faqat test qayta saqlanganda paydo
            // bo'lardi). Id'dan hosil qilingan md5 — determinstik va qatorlar orasida farqli.
            migrationBuilder.Sql("""
                UPDATE "TestResults"
                SET "Code" = upper(substr(md5("Id"), 1, 6))
                WHERE "Mode" = 'online' AND "Code" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalTestScores");

            migrationBuilder.DropIndex(
                name: "IX_TestResults_Code",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "GroupOpen",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "ExternalName",
                table: "TestBotSessions");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "TestBotSessions");
        }
    }
}
