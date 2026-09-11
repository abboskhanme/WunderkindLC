using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleCurriculumFromSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "CourseTopics",
                newName: "CurriculumId");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "CourseSubTopics",
                newName: "CurriculumId");

            migrationBuilder.RenameColumn(
                name: "CourseId",
                table: "CourseProgresses",
                newName: "CurriculumId");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "CourseLevels",
                newName: "CurriculumId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseLevels_SubjectId_Order",
                table: "CourseLevels",
                newName: "IX_CourseLevels_CurriculumId_Order");

            migrationBuilder.RenameColumn(
                name: "SubjectId",
                table: "CourseItems",
                newName: "CurriculumId");

            migrationBuilder.AddColumn<string>(
                name: "CreatedAt",
                table: "CourseItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Curricula",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Curricula", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubjectCurricula",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurriculumId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectCurricula", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCurricula_CurriculumId",
                table: "SubjectCurricula",
                column: "CurriculumId");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectCurricula_SubjectId_CurriculumId",
                table: "SubjectCurricula",
                columns: new[] { "SubjectId", "CurriculumId" },
                unique: true);

            // MAVJUD DASTUR MA'LUMOTLARINI KO'CHIRISH: yuqoridagi RenameColumn'lardan so'ng
            // CourseLevels/Topics/SubTopics/Items/Progresses."CurriculumId" hali ESKI Subject.Id
            // qiymatlarini saqlaydi (haqiqiy Curriculum id emas). Har bir shunday Subject uchun
            // YANGI standalone Curriculum yaratiladi (kurs nomi bilan) + SubjectCurriculum orqali
            // o'sha kursga biriktiriladi, so'ng barcha 5 jadval yangi Curriculum.Id'ga o'tkaziladi —
            // MA'LUMOT YO'QOTILMAYDI, faqat egasi Subject'dan Curriculum'ga almashadi.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE ""_curriculum_migration_map"" AS
                SELECT DISTINCT
                    cl.""CurriculumId"" AS old_subject_id,
                    gen_random_uuid()::text AS new_curriculum_id
                FROM ""CourseLevels"" cl;

                INSERT INTO ""Curricula"" (""Id"", ""Name"", ""Note"", ""Order"", ""CreatedAt"")
                SELECT m.new_curriculum_id, COALESCE(s.""Name"", 'O''quv dasturi'), '', 0, ''
                FROM ""_curriculum_migration_map"" m
                LEFT JOIN ""Subjects"" s ON s.""Id"" = m.old_subject_id;

                INSERT INTO ""SubjectCurricula"" (""Id"", ""SubjectId"", ""CurriculumId"", ""Order"")
                SELECT gen_random_uuid()::text, m.old_subject_id, m.new_curriculum_id, 0
                FROM ""_curriculum_migration_map"" m;

                UPDATE ""CourseLevels"" cl SET ""CurriculumId"" = m.new_curriculum_id
                FROM ""_curriculum_migration_map"" m WHERE cl.""CurriculumId"" = m.old_subject_id;

                UPDATE ""CourseTopics"" ct SET ""CurriculumId"" = m.new_curriculum_id
                FROM ""_curriculum_migration_map"" m WHERE ct.""CurriculumId"" = m.old_subject_id;

                UPDATE ""CourseSubTopics"" cst SET ""CurriculumId"" = m.new_curriculum_id
                FROM ""_curriculum_migration_map"" m WHERE cst.""CurriculumId"" = m.old_subject_id;

                UPDATE ""CourseItems"" ci SET ""CurriculumId"" = m.new_curriculum_id
                FROM ""_curriculum_migration_map"" m WHERE ci.""CurriculumId"" = m.old_subject_id;

                UPDATE ""CourseProgresses"" cp SET ""CurriculumId"" = m.new_curriculum_id
                FROM ""_curriculum_migration_map"" m WHERE cp.""CurriculumId"" = m.old_subject_id;

                DROP TABLE ""_curriculum_migration_map"";
            ");
        }

        /// <inheritdoc />
        /// <remarks>DIQQAT: bu Down() faqat sxemani qaytaradi — Up()dagi ma'lumot ko'chirish
        /// (Subject.Id → yangi Curriculum.Id) TESKARI qilinmaydi. Faqat migratsiya ishlatilishidan
        /// OLDIN (bo'sh Curricula) xavfsiz qaytariladi.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Curricula");

            migrationBuilder.DropTable(
                name: "SubjectCurricula");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CourseItems");

            migrationBuilder.RenameColumn(
                name: "CurriculumId",
                table: "CourseTopics",
                newName: "SubjectId");

            migrationBuilder.RenameColumn(
                name: "CurriculumId",
                table: "CourseSubTopics",
                newName: "SubjectId");

            migrationBuilder.RenameColumn(
                name: "CurriculumId",
                table: "CourseProgresses",
                newName: "CourseId");

            migrationBuilder.RenameColumn(
                name: "CurriculumId",
                table: "CourseLevels",
                newName: "SubjectId");

            migrationBuilder.RenameIndex(
                name: "IX_CourseLevels_CurriculumId_Order",
                table: "CourseLevels",
                newName: "IX_CourseLevels_SubjectId_Order");

            migrationBuilder.RenameColumn(
                name: "CurriculumId",
                table: "CourseItems",
                newName: "SubjectId");
        }
    }
}
