using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWikiSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSection",
                table: "WikiPages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSection",
                table: "WikiPages");
        }
    }
}
