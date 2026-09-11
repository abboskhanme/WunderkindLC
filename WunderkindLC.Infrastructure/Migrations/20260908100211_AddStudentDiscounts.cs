using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentDiscounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GroupName = table.Column<string>(type: "text", nullable: false),
                    TeacherId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TeacherName = table.Column<string>(type: "text", nullable: false),
                    Pct = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartMonth = table.Column<string>(type: "text", nullable: false),
                    EndMonth = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedById = table.Column<string>(type: "text", nullable: true),
                    EndedAt = table.Column<string>(type: "text", nullable: false),
                    EndedBy = table.Column<string>(type: "text", nullable: false),
                    CancelReason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentDiscounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentDiscounts_Status",
                table: "StudentDiscounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StudentDiscounts_StudentId_Status",
                table: "StudentDiscounts",
                columns: new[] { "StudentId", "Status" });

            // ---------- BACKFILL: mavjud chegirmalar registrga KO'CHIRILADI ----------
            //
            // ⚠️ Bu `AddStudentGroupPastPeriods` dagi qaroridan FARQ qiladi (u yerda backfill
            // ATAYIN qilinmagan edi) va sabab aniq: u yerda to'ldirish PUL hisobini o'zgartirardi,
            // bu yerda esa jadval pulga UMUMAN tegmaydi (hisob avvalgidek `Student.Discount*` dan).
            // Backfill bo'lmasa bo'lim birinchi kundanoq "bo'sh" ko'rinardi: chegirmasi bor
            // o'quvchining profilida "chegirma yo'q" deb turardi.
            //
            // Har chegirmali o'quvchi uchun BITTA `active` qator — invariant (bitta `active`
            // qator = `Student.Discount*`) shu bilan boshidanoq bajariladi.
            // Guruh/o'qituvchi SNAPSHOT'lari LEFT JOIN bilan olinadi: guruh biriktirilmagan
            // (yoki o'chirilgan) chegirmada ular bo'sh qoladi, qator baribir yaratiladi.
            // Vaqt — markaz mintaqasida (`AppClock` bilan bir xil), aks holda tarix 5 soat
            // surilgan ko'rinardi.
            migrationBuilder.Sql("""
                INSERT INTO "StudentDiscounts" (
                    "Id", "StudentId", "StudentName", "GroupId", "GroupName",
                    "TeacherId", "TeacherName", "Pct", "Amount",
                    "StartMonth", "EndMonth", "Reason", "Status",
                    "CreatedAt", "CreatedBy", "CreatedById", "EndedAt", "EndedBy", "CancelReason")
                SELECT
                    gen_random_uuid()::text,
                    s."Id",
                    s."FullName",
                    NULLIF(s."DiscountGroupId", ''),
                    COALESCE(g."Name", ''),
                    NULLIF(COALESCE(g."TeacherId", ''), ''),
                    COALESCE(t."FullName", ''),
                    s."DiscountPct",
                    s."DiscountAmount",
                    COALESCE(s."DiscountStartMonth", ''),
                    COALESCE(s."DiscountEndMonth", ''),
                    COALESCE(s."DiscountNote", ''),
                    'active',
                    to_char(now() AT TIME ZONE 'Asia/Tashkent', 'YYYY-MM-DD"T"HH24:MI:SS'),
                    'Ko''chirildi (eski yozuv)',
                    NULL, '', '', ''
                FROM "Students" s
                LEFT JOIN "Classes" g ON g."Id" = NULLIF(s."DiscountGroupId", '')
                LEFT JOIN "Teachers" t ON t."Id" = NULLIF(COALESCE(g."TeacherId", ''), '')
                WHERE s."DiscountPct" > 0 OR s."DiscountAmount" > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Jadval butunlay tushadi — registr TARIXI ham yo'qoladi. Bu qabul qilingan:
            // pul hisobi `Student.Discount*` da qoladi, ya'ni orqaga qaytish hech narsani buzmaydi.
            migrationBuilder.DropTable(
                name: "StudentDiscounts");
        }
    }
}
