using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsTemplateTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "SmsTemplates",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Eski "Avto SMS" (IsAuto=true) andozalari "yangi lid" hodisasiga ko'chiriladi.
            migrationBuilder.Sql(
                "UPDATE \"SmsTemplates\" SET \"Trigger\" = 'lead_new' WHERE \"IsAuto\" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "SmsTemplates");
        }
    }
}
