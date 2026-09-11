using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetentionPerCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RetentionBonusAwards_StudentId_CycleNo",
                table: "RetentionBonusAwards");

            migrationBuilder.AddColumn<string>(
                name: "CourseId",
                table: "RetentionBonusAwards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CourseName",
                table: "RetentionBonusAwards",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RetentionBonusTracks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CourseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartMonth = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionBonusTracks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusAwards_StudentId_CourseId_CycleNo",
                table: "RetentionBonusAwards",
                columns: new[] { "StudentId", "CourseId", "CycleNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusTracks_StudentId_CourseId",
                table: "RetentionBonusTracks",
                columns: new[] { "StudentId", "CourseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionBonusTracks");

            migrationBuilder.DropIndex(
                name: "IX_RetentionBonusAwards_StudentId_CourseId_CycleNo",
                table: "RetentionBonusAwards");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "RetentionBonusAwards");

            migrationBuilder.DropColumn(
                name: "CourseName",
                table: "RetentionBonusAwards");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusAwards_StudentId_CycleNo",
                table: "RetentionBonusAwards",
                columns: new[] { "StudentId", "CycleNo" },
                unique: true);
        }
    }
}
