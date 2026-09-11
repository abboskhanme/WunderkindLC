using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetentionTrackEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DIQQAT: defaultValue TRUE. EF ustun default'i sifatida CLR default'ini (false)
            // qo'yardi — u holda MAVJUD track qatorlari (bonus berilgan yoki «Qayta boshlash»
            // qilingan o'quvchilar) jimgina O'CHIRILGAN holatga tushib, bonus hisobotidan
            // yo'qolib ketardi. Ular allaqachon tizimda — shuning uchun yoqilgan bo'lib qoladi.
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "RetentionBonusTracks",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "RetentionBonusTracks");
        }
    }
}
