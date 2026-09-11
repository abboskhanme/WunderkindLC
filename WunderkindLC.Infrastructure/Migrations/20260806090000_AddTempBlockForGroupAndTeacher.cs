using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// VAQTINCHA BLOKLASH — guruh uchun ham, o'qituvchi uchun ham.
    ///
    /// <para>Guruh (Classes): <c>IsBlocked</c> bo'lsa guruh o'qituvchi ilovasida umuman ko'rinmaydi
    /// (ro'yxat, jurnal, baholash, testlar, chat) — admin panelida esa odatdagidek qoladi.</para>
    ///
    /// <para>O'qituvchi (Teachers): <c>IsBlocked</c> bo'lsa tizimga kira olmaydi (login va mavjud
    /// token rad etiladi), lekin paroli va butun tarixi saqlanadi — arxivlashdan farqi shu.</para>
    ///
    /// <para>Mavjud qatorlarga <c>defaultValue: false</c> / <c>""</c> qo'yiladi, ya'ni
    /// YANGILANGANDAN KEYIN hech kim va hech nima bloklangan holatda kelmaydi.</para>
    /// </summary>
    public partial class AddTempBlockForGroupAndTeacher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Classes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BlockedAt",
                table: "Classes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockNote",
                table: "Classes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Teachers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BlockedAt",
                table: "Teachers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockNote",
                table: "Teachers",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsBlocked", table: "Classes");
            migrationBuilder.DropColumn(name: "BlockedAt", table: "Classes");
            migrationBuilder.DropColumn(name: "BlockNote", table: "Classes");

            migrationBuilder.DropColumn(name: "IsBlocked", table: "Teachers");
            migrationBuilder.DropColumn(name: "BlockedAt", table: "Teachers");
            migrationBuilder.DropColumn(name: "BlockNote", table: "Teachers");
        }
    }
}
