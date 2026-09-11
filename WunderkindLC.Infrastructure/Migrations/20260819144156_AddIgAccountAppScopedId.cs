using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// <c>IgAccounts.AppScopedUserId</c> — Instagram akkauntining APP-SCOPED id'si (<c>me.id</c>),
    /// <c>IgUserId</c> (<c>me.user_id</c>) dan boshqa raqam. Webhook'da <c>from.id</c> ba'zan biri,
    /// ba'zan ikkinchisi bo'lib keladi va faqat bittasini saqlash cheksiz halqa himoyasini
    /// teshadi (<c>.claude/rules/marketing-instagram.md</c> §4).
    ///
    /// <para>⚠️ <b>NEGA XOM SQL va <c>IF NOT EXISTS</c>:</b> shu migratsiyani yaratganda EF
    /// modelga kiritilgan, lekin hech qachon migratsiya qilinmagan <c>CenterMeta</c> ustunlarini
    /// ham qo'shib yubordi. Ular <b>allaqachon mavjud</b>: <c>Program.cs</c> ularni har startupda
    /// <c>ALTER TABLE … ADD COLUMN IF NOT EXISTS</c> bilan qo'shadi (eski o'rnatishlar uchun
    /// qoldirilgan mexanizm). Oddiy <c>AddColumn</c> bo'lsa ishlab turgan bazada migratsiya
    /// «column already exists» bilan yiqilar va DEPLOY TO'XTAB QOLARDI. Shuning uchun butun
    /// blok idempotent xom SQL — bo'sh bazada ham, ishlab turgan bazada ham xatosiz o'tadi.</para>
    /// </summary>
    public partial class AddIgAccountAppScopedId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""IgAccounts"" ADD COLUMN IF NOT EXISTS ""AppScopedUserId"" text NOT NULL DEFAULT '';

                -- Quyidagilar modelda bor edi-yu, migratsiyasi yo'q edi (Program.cs qo'shadi).
                -- Bu yerda ham qo'shiladi, aks holda ModelSnapshot bilan baza ayri ketaverardi.
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""MapIframeUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""TelegramUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""InstagramUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""YoutubeUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""FacebookUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterEmail"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""AppStoreUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""PlayMarketUrl"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""ContactPhone"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""CenterAddress"" text NOT NULL DEFAULT '';
                ALTER TABLE ""CenterMeta"" ADD COLUMN IF NOT EXISTS ""WorkingHours"" text NOT NULL DEFAULT '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // FAQAT shu migratsiya HAQIQATAN kiritgan ustun qaytariladi. `CenterMeta` ustunlari
            // bu yerdan OLDIN ham mavjud edi (Program.cs qo'shgan) — ularni tashlash orqaga
            // qaytishda ishlab turgan bazadagi ma'lumotni yo'q qilardi.
            migrationBuilder.Sql(@"ALTER TABLE ""IgAccounts"" DROP COLUMN IF EXISTS ""AppScopedUserId"";");
        }
    }
}
