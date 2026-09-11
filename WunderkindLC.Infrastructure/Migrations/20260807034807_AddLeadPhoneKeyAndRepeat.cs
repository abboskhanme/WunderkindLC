using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// LIDGA IKKI QO'SHIMCHA (mavjud ma'lumot o'zgarmaydi):
    ///
    /// <para><b>1. <c>PhoneKey</c></b> — telefonning oxirgi 9 raqami, INDEKSLANGAN. Ilgari
    /// "shu telefon bilan lid bormi?" savoli (ommaviy forma va daraja testi har arizada so'raydi)
    /// butun <c>Leads</c> jadvalini xotiraga o'qib, kalitni har safar qayta hisoblab javob berardi.
    /// Ustunni ilova O'ZI to'ldiradi (<c>AppDbContext.SaveChanges</c>), bu yerda esa MAVJUD
    /// qatorlar bir marta to'ldiriladi — aks holda eski lidlar qidiruvdan tushib qolar va ular
    /// uchun DUBLIKAT lid ochilardi.</para>
    ///
    /// <para><b>2. <c>RepeatCount</c> / <c>LastRepeatAt</c></b> — takroriy murojaat belgisi
    /// (kanban kartasidagi «Takroriy N»). Eski qatorlarda 0/bo'sh bo'ladi: o'tmishdagi takroriy
    /// murojaatlar faqat lid izohida qolgan, ularni qayta tiklab bo'lmaydi.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddLeadPhoneKeyAndRepeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastRepeatAt",
                table: "Leads",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhoneKey",
                table: "Leads",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RepeatCount",
                table: "Leads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // MAVJUD lidlarni to'ldirish — `PhoneUtil.Key` bilan AYNAN bir xil qoida: faqat
            // raqamlar qoldiriladi va oxirgi 9 tasi olinadi (9 tadan qisqa bo'lsa — o'zi).
            // Indeksdan OLDIN bajariladi (indeksni bir marta qurish arzonroq).
            migrationBuilder.Sql(
                """
                UPDATE "Leads"
                SET "PhoneKey" = right(regexp_replace(coalesce("Phone", ''), '[^0-9]', '', 'g'), 9);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_PhoneKey",
                table: "Leads",
                column: "PhoneKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_PhoneKey",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "LastRepeatAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "PhoneKey",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "RepeatCount",
                table: "Leads");
        }
    }
}
