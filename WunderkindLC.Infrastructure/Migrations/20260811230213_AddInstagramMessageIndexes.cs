using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramMessageIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IgMessageId",
                table: "IgMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CommentId",
                table: "IgMessages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_IgMessages_CommentId",
                table: "IgMessages",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_IgMessages_IgMessageId",
                table: "IgMessages",
                column: "IgMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IgMessages_CommentId",
                table: "IgMessages");

            migrationBuilder.DropIndex(
                name: "IX_IgMessages_IgMessageId",
                table: "IgMessages");

            migrationBuilder.AlterColumn<string>(
                name: "IgMessageId",
                table: "IgMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "CommentId",
                table: "IgMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
