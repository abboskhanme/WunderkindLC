using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// TUZATISH: AddStaffRoleTemplates (20260622) migratsiyasi <c>DefaultPermissions</c>ni
    /// <c>text</c> (JSON satr '["leads","messages"]') qilib yaratgan, model/snapshot esa keyin
    /// EF8 primitive collection (<c>text[]</c>)ga o'zgargan — ALTER migratsiyasi yozilmagan.
    /// Natijada jadvalning HAR QANDAY o'qilishi InvalidCastException bilan yiqilardi
    /// ("Xodimlar va rollar"da rol shablonlari 500 qaytarardi). Snapshot allaqachon text[]
    /// bo'lgani uchun EF diff bo'sh — SQL QO'LDA yozildi (idempotent: ustun turi tekshiriladi,
    /// allaqachon to'g'ri bazada zararsiz o'tadi). CreatedAt ham xuddi shu drift:
    /// timestamptz -&gt; timestamp (model/AppClock legacy behavior bilan moslash).
    /// </summary>
    public partial class FixStaffRoleTemplateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    -- DefaultPermissions: text (JSON satr) -> text[] (Npgsql primitive collection).
    -- ALTER..USING ichida subquery mumkin emas (0A000) — vaqtinchalik ustun orqali.
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'StaffRoleTemplates'
                 AND column_name = 'DefaultPermissions'
                 AND data_type = 'text') THEN
        ALTER TABLE ""StaffRoleTemplates""
            ADD COLUMN ""DefaultPermissions_new"" text[] NOT NULL DEFAULT '{}';
        UPDATE ""StaffRoleTemplates""
            SET ""DefaultPermissions_new"" =
                CASE
                    WHEN ""DefaultPermissions"" IS NULL
                         OR ""DefaultPermissions"" IN ('', '[]') THEN '{}'::text[]
                    ELSE ARRAY(SELECT jsonb_array_elements_text(""DefaultPermissions""::jsonb))
                END;
        ALTER TABLE ""StaffRoleTemplates"" DROP COLUMN ""DefaultPermissions"";
        ALTER TABLE ""StaffRoleTemplates""
            RENAME COLUMN ""DefaultPermissions_new"" TO ""DefaultPermissions"";
        ALTER TABLE ""StaffRoleTemplates""
            ALTER COLUMN ""DefaultPermissions"" DROP DEFAULT;
    END IF;

    -- CreatedAt: timestamptz -> timestamp (AppClock.Now Kind=Unspecified, legacy behavior).
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'StaffRoleTemplates'
                 AND column_name = 'CreatedAt'
                 AND data_type = 'timestamp with time zone') THEN
        ALTER TABLE ""StaffRoleTemplates""
            ALTER COLUMN ""CreatedAt"" TYPE timestamp without time zone
            USING (""CreatedAt"" AT TIME ZONE 'UTC');
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""StaffRoleTemplates""
    ALTER COLUMN ""DefaultPermissions"" TYPE text
    USING (to_jsonb(""DefaultPermissions"")::text);
ALTER TABLE ""StaffRoleTemplates""
    ALTER COLUMN ""CreatedAt"" TYPE timestamp with time zone
    USING (""CreatedAt"" AT TIME ZONE 'UTC');
");
        }
    }
}
