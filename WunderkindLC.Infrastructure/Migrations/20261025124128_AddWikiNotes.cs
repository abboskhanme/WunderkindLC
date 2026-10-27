using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWikiNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WikiPages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    ContentText = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    LockedBy = table.Column<string>(type: "text", nullable: false),
                    LockedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WikiSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ManagersSeeTeamNotes = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerVisiblePositionIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WikiPages_OwnerUserId_ParentId_Order",
                table: "WikiPages",
                columns: new[] { "OwnerUserId", "ParentId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WikiPages");

            migrationBuilder.DropTable(
                name: "WikiSettings");
        }
    }
}
