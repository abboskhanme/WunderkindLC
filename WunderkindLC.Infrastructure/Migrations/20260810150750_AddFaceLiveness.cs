using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceLiveness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ defaultValue'lar QO'LDA to'g'rilangan — EF ularni "0/false/bo'sh" qilib yozadi va
            // `AddFaceLogin` da aynan shu tuzoq bo'lgan edi (chegara 0 bo'lib qolsa modul HAR
            // QANDAY yuzni o'tkazib yuborardi). Bu yerda ham xuddi shunday xavf bor:
            // `LoginFaceRequireLiveness` false bo'lib qolsa — tiriklik tekshiruvi JIMGINA
            // o'chiq qolardi va bosma surat bilan kirish ochiq bo'lardi.

            // Eski urinishlarda attestation ma'lumoti YO'Q edi — bo'sh satr aynan shuni bildiradi
            // ("noma'lum"), chunki `AppAttestation.Code` hech qachon bo'sh qaytarmaydi.
            migrationBuilder.AddColumn<string>(
                name: "AttestReason",
                table: "LoginFaceChecks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Attested",
                table: "LoginFaceChecks",
                type: "text",
                nullable: false,
                defaultValue: "");

            // FALSE — ATAYIN: `PLAY_INTEGRITY_*` kaliti sozlanmaguncha va ilovaning yangi
            // versiyasi tarqalmaguncha hech kim qulflanib qolmasin (natija baribir jurnalga
            // yoziladi). Sozlamalar sahifasidan yoqiladi.
            migrationBuilder.AddColumn<bool>(
                name: "LoginFaceRequireAttestation",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // TRUE — ATAYIN (EF `false` yozgan edi, qo'lda to'g'rilandi). Yuz bilan kirish
            // modulining O'ZI yangi, ya'ni "eski klient" muammosi YO'Q: tiriklik tekshiruvi
            // birinchi kundanoq majburiy bo'lishi kerak. `false` bo'lib qolsa modul ishlayotgandek
            // ko'rinib, aslida bosma surat/ekrandagi rasmni o'tkazib yuborardi.
            migrationBuilder.AddColumn<bool>(
                name: "LoginFaceRequireLiveness",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "FaceChallenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Nonce = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActionsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UsedAt = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceChallenges_Nonce",
                table: "FaceChallenges",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceChallenges_UserId_CreatedAt",
                table: "FaceChallenges",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceChallenges");

            migrationBuilder.DropColumn(
                name: "AttestReason",
                table: "LoginFaceChecks");

            migrationBuilder.DropColumn(
                name: "Attested",
                table: "LoginFaceChecks");

            migrationBuilder.DropColumn(
                name: "LoginFaceRequireAttestation",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LoginFaceRequireLiveness",
                table: "CenterMeta");
        }
    }
}
