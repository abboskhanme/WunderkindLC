using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetentionBonusSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RetentionBonus",
                table: "Students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RetentionBonusStartMonth",
                table: "Students",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionDefaultAmount",
                table: "CenterMeta",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RetentionMaxGapMonths",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RetentionMonthsRequired",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RetentionBonusAwards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    CycleNo = table.Column<int>(type: "integer", nullable: false),
                    PeriodFrom = table.Column<string>(type: "text", nullable: false),
                    PeriodTo = table.Column<string>(type: "text", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CancelReason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    GivenBy = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionBonusAwards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetentionBonusShares",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AwardId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeacherId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeacherName = table.Column<string>(type: "text", nullable: false),
                    Months = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionBonusShares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusAwards_StudentId_CycleNo",
                table: "RetentionBonusAwards",
                columns: new[] { "StudentId", "CycleNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusShares_AwardId",
                table: "RetentionBonusShares",
                column: "AwardId");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionBonusShares_TeacherId",
                table: "RetentionBonusShares",
                column: "TeacherId");

            // Yangi ustunlar MAVJUD CenterMeta qatoriga 0 bo'lib tushadi (EF ustun default'i CLR
            // default'i — C# property initializer'i ("= 6") faqat YANGI obyektga qo'llanadi).
            // 0 qolib ketsa "0 oy talab qilinadi" degani bo'lib, har bir o'quvchi darhol "tayyor"
            // bo'lib chiqardi. Shuning uchun mavjud qator(lar) aniq qiymatlarga tushiriladi.
            migrationBuilder.Sql("""
                UPDATE "CenterMeta"
                   SET "RetentionMonthsRequired" = 6,
                       "RetentionMaxGapMonths"   = 2
                 WHERE "RetentionMonthsRequired" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionBonusAwards");

            migrationBuilder.DropTable(
                name: "RetentionBonusShares");

            migrationBuilder.DropColumn(
                name: "RetentionBonus",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "RetentionBonusStartMonth",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "RetentionDefaultAmount",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "RetentionMaxGapMonths",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "RetentionMonthsRequired",
                table: "CenterMeta");
        }
    }
}
