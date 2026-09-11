using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Answers",
                table: "TestScores",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "TestScores",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SubmittedAt",
                table: "TestScores",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerKey",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EndAt",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "TestResults",
                type: "text",
                nullable: false,
                // Mavjud testlar OFLAYN (eski tizim) — yangi rejim faqat ataylab tanlanadi.
                defaultValue: "offline");

            migrationBuilder.AddColumn<int>(
                name: "OptionCount",
                table: "TestResults",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "PdfFileId",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PdfName",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PdfUrl",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "QuestionCount",
                table: "TestResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StartAt",
                table: "TestResults",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TestBotSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    TestResultId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Answers = table.Column<string>(type: "text", nullable: false),
                    Current = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    InputMode = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestBotSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestBotSessions_TestResults_TestResultId",
                        column: x => x.TestResultId,
                        principalTable: "TestResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestBotSessions_ChatId",
                table: "TestBotSessions",
                column: "ChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestBotSessions_TestResultId",
                table: "TestBotSessions",
                column: "TestResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestBotSessions");

            migrationBuilder.DropColumn(
                name: "Answers",
                table: "TestScores");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TestScores");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "TestScores");

            migrationBuilder.DropColumn(
                name: "AnswerKey",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "EndAt",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "OptionCount",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "PdfFileId",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "PdfName",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "PdfUrl",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "QuestionCount",
                table: "TestResults");

            migrationBuilder.DropColumn(
                name: "StartAt",
                table: "TestResults");
        }
    }
}
