using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadStageHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorUserId",
                table: "LeadEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromStage",
                table: "LeadEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToStage",
                table: "LeadEvents",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "LeadEvents");

            migrationBuilder.DropColumn(
                name: "FromStage",
                table: "LeadEvents");

            migrationBuilder.DropColumn(
                name: "ToStage",
                table: "LeadEvents");
        }
    }
}
