using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalPaymentGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "JournalHideUnpaidAfterDay",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "JournalHideUnpaidPrevMonth",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "JournalUnpaidCutoffDay",
                table: "CenterMeta",
                // Eski o'rnatishdagi mavjud qator ham mazmunli qiymat olsin (0 emas) — entity defaulti bilan bir xil.
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JournalHideUnpaidAfterDay",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "JournalHideUnpaidPrevMonth",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "JournalUnpaidCutoffDay",
                table: "CenterMeta");
        }
    }
}
