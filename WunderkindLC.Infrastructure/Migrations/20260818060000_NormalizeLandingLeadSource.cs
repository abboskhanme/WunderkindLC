using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// FAQAT MA'LUMOT MIGRATSIYASI — sxema o'zgarmaydi.
    ///
    /// <para>Landing sahifasidan kelgan lidlarga ilgari <c>Source = "sayt"</c> (kichik harf)
    /// yozilardi, manbalar ma'lumotnomasida esa yozuv <b>"Sayt"</b> (<c>AddLeadSources</c>
    /// migratsiyasida seed qilingan). <c>LeadsController</c> manba kesimini AYNAN MATN bo'yicha
    /// guruhlagani uchun CRM "Manba" filtrida bitta kanal IKKI alohida qator bo'lib ko'rinar va
    /// statistika ikkiga bo'linib ketardi.</para>
    ///
    /// <para><c>PublicLandingController</c> tuzatilgan (manba endi ma'lumotnomadan olinadi), ya'ni
    /// YANGI lidlar to'g'ri yoziladi. Bu yerda MAVJUD qatorlar ko'chiriladi.</para>
    ///
    /// <para><b>Nima ko'chiriladi:</b> faqat AYNAN shu so'z — registr farqisiz va chetki
    /// bo'shliqlar hisobga olinib (<c>"sayt"</c>, <c>"SAYT"</c>, <c>"Sayt "</c>). Begona
    /// qiymatlarga TEGILMAYDI: <c>"Saytdan"</c>, <c>"sayti"</c>, <c>"Instagram"</c>, bo'sh satr
    /// va <c>NULL</c> o'z holicha qoladi.</para>
    ///
    /// <para><c>"Source" &lt;&gt; 'Sayt'</c> sharti — allaqachon to'g'ri yozilgan qatorlar bekorga
    /// yangilanmasin (migratsiya IDEMPOTENT: ikkinchi marta ishlaganda 0 qator tegadi).</para>
    /// </summary>
    /// <inheritdoc />
    public partial class NormalizeLandingLeadSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Leads"
                SET "Source" = 'Sayt'
                WHERE lower(btrim("Source")) = 'sayt'
                  AND "Source" <> 'Sayt';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ATAYIN BO'SH — bu ma'lumot migratsiyasining aniq teskarisi YO'Q.
            //
            // Qaysi "Sayt" qatori ilgari "sayt" bo'lganini bilib bo'lmaydi: ko'chirilgandan keyin
            // ular ma'lumotnoma bo'yicha to'g'ri yozilgan qatorlardan (jumladan tuzatilgan
            // `PublicLandingController` yaratgan BARCHA yangi lidlardan) farq qilmaydi.
            //
            // Shuning uchun ko'r-ko'rona `UPDATE ... SET "Source" = 'sayt'` YOZILMADI: u orqaga
            // qaytarmas, aksincha SOG'LOM qatorlarni ham ma'lumotnomaga mos kelmaydigan holga
            // keltirib, aynan shu migratsiya tuzatgan muammoni QAYTA yaratardi.
            //
            // (Solishtiring: `ChangeFaceModelToSFace` da teskari `UPDATE` bor — u yerda ustunning
            // DEFAULT qiymati ham birga qaytariladi, ya'ni tizim butunlay eski holatga tushadi.
            // Bu yerda esa qaytariladigan sxema yo'q.)
            //
            // Migratsiyani orqaga qaytarish sxemani buzmaydi — manba nomlari normallashtirilgan
            // bo'lib qolaveradi.
        }
    }
}
