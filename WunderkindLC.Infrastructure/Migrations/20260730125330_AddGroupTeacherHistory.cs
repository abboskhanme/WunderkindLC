using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupTeacherHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupTeacherAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    GroupId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeacherId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromDate = table.Column<string>(type: "text", nullable: false),
                    ToDate = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupTeacherAssignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupTeacherAssignments_GroupId_FromDate",
                table: "GroupTeacherAssignments",
                columns: new[] { "GroupId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupTeacherAssignments_TeacherId",
                table: "GroupTeacherAssignments",
                column: "TeacherId");

            // ---------- BACKFILL: mavjud guruhlar uchun bitta ochiq biriktirish ----------
            // Bazada o'qituvchi TARIXI umuman yo'q edi — faqat Group."TeacherId" (hozirgi o'qituvchi).
            // Shuning uchun o'tmishdagi almashuvlarni tiklab bo'lmaydi. Mavjud yagona oqilona
            // taxmin: "hozirgi o'qituvchi guruh boshlanganidan beri o'qitgan" — FromDate sifatida
            //   1) guruh "StartDate" (kurs boshlanish sanasi), yo'q bo'lsa
            //   2) shu guruhga eng erta qo'shilgan o'quvchining "JoinedAt", u ham yo'q bo'lsa
            //   3) bugungi sana
            // olinadi. Nega bugungi sana emas: agar tarix faqat bugundan boshlansa, ertaga
            // o'qituvchi almashgan zahoti O'TMISHDAGI oylar YANGI o'qituvchiga yozilib qolardi
            // (TeacherAtMonth topa olmay Group.TeacherId'ga fallback qiladi). Guruh boshlanishidan
            // yozib qo'yish — noto'g'ri emas, shunchaki aniqligi cheklangan taxmin.
            // CreatedBy = 'migratsiya' → bu qatorlar taxmin ekani auditda ko'rinib turadi.
            migrationBuilder.Sql("""
                INSERT INTO "GroupTeacherAssignments" ("Id", "GroupId", "TeacherId", "FromDate", "ToDate", "CreatedBy")
                SELECT
                    gen_random_uuid()::text,
                    c."Id",
                    c."TeacherId",
                    COALESCE(
                        NULLIF(c."StartDate", ''),
                        (SELECT MIN(sg."JoinedAt") FROM "StudentGroups" sg
                          WHERE sg."GroupId" = c."Id" AND sg."JoinedAt" <> ''),
                        to_char(CURRENT_DATE, 'YYYY-MM-DD')
                    ),
                    NULL,
                    'migratsiya'
                FROM "Classes" c
                WHERE c."TeacherId" IS NOT NULL AND c."TeacherId" <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupTeacherAssignments");
        }
    }
}
