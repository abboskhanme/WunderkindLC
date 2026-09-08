using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntellectCRM.Infrastructure.Migrations
{
    /// <summary>
    /// Chegirma qatoriga FAN (kurs) snapshot'i: <c>CourseId</c> + <c>CourseName</c>.
    ///
    /// <para>Foydalanuvchi chegirmani "guruh" emas, <b>FAN</b> deb o'ylaydi ("Matematikaga 20%"),
    /// shuning uchun UI birinchi navbatda fanni ko'rsatadi. Snapshot — qolgan snapshotlar bilan
    /// bir xil sabab: kurs nomi o'zgarsa yoki guruh o'chirilsa ham tarix o'qilishi kerak.</para>
    ///
    /// <para>MAVJUD qatorlar <c>Classes</c> -> <c>Subjects</c> bilan LEFT JOIN qilib to'ldiriladi:
    /// guruhga biriktirilmagan («barcha guruhlar») va kursi yo'q guruh chegirmalari bo'sh qoladi —
    /// bu NORMAL holat, ular «Barcha guruhlar» deb ko'rsatiladi.</para>
    /// </summary>
    public partial class AddStudentDiscountCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CourseId",
                table: "StudentDiscounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourseName",
                table: "StudentDiscounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            // ⚠️ Indeks QAMROV bo'yicha qidiruv uchun ("shu fanda allaqachon chegirma bormi" — 409).
            // Unikal EMAS: tarixiy (`replaced`/`cancelled`) qatorlar bir qamrovda ko'p bo'ladi.
            migrationBuilder.CreateIndex(
                name: "IX_StudentDiscounts_StudentId_GroupId_Status",
                table: "StudentDiscounts",
                columns: new[] { "StudentId", "GroupId", "Status" });

            // ---------- BACKFILL: mavjud qatorlarga FAN snapshot'i ----------
            // Faqat guruhga BIRIKTIRILGAN chegirmalar to'ldiriladi; «barcha guruhlar» qatorlari
            // (`GroupId IS NULL`) va kursi yo'q guruhlar bo'sh qoladi.
            // ⚠️ Pul mantig'iga TEGMAYDI — bu faqat KO'RSATISH uchun nom.
            migrationBuilder.Sql("""
                UPDATE "StudentDiscounts" d
                SET "CourseId"   = NULLIF(COALESCE(g."CourseId", ''), ''),
                    "CourseName" = COALESCE(sub."Name", '')
                FROM "Classes" g
                LEFT JOIN "Subjects" sub ON sub."Id" = NULLIF(COALESCE(g."CourseId", ''), '')
                WHERE g."Id" = d."GroupId" AND d."GroupId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentDiscounts_StudentId_GroupId_Status",
                table: "StudentDiscounts");

            migrationBuilder.DropColumn(
                name: "CourseId",
                table: "StudentDiscounts");

            migrationBuilder.DropColumn(
                name: "CourseName",
                table: "StudentDiscounts");
        }
    }
}
