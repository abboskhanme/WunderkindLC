using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentGroupPastPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "PastPeriods",
                table: "StudentGroups",
                type: "text[]",
                nullable: false,
                // MAVJUD qatorlar bo'sh massiv oladi — tarix BACKFILL QILINMAYDI (aniq qaror).
                // Default'siz PostgreSQL NOT NULL ustunni qatorlari bor jadvalga qo'sha olmaydi.
                defaultValue: new List<string>());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PastPeriods",
                table: "StudentGroups");
        }
    }
}
