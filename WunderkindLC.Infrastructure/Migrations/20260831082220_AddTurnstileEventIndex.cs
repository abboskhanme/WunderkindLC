using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnstileEventIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TurnstileEvents_DeviceUserId_EventAt",
                table: "TurnstileEvents",
                columns: new[] { "DeviceUserId", "EventAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TurnstileEvents_DeviceUserId_EventAt",
                table: "TurnstileEvents");
        }
    }
}
