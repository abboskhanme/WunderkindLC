using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <summary>
    /// LID FORMALARI moduli — "O'quv bo'limi → Formalar" bo'limining ikkinchi turi (birinchisi —
    /// mavjud DARAJA TESTI). Har bir ijtimoiy tarmoq / reklama kanali uchun alohida ommaviy forma:
    /// o'z havolasi (<c>/forma/{slug}</c>), o'z savollari va o'z MANBASI bilan.
    ///
    /// <para>Mavjud jadvallarga TEGMAYDI — faqat uchta yangi jadval. Tushgan ariza odatdagi
    /// <c>Leads</c> yozuvini yaratadi (yoki telefon bo'yicha mavjudiga biriktiriladi), ya'ni
    /// Lidlar bo'limi o'zgarishsiz ishlayveradi.</para>
    ///
    /// <para>Kurs markazdagi <c>Subjects</c> ga BOG'LANMAGAN: <c>LeadForms.CourseName</c> — erkin
    /// matn, <c>CourseOptions</c> (<c>text[]</c>) — formaning O'ZIDA yozilgan, mijoz tanlaydigan
    /// variantlar. Shu sabab bu yerda hech qanday tashqi kalit yo'q.</para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddLeadForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadFormFields",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FormId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<List<string>>(type: "text[]", nullable: false),
                    Placeholder = table.Column<string>(type: "text", nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadFormFields", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadForms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CourseName = table.Column<string>(type: "text", nullable: false),
                    CourseOptions = table.Column<List<string>>(type: "text[]", nullable: false),
                    Intro = table.Column<string>(type: "text", nullable: false),
                    SuccessText = table.Column<string>(type: "text", nullable: false),
                    ButtonText = table.Column<string>(type: "text", nullable: false),
                    AskAge = table.Column<bool>(type: "boolean", nullable: false),
                    AskCourse = table.Column<bool>(type: "boolean", nullable: false),
                    AskParentPhone = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Views = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    InstagramUrl = table.Column<string>(type: "text", nullable: false),
                    TelegramUrl = table.Column<string>(type: "text", nullable: false),
                    FacebookUrl = table.Column<string>(type: "text", nullable: false),
                    YoutubeUrl = table.Column<string>(type: "text", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadForms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeadFormSubmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FormId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LeadId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsNewLead = table.Column<bool>(type: "boolean", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    ParentPhone = table.Column<string>(type: "text", nullable: false),
                    Age = table.Column<int>(type: "integer", nullable: false),
                    CourseName = table.Column<string>(type: "text", nullable: false),
                    Ref = table.Column<string>(type: "text", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadFormSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadFormFields_FormId_Order",
                table: "LeadFormFields",
                columns: new[] { "FormId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadForms_Slug",
                table: "LeadForms",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadFormSubmissions_FormId_CreatedAt",
                table: "LeadFormSubmissions",
                columns: new[] { "FormId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadFormSubmissions_LeadId",
                table: "LeadFormSubmissions",
                column: "LeadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadFormFields");

            migrationBuilder.DropTable(
                name: "LeadForms");

            migrationBuilder.DropTable(
                name: "LeadFormSubmissions");
        }
    }
}
