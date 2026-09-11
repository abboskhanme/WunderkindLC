using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeFaceModelToSFace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column default qiymatini yangilash (yangi qatorlar uchun).
            migrationBuilder.AlterColumn<string>(
                name: "LoginFaceModelVersion",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "sface-2021dec-int8-v1",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "buffalo_s@512");

            // Mavjud qatorlar: buffalo_s@512 → sface-2021dec-int8-v1.
            // Faqat DEFAULT qiymatni saqlagan qatorlar yangilanadi — admin qo'lda o'zgartirgan
            // bo'lsa (masalan boshqa qiymat yozgan bo'lsa) tegmaydi.
            migrationBuilder.Sql(
                "UPDATE \"CenterMeta\" SET \"LoginFaceModelVersion\" = 'sface-2021dec-int8-v1' " +
                "WHERE \"LoginFaceModelVersion\" = 'buffalo_s@512'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "LoginFaceModelVersion",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "buffalo_s@512",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "sface-2021dec-int8-v1");

            migrationBuilder.Sql(
                "UPDATE \"CenterMeta\" SET \"LoginFaceModelVersion\" = 'buffalo_s@512' " +
                "WHERE \"LoginFaceModelVersion\" = 'sface-2021dec-int8-v1'");
        }
    }
}

