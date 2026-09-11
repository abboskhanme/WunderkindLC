using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLmsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LmsMaterials");

            migrationBuilder.DropTable(
                name: "LmsProgresses");

            migrationBuilder.DropTable(
                name: "LmsTopics");

            migrationBuilder.DropTable(
                name: "LmsModules");

            migrationBuilder.DropTable(
                name: "LmsSubjects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LmsSubjects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    ClassId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    UnlockMode = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsSubjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LmsModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsModules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsModules_LmsSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "LmsSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LmsTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TextContent = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    VideoUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsTopics_LmsModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "LmsModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LmsMaterials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<string>(type: "character varying(200)", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsMaterials_LmsTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "LmsTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LmsProgresses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TopicId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StudentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsProgresses_LmsTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "LmsTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LmsMaterials_TopicId",
                table: "LmsMaterials",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsModules_SubjectId_Order",
                table: "LmsModules",
                columns: new[] { "SubjectId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_StudentId",
                table: "LmsProgresses",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_StudentId_TopicId",
                table: "LmsProgresses",
                columns: new[] { "StudentId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_TopicId",
                table: "LmsProgresses",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsSubjects_ClassId",
                table: "LmsSubjects",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsTopics_ModuleId_Order",
                table: "LmsTopics",
                columns: new[] { "ModuleId", "Order" });
        }
    }
}
