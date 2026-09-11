using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreCourseModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Yangi jadvallar avval yaratiladi — CourseLessons ga CourseSubTopics'dan MA'LUMOT
            //    KO'CHIRILADI (bir xil Id bilan — shu sabab CourseItems.LessonId (pastda RENAME
            //    qilinadi, hozircha eski SubTopicId) qayta yozilmasdan ham to'g'ri ishlayveradi).
            migrationBuilder.CreateTable(
                name: "CourseModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CurriculumId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourseLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CurriculumId = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLessons", x => x.Id);
                });

            // 2) CourseSubTopics -> CourseLessons: MA'LUMOT KO'CHIRILADI (bir xil Id bilan).
            migrationBuilder.Sql(@"
                INSERT INTO ""CourseLessons"" (""Id"", ""CurriculumId"", ""TopicId"", ""Title"", ""Note"", ""Order"")
                SELECT ""Id"", ""CurriculumId"", ""TopicId"", ""Title"", ""Note"", ""Order"" FROM ""CourseSubTopics"";
            ");

            migrationBuilder.DropTable(
                name: "CourseSubTopics");

            migrationBuilder.DropIndex(
                name: "IX_CourseTopics_CurriculumId_Order",
                table: "CourseTopics");

            // 3) CourseItems.SubTopicId -> LessonId (RENAME — qiymatlar CourseLessons'ning YUQORIDA
            //    bir xil Id bilan ko'chirilgan qatorlariga to'g'ri keladi, qayta yozish shart emas).
            migrationBuilder.RenameColumn(
                name: "SubTopicId",
                table: "CourseItems",
                newName: "LessonId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseItems_SubTopicId_Order",
                table: "CourseItems",
                newName: "IX_CourseItems_LessonId_Order");

            migrationBuilder.AlterColumn<string>(
                name: "CurriculumId",
                table: "CourseTopics",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "ModuleId",
                table: "CourseTopics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // 4) MAVJUD MAVZULARNI MODULGA BIRIKTIRISH: har Curriculum uchun (unda Mavzu bo'lsa)
            //    BITTA standart Modul ("1-modul") yaratiladi va o'sha dasturning BARCHA mavzulari
            //    shu modulga biriktiriladi — hech narsa yo'qolmaydi, faqat yangi guruhlash qatlami
            //    qo'shiladi (admin keyin qo'lda qayta tashkil qilishi mumkin).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE ""_module_migration_map"" AS
                SELECT DISTINCT
                    ct.""CurriculumId"" AS curriculum_id,
                    gen_random_uuid()::text AS new_module_id
                FROM ""CourseTopics"" ct;

                INSERT INTO ""CourseModules"" (""Id"", ""CurriculumId"", ""Name"", ""Note"", ""Order"")
                SELECT m.new_module_id, m.curriculum_id, '1-modul', '', 0
                FROM ""_module_migration_map"" m;

                UPDATE ""CourseTopics"" ct SET ""ModuleId"" = m.new_module_id
                FROM ""_module_migration_map"" m WHERE ct.""CurriculumId"" = m.curriculum_id;

                DROP TABLE ""_module_migration_map"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_CourseTopics_ModuleId_Order",
                table: "CourseTopics",
                columns: new[] { "ModuleId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseLessons_TopicId_Order",
                table: "CourseLessons",
                columns: new[] { "TopicId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseModules_CurriculumId_Order",
                table: "CourseModules",
                columns: new[] { "CurriculumId", "Order" });
        }

        /// <inheritdoc />
        /// <remarks>DIQQAT: bu Down() faqat sxemani qaytaradi — Up()dagi ma'lumot ko'chirish
        /// (Modul biriktirish) TESKARI qilinmaydi. Faqat migratsiya ishlatilishidan OLDIN
        /// (bo'sh CourseModules/CourseLessons) xavfsiz qaytariladi.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseModules");

            migrationBuilder.DropIndex(
                name: "IX_CourseTopics_ModuleId_Order",
                table: "CourseTopics");

            migrationBuilder.DropColumn(
                name: "ModuleId",
                table: "CourseTopics");

            migrationBuilder.RenameColumn(
                name: "LessonId",
                table: "CourseItems",
                newName: "SubTopicId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseItems_LessonId_Order",
                table: "CourseItems",
                newName: "IX_CourseItems_SubTopicId_Order");

            migrationBuilder.AlterColumn<string>(
                name: "CurriculumId",
                table: "CourseTopics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "CourseSubTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CurriculumId = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSubTopics", x => x.Id);
                });

            migrationBuilder.Sql(@"
                INSERT INTO ""CourseSubTopics"" (""Id"", ""CurriculumId"", ""TopicId"", ""Title"", ""Note"", ""Order"")
                SELECT ""Id"", ""CurriculumId"", ""TopicId"", ""Title"", ""Note"", ""Order"" FROM ""CourseLessons"";
            ");

            migrationBuilder.DropTable(
                name: "CourseLessons");

            migrationBuilder.CreateIndex(
                name: "IX_CourseTopics_CurriculumId_Order",
                table: "CourseTopics",
                columns: new[] { "CurriculumId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSubTopics_TopicId_Order",
                table: "CourseSubTopics",
                columns: new[] { "TopicId", "Order" });
        }
    }
}
