using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// Eski "Adminga topshiriq" (KUNLIK CHEKLIST) moduli butunlay olib tashlandi — uning o'rnini
    /// "Topshiriqlar" (Kanban: <c>WorkTask*</c>) egalladi, ya'ni ikkita bir-biriga o'xshash
    /// topshiriq tizimi yonma-yon turishiga hojat qolmadi.
    ///
    /// <para>⚠️ <b>MA'LUMOT YO'QOLADI:</b> <c>StaffTasks</c> (cheklist bandlari) va
    /// <c>StaffTaskLogs</c> (har kunlik bajarildi/bajarilmadi tarixi) o'chiriladi, shuningdek
    /// <c>CenterMeta</c> dagi kunlik jo'natish sozlamalari (<c>StaffTaskEnabled/Hour/Minute</c>).
    /// <c>Down</c> jadval SXEMASINI tiklaydi, MA'LUMOTNI emas — kerak bo'lsa tungi zaxiradan
    /// tiklanadi.</para>
    /// </summary>
    public partial class RemoveStaffTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffTaskLogs");

            migrationBuilder.DropTable(
                name: "StaffTasks");

            migrationBuilder.DropColumn(
                name: "StaffTaskEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "StaffTaskHour",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "StaffTaskMinute",
                table: "CenterMeta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StaffTaskEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StaffTaskHour",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StaffTaskMinute",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "StaffTaskLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<string>(type: "text", nullable: false),
                    Done = table.Column<bool>(type: "boolean", nullable: false),
                    DoneAt = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    StaffUserId = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffTaskLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    StaffUserId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffTasks", x => x.Id);
                });
        }
    }
}
