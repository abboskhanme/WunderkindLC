using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CrmWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovalRequired",
                table: "WorkTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "WorkTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedById",
                table: "WorkTasks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReturnedAt",
                table: "WorkTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnCount",
                table: "WorkTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HomeLayout",
                table: "UserSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Floor",
                table: "Rooms",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalRequired",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "LastReturnedAt",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "ReturnCount",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "HomeLayout",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "Floor",
                table: "Rooms");
        }
    }
}
