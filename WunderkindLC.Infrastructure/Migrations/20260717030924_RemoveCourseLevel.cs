using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCourseLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseLevels");

            migrationBuilder.DropIndex(
                name: "IX_CourseTopics_LevelId_Order",
                table: "CourseTopics");

            migrationBuilder.DropColumn(
                name: "LevelId",
                table: "CourseTopics");

            migrationBuilder.AlterColumn<string>(
                name: "CurriculumId",
                table: "CourseTopics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_CourseTopics_CurriculumId_Order",
                table: "CourseTopics",
                columns: new[] { "CurriculumId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourseTopics_CurriculumId_Order",
                table: "CourseTopics");

            migrationBuilder.AlterColumn<string>(
                name: "CurriculumId",
                table: "CourseTopics",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "LevelId",
                table: "CourseTopics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CourseLevels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CurriculumId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLevels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseTopics_LevelId_Order",
                table: "CourseTopics",
                columns: new[] { "LevelId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseLevels_CurriculumId_Order",
                table: "CourseLevels",
                columns: new[] { "CurriculumId", "Order" });
        }
    }
}
