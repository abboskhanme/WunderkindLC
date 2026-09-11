using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseProgressCourseId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CourseId",
                table: "CourseProgresses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_course_progress_course_tracking",
                table: "CourseProgresses",
                columns: new[] { "StudentId", "CourseId", "ItemId" },
                unique: true,
                filter: "\"CourseId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_course_progress_course_tracking",
                table: "CourseProgresses");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "CourseProgresses");
        }
    }
}
