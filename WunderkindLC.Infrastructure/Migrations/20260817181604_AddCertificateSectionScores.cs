using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateSectionScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertType",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Listening",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OverallScore",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reading",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResultNote",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Speaking",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Writing",
                table: "LandingCertificates",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertType",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "Listening",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "OverallScore",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "Reading",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "ResultNote",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "Speaking",
                table: "LandingCertificates");

            migrationBuilder.DropColumn(
                name: "Writing",
                table: "LandingCertificates");
        }
    }
}
