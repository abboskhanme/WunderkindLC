using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadTelegramMessageFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ FK QO'SHISHDAN OLDIN YETIM QATORLAR TOZALANADI.
            // Jadval FK'siz yaratilgan edi (AddLeadTelegramMessages), ya'ni oradagi vaqtda
            // o'chirilgan lidning kartasi bazada qolib ketgan bo'lishi mumkin. Bunday qator
            // bo'lsa `ADD CONSTRAINT` prodda 23503 (foreign_key_violation) bilan YIQILADI va
            // butun deploy to'xtardi. Yozuv o'chishi xavfsiz: u faqat mavjud bo'lmagan lidning
            // Telegram xabariga ishora qiladi, ya'ni baribir hech qachon ishlatilmasdi.
            migrationBuilder.Sql(
                """DELETE FROM "LeadTelegramMessages" WHERE "LeadId" NOT IN (SELECT "Id" FROM "Leads");""");

            migrationBuilder.AddForeignKey(
                name: "FK_LeadTelegramMessages_Leads_LeadId",
                table: "LeadTelegramMessages",
                column: "LeadId",
                principalTable: "Leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeadTelegramMessages_Leads_LeadId",
                table: "LeadTelegramMessages");
        }
    }
}
