using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JournalApplyToAdmins",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JournalConductedOnly",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JournalEditMode",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "JournalRetroDays",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JournalApplyToAdmins",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "JournalConductedOnly",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "JournalEditMode",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "JournalRetroDays",
                table: "CenterMeta");
        }
    }
}
