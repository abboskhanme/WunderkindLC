using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// Shartnoma oqimi soddalashtirildi: PDF nusxani tizim hosil qilmaydi — superadmin
    /// tayyor PDF'ni o'zi yuklaydi va u <c>PdfUrl</c>da saqlanadi. Shu sababli alohida
    /// "imzolangan nusxa" ustuni (<c>SignedUrl</c>) keraksiz bo'lib qoldi.
    /// </summary>
    public partial class RemoveContractSignedUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignedUrl",
                table: "Contracts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignedUrl",
                table: "Contracts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
