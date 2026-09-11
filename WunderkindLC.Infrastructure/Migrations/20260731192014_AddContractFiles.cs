using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocxUrl",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PdfUrl",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SignedUrl",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TemplateName",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Eski yozuvlar ham ko'rinadigan bo'lib qolsin (entity default'i ham true) —
            // shuning uchun defaultValue: true (EF avtomatik false qo'yardi).
            migrationBuilder.AddColumn<bool>(
                name: "Visible",
                table: "Contracts",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocxUrl",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "PdfUrl",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedUrl",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "TemplateName",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "Visible",
                table: "Contracts");
        }
    }
}
