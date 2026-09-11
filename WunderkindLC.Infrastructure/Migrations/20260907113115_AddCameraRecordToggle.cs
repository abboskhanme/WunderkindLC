using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraRecordToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CameraRecordEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ⚠️ MAVJUD kameralar uchun TRUE — entity default'i ham `true`. Sabab: bu bayroq
            // "shu kamerani ISTISNO qil" degani, "hech narsa yozilmasin" degani EMAS. Yozuvni
            // butunlay o'chirish/yoqish BOSH kalit bilan bo'ladi (CenterMeta.CameraRecordEnabled,
            // u default FALSE). Bu ustun `false` bilan to'ldirilsa admin bosh kalitni yoqib,
            // "yoqdim — lekin baribir yozilmayapti" holatiga tushardi (har kamerani qo'lda
            // ochish kerak bo'lardi).
            migrationBuilder.AddColumn<bool>(
                name: "RecordEnabled",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CameraRecordEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "RecordEnabled",
                table: "Cameras");
        }
    }
}
