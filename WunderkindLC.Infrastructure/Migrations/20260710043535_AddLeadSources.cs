using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadSources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadSources", x => x.Id);
                });

            // Boshlang'ich to'ldirish: mavjud lidlarda ishlatilgan manbalar (tarix buzilmasin),
            // so'ng standart ro'yxatdagi yetishmayotganlari.
            migrationBuilder.Sql("""
                INSERT INTO "LeadSources" ("Id", "Name", "Order")
                SELECT gen_random_uuid()::text, s."Source",
                       (row_number() OVER (ORDER BY s."Source"))::int - 1
                FROM (SELECT DISTINCT "Source" FROM "Leads" WHERE COALESCE("Source", '') <> '') s;

                INSERT INTO "LeadSources" ("Id", "Name", "Order")
                SELECT gen_random_uuid()::text, v.name,
                       (SELECT COALESCE(MAX("Order"), -1) FROM "LeadSources") + v.ord
                FROM (VALUES ('Instagram', 1), ('Telegram', 2), ('Sayt', 3),
                             ('Tanish orqali', 4), ('Tashrif', 5), ('Boshqa', 6)) AS v(name, ord)
                WHERE NOT EXISTS (SELECT 1 FROM "LeadSources" ls WHERE ls."Name" = v.name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadSources");
        }
    }
}
