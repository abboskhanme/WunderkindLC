using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseSubTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Ustun RENAME qilinadi — bu bosqichda "SubTopicId" hali ESKI TopicId qiymatlarini
            //    saqlaydi (haqiqiy sub-mavzu id'si emas). Quyida (3) bosqichda to'g'ri qiymatga
            //    o'tkaziladi — MA'LUMOT YO'QOTILMAYDI, faqat ma'nosi tuzatiladi.
            migrationBuilder.RenameColumn(
                name: "TopicId",
                table: "CourseItems",
                newName: "SubTopicId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseItems_TopicId_Order",
                table: "CourseItems",
                newName: "IX_CourseItems_SubTopicId_Order");

            migrationBuilder.CreateTable(
                name: "CourseSubTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSubTopics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSubTopics_TopicId_Order",
                table: "CourseSubTopics",
                columns: new[] { "TopicId", "Order" });

            // 2) MAVJUD DASTUR MA'LUMOTLARINI KO'CHIRISH: har (Mavzu, Tur) juftligi uchun — shu
            //    juftlikda kamida bitta band bo'lsa — bitta CourseSubTopic yaratiladi (nomi tur
            //    yorlig'i, masalan "Video darslar"). gen_random_uuid() PG 13+ da mavjud, ammo
            //    pgcrypto kengaytmasi orqali kafolatlash uchun avval yoqiladi (idempotent).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql(@"
                INSERT INTO ""CourseSubTopics"" (""Id"", ""SubjectId"", ""TopicId"", ""Title"", ""Note"", ""Order"", ""Type"")
                SELECT
                    gen_random_uuid()::text,
                    t.""SubjectId"",
                    t.""Id"",
                    CASE g.""Type""
                        WHEN 'video' THEN 'Video darslar'
                        WHEN 'audio' THEN 'Audio darslar'
                        WHEN 'pdf' THEN 'PDF darslar'
                        WHEN 'vocab' THEN 'Lug''at darslar'
                        WHEN 'test' THEN 'Test darslar'
                        ELSE 'Matn darslar'
                    END,
                    '',
                    ROW_NUMBER() OVER (PARTITION BY t.""Id"" ORDER BY g.""Type"") - 1,
                    g.""Type""
                FROM (SELECT DISTINCT ""SubTopicId"" AS ""OldTopicId"", ""Type"" FROM ""CourseItems"") g
                JOIN ""CourseTopics"" t ON t.""Id"" = g.""OldTopicId"";
            ");

            // 3) Bandlarni YANGI (haqiqiy) sub-mavzu id'siga o'tkazish — hozircha ""SubTopicId""
            //    hali ESKI mavzu id'sini saqlaydi (1-bosqich), shu bo'yicha (mavzu+tur) mos
            //    sub-mavzuni topib qiymat almashtiriladi.
            migrationBuilder.Sql(@"
                UPDATE ""CourseItems"" ci
                SET ""SubTopicId"" = st.""Id""
                FROM ""CourseSubTopics"" st
                WHERE st.""TopicId"" = ci.""SubTopicId"" AND st.""Type"" = ci.""Type"";
            ");
        }

        /// <inheritdoc />
        /// <remarks>DIQQAT: bu Down() faqat sxemani qaytaradi — Up()dagi (2)-(3) ma'lumot ko'chirish
        /// TESKARI qilinmaydi (CourseItems.SubTopicId sub-mavzu id'si bo'lib qoladi, Topic emas).
        /// Faqat migratsiya ishlatilishidan OLDIN (bo'sh CourseSubTopics) xavfsiz qaytariladi.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseSubTopics");

            migrationBuilder.RenameColumn(
                name: "SubTopicId",
                table: "CourseItems",
                newName: "TopicId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseItems_SubTopicId_Order",
                table: "CourseItems",
                newName: "IX_CourseItems_TopicId_Order");
        }
    }
}
