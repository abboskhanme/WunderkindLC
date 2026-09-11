using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CertificateTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CourseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HtmlTemplate = table.Column<string>(type: "text", nullable: false),
                    ValidityDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentCertificates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CourseId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DownloadCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCertificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentCertificates_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentCertificates_Subjects_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CertificateVerifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StudentCertificateId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    VerifiedFrom = table.Column<string>(type: "text", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    HashMatched = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateVerifications_StudentCertificates_StudentCertifi~",
                        column: x => x.StudentCertificateId,
                        principalTable: "StudentCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateTemplates_CourseId",
                table: "CertificateTemplates",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateVerifications_StudentCertificateId",
                table: "CertificateVerifications",
                column: "StudentCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCertificates_CourseId",
                table: "StudentCertificates",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCertificates_Status",
                table: "StudentCertificates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StudentCertificates_StudentId_CourseId",
                table: "StudentCertificates",
                columns: new[] { "StudentId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentCertificates_StudentId_CourseId_IssuedAt",
                table: "StudentCertificates",
                columns: new[] { "StudentId", "CourseId", "IssuedAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CertificateVerifications");
            migrationBuilder.DropTable(name: "StudentCertificates");
            migrationBuilder.DropTable(name: "CertificateTemplates");
        }
    }
}
