using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceTakenFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttendanceTaken",
                table: "LessonNotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: eski (shu paytgacha kiritilgan) "o'tildi" darslar avvalgi xulq-atvorni saqlab
            // qolishi uchun rassmiy davomat olingan deb hisoblanadi — faqat SHU DEPLOY'DAN KEYIN yangi
            // yaratiladigan LessonNote'lar uchun bu bayroq faqat "hammasi keldi/kelmadi" tugmasi orqali
            // (BulkAttendanceAsync) true bo'ladi, alohida baho kiritilganda avtomatik true bo'lmaydi.
            migrationBuilder.Sql(@"UPDATE ""LessonNotes"" SET ""AttendanceTaken"" = true WHERE ""Conducted"" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendanceTaken",
                table: "LessonNotes");
        }
    }
}
