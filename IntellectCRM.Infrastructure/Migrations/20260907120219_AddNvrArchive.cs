using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntellectCRM.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNvrArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NvrEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NvrHost",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Mavjud qatorlarga MA'NOLI standart port (kod 0 ni ham to'g'rilaydi, lekin
            // bazadagi qiymat UI'da ko'rinadi — 0 chalg'itardi).
            migrationBuilder.AddColumn<int>(
                name: "NvrIsapiPort",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<int>(
                name: "NvrRtspPort",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 554);

            migrationBuilder.AddColumn<string>(
                name: "NvrVendor",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "hikvision");

            migrationBuilder.AddColumn<int>(
                name: "NvrChannel",
                table: "Cameras",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NvrEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "NvrHost",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "NvrIsapiPort",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "NvrRtspPort",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "NvrVendor",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "NvrChannel",
                table: "Cameras");
        }
    }
}
