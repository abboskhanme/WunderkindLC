using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ defaultValue'lar entity'dagi default'lar bilan QO'LDA moslashtirilgan (EF ularni
            // avtomatik 0/"" qilib yozgan edi). Sabab — `AddBookSales` dagi saboq: o'sha yerda
            // entity default'i `true`, migratsiyaniki `false` bo'lib qolgan va "eski o'rnatishda
            // modul o'chiq" degan chalkashlik tug'ilgan. Bu yerda MAVJUD bazadagi qatorlar ham
            // ishlaydigan qiymat oladi: chegara 0.60 (0.0 bo'lsa har qanday yuz "mos" bo'lardi)
            // va saqlanadigan selfilar soni 5 (0 bo'lsa tozalash mantiqi ma'nosiz edi).
            //
            // `LoginFaceEnabled` esa ATAYIN `false`: mavjud o'quvchilarning kirishi deploy bilan
            // birdan buzilmasin — modul Sozlamalardan qo'lda yoqiladi.
            migrationBuilder.AddColumn<bool>(
                name: "LoginFaceEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LoginFaceKeepChecks",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "LoginFaceModelVersion",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "buffalo_s@512");

            migrationBuilder.AddColumn<double>(
                name: "LoginFaceThreshold",
                table: "CenterMeta",
                type: "double precision",
                nullable: false,
                defaultValue: 0.6);

            migrationBuilder.CreateTable(
                name: "LoginFaceChecks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    AppVersion = table.Column<string>(type: "text", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    ModelVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Quality = table.Column<string>(type: "text", nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: true),
                    Dim = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginFaceChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentFaceProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: false),
                    Dim = table.Column<int>(type: "integer", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    SampleUrl = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentFaceProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustedDevices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DeviceName = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginFaceChecks_Status",
                table: "LoginFaceChecks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LoginFaceChecks_StudentId_CreatedAt",
                table: "LoginFaceChecks",
                columns: new[] { "StudentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentFaceProfiles_StudentId",
                table: "StudentFaceProfiles",
                column: "StudentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_UserId_DeviceId",
                table: "TrustedDevices",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginFaceChecks");

            migrationBuilder.DropTable(
                name: "StudentFaceProfiles");

            migrationBuilder.DropTable(
                name: "TrustedDevices");

            migrationBuilder.DropColumn(
                name: "LoginFaceEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LoginFaceKeepChecks",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LoginFaceModelVersion",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "LoginFaceThreshold",
                table: "CenterMeta");
        }
    }
}
