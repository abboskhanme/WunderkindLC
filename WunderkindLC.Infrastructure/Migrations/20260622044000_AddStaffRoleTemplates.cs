using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffRoleTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaffRoleTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DefaultPermissions = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRoleTemplates", x => x.Id);
                });

            // Seed 3 ta template (raw SQL — InsertData emas, entity mapping kerak emas)
            migrationBuilder.Sql(
                @"INSERT INTO ""StaffRoleTemplates"" (""Id"", ""Code"", ""Name"", ""Description"", ""DefaultPermissions"", ""CreatedAt"")
                VALUES
                ('template-call-operator', 'call_operator', 'Qo''ng''iroq operatori', 'Qo''ng''iroq qabul qiladi va lidlarni boshqaradi', '[""leads"",""messages""]', CURRENT_TIMESTAMP),
                ('template-cashier', 'cashier', 'Kassir', 'To''lovlarni kiritadi va boshqaradi, o''quvchi ma''lumotlarini ko''radi', '[""students"",""finance"",""messages""]', CURRENT_TIMESTAMP),
                ('template-administrator', 'administrator', 'Administrator', 'Asosiy boshqaruv — guruhlar, o''quvchilar, o''qituvchilar, o''quv bo''limi', '[""leads"",""students"",""teachers"",""classes"",""schedule"",""messages"",""app""]', CURRENT_TIMESTAMP)
                ON CONFLICT DO NOTHING");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StaffRoleTemplates");
        }
    }
}
