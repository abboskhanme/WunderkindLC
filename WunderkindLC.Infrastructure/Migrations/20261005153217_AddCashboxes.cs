using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashboxId",
                table: "FinanceTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cashboxes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResponsibleUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResponsibleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedByName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cashboxes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceTransactions_CashboxId",
                table: "FinanceTransactions",
                column: "CashboxId");

            // «Asosiy kassa» (`CashboxService.MainId`) va BARCHA eski yozuvlar unga (foydalanuvchi
            // qarori, `docs/SPEC-kassalar.md`) — aks holda kassa qoldiqlari faqat yangi to'lovlardan
            // hisoblanib, haqiqiy pulni ko'rsatmasdi. Bekor qilinganlar ham ko'chiriladi (ular baribir
            // qoldiqqa kirmaydi, lekin "qaysi kassada edi" savoliga javob bo'lsin).
            migrationBuilder.Sql(@"
INSERT INTO ""Cashboxes"" (""Id"", ""Name"", ""IsArchived"", ""CreatedAt"")
VALUES ('main', 'Asosiy kassa', FALSE, to_char(now() AT TIME ZONE 'Asia/Tashkent', 'YYYY-MM-DD""T""HH24:MI:SS'))
ON CONFLICT (""Id"") DO NOTHING;");
            migrationBuilder.Sql(@"UPDATE ""FinanceTransactions"" SET ""CashboxId"" = 'main' WHERE ""CashboxId"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cashboxes");

            migrationBuilder.DropIndex(
                name: "IX_FinanceTransactions_CashboxId",
                table: "FinanceTransactions");

            migrationBuilder.DropColumn(
                name: "CashboxId",
                table: "FinanceTransactions");
        }
    }
}
