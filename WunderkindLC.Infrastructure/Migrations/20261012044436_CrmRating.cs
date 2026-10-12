using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RatingTestWeight",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingTopBottomCount",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RatingTopBottomScope",
                table: "CenterMeta",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            // Owner defaults for the existing settings row (docs/migrations/Phase4.sql). Only here, in the
            // migration that ADDS the columns — later it would overwrite an admin's saved weight.
            migrationBuilder.Sql(@"UPDATE ""CenterMeta"" SET ""RatingTestWeight"" = 60, ""RatingTopBottomCount"" = 5, ""RatingTopBottomScope"" = 'group';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatingTestWeight",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "RatingTopBottomCount",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "RatingTopBottomScope",
                table: "CenterMeta");
        }
    }
}
