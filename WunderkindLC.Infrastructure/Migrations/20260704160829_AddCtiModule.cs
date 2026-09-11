using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCtiModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CtiAgents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FcmToken = table.Column<string>(type: "text", nullable: false),
                    IsOnline = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtiAgents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CtiCallRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    RemoteNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContactName = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DurationSec = table.Column<int>(type: "integer", nullable: false),
                    AudioPath = table.Column<string>(type: "text", nullable: false),
                    AudioUploaded = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtiCallRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CtiCommandLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtiCommandLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CtiCallEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CallId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtiCallEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CtiCallEvents_CtiCallRecords_CallId",
                        column: x => x.CallId,
                        principalTable: "CtiCallRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CtiAgents_Login",
                table: "CtiAgents",
                column: "Login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CtiCallEvents_CallId",
                table: "CtiCallEvents",
                column: "CallId");

            migrationBuilder.CreateIndex(
                name: "IX_CtiCallRecords_AgentId",
                table: "CtiCallRecords",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CtiCallRecords_RemoteNumber",
                table: "CtiCallRecords",
                column: "RemoteNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CtiCallRecords_StartedAt",
                table: "CtiCallRecords",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CtiCommandLogs_AgentId",
                table: "CtiCommandLogs",
                column: "AgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CtiAgents");

            migrationBuilder.DropTable(
                name: "CtiCallEvents");

            migrationBuilder.DropTable(
                name: "CtiCommandLogs");

            migrationBuilder.DropTable(
                name: "CtiCallRecords");
        }
    }
}
