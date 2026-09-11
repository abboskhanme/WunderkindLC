using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookManualSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardLast4",
                table: "BookOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidTime",
                table: "BookOrders",
                type: "text",
                nullable: true);

            // MAVJUD buyurtmalarning hammasi botdan tushgan — shuning uchun default "bot"
            // (bo'sh satr qolsa "manba noma'lum" degan uchinchi holat paydo bo'lardi).
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "BookOrders",
                type: "text",
                nullable: false,
                defaultValue: "bot");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardLast4",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "PaidTime",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "BookOrders");
        }
    }
}
