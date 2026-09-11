using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundsAndLessonReschedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundOfId",
                table: "FinanceTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LessonReschedules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ClassId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromDate = table.Column<string>(type: "text", nullable: false),
                    ToDate = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonReschedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonReschedules_ClassId",
                table: "LessonReschedules",
                column: "ClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonReschedules");

            migrationBuilder.DropColumn(
                name: "RefundOfId",
                table: "FinanceTransactions");
        }
    }
}
