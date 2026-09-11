using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalSms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "SmsLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "SmsLogs",
                type: "text",
                nullable: false,
                defaultValue: "eskiz");

            migrationBuilder.AddColumn<string>(
                name: "LocalSmsDefaultAgentId",
                table: "CenterMeta",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LocalSmsEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmsProvider",
                table: "AutoMessageRules",
                type: "text",
                nullable: false,
                defaultValue: "eskiz");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "SmsLogs");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "SmsLogs");

            migrationBuilder.DropColumn(
                name: "LocalSmsDefaultAgentId",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LocalSmsEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "SmsProvider",
                table: "AutoMessageRules");
        }
    }
}
