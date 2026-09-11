using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// KITOB QAYTARISH (vozvrat) — sotilgan kitobni qaytarib olish uchun maydonlar.
    /// Qaytarish buyurtma HOLATINI o'zgartirmaydi (qisman ham bo'ladi), shuning uchun
    /// alohida <c>ReturnedQty</c> ustuni: hisobotlar SOF qiymat bilan ishlaydi
    /// (<c>Qty − ReturnedQty</c>, <c>Total − UnitPrice × ReturnedQty</c>).
    /// Eski qatorlarga to'ldirish KERAK EMAS — 0 (qaytarilmagan) to'g'ri qiymat.
    /// </summary>
    public partial class AddBookReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "BookOrders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "BookOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "BookOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnedBy",
                table: "BookOrders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReturnedQty",
                table: "BookOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BookOrders_ReturnedAt",
                table: "BookOrders",
                column: "ReturnedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookOrders_ReturnedAt",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedBy",
                table: "BookOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedQty",
                table: "BookOrders");
        }
    }
}
