using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseItemAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseItemAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurriculumId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LessonId = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "text", nullable: false),
                    Section = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExerciseKind = table.Column<string>(type: "text", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    Correct = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    ScorePct = table.Column<int>(type: "integer", nullable: false),
                    DurationSec = table.Column<int>(type: "integer", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseItemAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseItemAttempts_ItemId_StudentId_Section",
                table: "CourseItemAttempts",
                columns: new[] { "ItemId", "StudentId", "Section" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseItemAttempts_StudentId_FinishedAt",
                table: "CourseItemAttempts",
                columns: new[] { "StudentId", "FinishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseItemAttempts");
        }
    }
}
