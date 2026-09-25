using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffRoleLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleTemplateId",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryChargedBaseFrom",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Foydalanuvchi qarori (2026-09-25): foizli maosh bazasi JORIY OYDAN hisoblangan to'liq
            // oylik. O'tgan oylar yig'ilgan pulda qoladi (maosh allaqachon berilgan). Yangi baza
            // (qator hali yo'q) bo'lsa — default "" (eski qoida), sozlama keyin qo'yiladi.
            migrationBuilder.Sql("UPDATE \"CenterMeta\" SET \"SalaryChargedBaseFrom\" = '2026-09';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoleTemplateId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SalaryChargedBaseFrom",
                table: "CenterMeta");
        }
    }
}
