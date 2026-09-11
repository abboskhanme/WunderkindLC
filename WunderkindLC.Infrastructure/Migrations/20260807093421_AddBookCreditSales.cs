using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookCreditSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "BookOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "BookOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "BookOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidBy",
                table: "BookOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SettledMethod",
                table: "BookOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookOrders_PaymentMethod_PaidAt",
                table: "BookOrders",
                columns: new[] { "PaymentMethod", "PaidAt" });

            // ESKI QATORLARNI TO'LDIRISH: nasiya modulidan oldingi sotuvlar naqd yoki karta
            // bo'lgan, ya'ni pul tasdiqlangan paytda olingan. `PaidAt` ni o'sha paytga qo'yamiz —
            // shunda "qachon pul tushdi" hisoboti eski sotuvlarni ham ko'radi.
            // (Hisobotlar bunga TAYANMAYDI — `BookSalesService.IsPaid` nasiya bo'lmagan
            //  tasdiqlangan buyurtmani `PaidAt` bo'sh bo'lsa ham to'langan deb sanaydi.)
            migrationBuilder.Sql(
                """
                UPDATE "BookOrders" SET "PaidAt" = "DecidedAt"
                WHERE "Status" = 'approved' AND "DecidedAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookOrders_PaymentMethod_PaidAt",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "PaidBy",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "SettledMethod",
                table: "BookOrders");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "BookOrders",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
