using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStudentIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // «Qabul cheklisti» moduli butunlay olib tashlandi (2026-10-23, egasining qarori: adashib qo'shilgan).
            // Qo'lda belgilar jadvali va ularning tarix (audit) qatorlari ham o'chadi. O'quvchi ma'lumotiga
            // (o'quvchi, a'zolik, to'lov, shartnoma, Telegram, lid, daraja testi) TEGILMAYDI — cheklist ularni
            // faqat O'QIRDI.
            migrationBuilder.Sql(@"DELETE FROM ""AuditLogs"" WHERE ""EntityType"" = 'StudentIntake';");
            migrationBuilder.DropTable(
                name: "StudentIntakeMarks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentIntakeMarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Item = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MarkedAt = table.Column<string>(type: "text", nullable: false),
                    MarkedById = table.Column<string>(type: "text", nullable: false),
                    MarkedByName = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentIntakeMarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentIntakeMarks_StudentId_Item",
                table: "StudentIntakeMarks",
                columns: new[] { "StudentId", "Item" },
                unique: true);
        }
    }
}
