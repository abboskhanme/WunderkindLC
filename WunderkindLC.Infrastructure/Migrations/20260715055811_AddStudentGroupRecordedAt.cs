using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentGroupRecordedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedAt",
                table: "StudentGroups",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Mavjud yozuvlar uchun RecordedAt = JoinedAt (eng erta oqilona qiymat) — shu bilan jurnalning
            // "PresentDefaultFrom" cheklovi ESKI a'zoliklar uchun hech narsani o'zgartirmaydi (MemberStart >=
            // JoinedAt bo'lgani uchun cheklov amalda ishlamaydi). Faqat shu migratsiyadan KEYINGI yangi
            // qo'shish/aktivlashtirish amallari haqiqiy bugungi sanani yozadi.
            migrationBuilder.Sql("""
                UPDATE "StudentGroups" SET "RecordedAt" = "JoinedAt" WHERE "RecordedAt" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "StudentGroups");
        }
    }
}
