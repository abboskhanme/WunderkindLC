using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// KALITLAR BAZADAN OLIB TASHLANDI — endi ular FAQAT <c>.env</c> dan o'qiladi
    /// (<c>AppSecrets</c>). Sabab: baza dump'i/backup fayli (Telegram'ga yuboriladigan JSON ham),
    /// <c>pg_dump</c> nusxalari va SQL kirishi orqali kalitlar sizib chiqar edi; UI'dan kiritilgan
    /// kalit esa deploy/tiklashdan keyin ham bazada qolib ketardi.
    ///
    /// <para>DIQQAT: ustunlar O'CHIRILADI — qiymatlar YO'QOLADI. Ishga tushishda migratsiyadan
    /// OLDIN <c>LegacySecretRescue</c> eski qiymatlarni tayyor <c>.env</c> qatorlari ko'rinishida
    /// logga chiqaradi (nusxa olib serverdagi <c>.env</c> ga joylash uchun).</para>
    /// </summary>
    public partial class RemoveSecretsFromDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureSpeechKey",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "AzureSpeechRegion",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizEmail",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizPassword",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizToken",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "EskizTokenExpiresAt",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "FcmServiceAccountJson",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "GeminiApiKey",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "TelegramBotToken",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "TurnstilePassword",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "TurnstileUsername",
                table: "CenterMeta");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureSpeechKey",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AzureSpeechRegion",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizEmail",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizPassword",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EskizToken",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EskizTokenExpiresAt",
                table: "CenterMeta",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FcmServiceAccountJson",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GeminiApiKey",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TelegramBotToken",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TurnstilePassword",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TurnstileUsername",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
