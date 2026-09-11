using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Trigger = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MessageTemplate = table.Column<string>(type: "text", nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderRules", x => x.Id);
                });

            // Eski "PaymentRemindersEnabled" bool qiymatini yangi "payment_debt" eslatma qoidasiga ko'chiramiz
            // (ustun o'chirilishidan OLDIN — mavjud sozlama yo'qolmasin).
            migrationBuilder.Sql(@"
                INSERT INTO ""ReminderRules"" (""Id"", ""Trigger"", ""Name"", ""Enabled"", ""MessageTemplate"", ""OffsetMinutes"", ""CreatedAt"")
                SELECT '11111111-1111-1111-1111-111111111111', 'payment_debt', 'Qarzdorlik eslatmasi',
                       COALESCE(""PaymentRemindersEnabled"", true), '', 0, to_char(now(), 'YYYY-MM-DD""T""HH24:MI:SS')
                FROM ""CenterMeta"" LIMIT 1;
            ");

            // Yangi davomat-eslatmasi qoidasi — default O'CHIRILGAN (admin yoqib, kerak bo'lsa matnini tahrirlaydi).
            migrationBuilder.Sql(@"
                INSERT INTO ""ReminderRules"" (""Id"", ""Trigger"", ""Name"", ""Enabled"", ""MessageTemplate"", ""OffsetMinutes"", ""CreatedAt"")
                VALUES ('22222222-2222-2222-2222-222222222222', 'lesson_attendance', 'Davomat eslatmasi (o''qituvchi)', false,
                        'Assalomu alaykum, {fish}! {guruh} guruhida ({kurs}) dars boshlandi ({dars_vaqti}). Iltimos, davomatni jurnalga kiriting.',
                        5, to_char(now(), 'YYYY-MM-DD""T""HH24:MI:SS'));
            ");

            migrationBuilder.DropColumn(
                name: "PaymentRemindersEnabled",
                table: "CenterMeta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderRules");

            migrationBuilder.AddColumn<bool>(
                name: "PaymentRemindersEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
