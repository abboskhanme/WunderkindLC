using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingRagAndQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiSuggestedIntent",
                table: "IgMessages",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiSuggestedText",
                table: "IgMessages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WasEdited",
                table: "IgMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedAt",
                table: "IgKnowledges",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddedHash",
                table: "IgKnowledges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingJson",
                table: "IgKnowledges",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "IgKnowledges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_IgMessages_CreatedAt",
                table: "IgMessages",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IgMessages_CreatedAt",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "AiSuggestedIntent",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "AiSuggestedText",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "WasEdited",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "IgKnowledges");

            migrationBuilder.DropColumn(
                name: "EmbeddedHash",
                table: "IgKnowledges");

            migrationBuilder.DropColumn(
                name: "EmbeddingJson",
                table: "IgKnowledges");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "IgKnowledges");
        }
    }
}
