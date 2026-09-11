using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramLeadAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ `defaultValue` ATAYIN entity default'i bilan bir xil: bo'sh qoldirilsa MAVJUD
            // markazda maydon bo'sh ko'rinardi (entity default'i faqat YANGI qatorga tegishli —
            // `books.md` §4 dagi saboq). Kod baribir bo'sh qiymatda `MetaLeadBridge.DefaultSource`
            // ga qaytadi, lekin admin sozlamalar sahifasida bo'sh maydonni ko'rmasligi kerak.
            migrationBuilder.AddColumn<string>(
                name: "InstagramAdsLeadSource",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "Instagram reklama");

            migrationBuilder.AddColumn<bool>(
                name: "InstagramLeadAdsEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "IgAdLeads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    LeadgenId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PageId = table.Column<string>(type: "text", nullable: false),
                    FormId = table.Column<string>(type: "text", nullable: false),
                    FormName = table.Column<string>(type: "text", nullable: false),
                    AdId = table.Column<string>(type: "text", nullable: false),
                    AdName = table.Column<string>(type: "text", nullable: false),
                    AdsetId = table.Column<string>(type: "text", nullable: false),
                    CampaignId = table.Column<string>(type: "text", nullable: false),
                    CampaignName = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    RawFieldsJson = table.Column<string>(type: "text", nullable: false),
                    LeadId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsNewLead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAdLeads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgAdPages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    PageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PageName = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    LeadgenSubscribed = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<string>(type: "text", nullable: false),
                    ConnectedBy = table.Column<string>(type: "text", nullable: false),
                    LastLeadAt = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAdPages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IgAdLeads_CreatedTime",
                table: "IgAdLeads",
                column: "CreatedTime");

            migrationBuilder.CreateIndex(
                name: "IX_IgAdLeads_LeadgenId",
                table: "IgAdLeads",
                column: "LeadgenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgAdLeads_LeadId",
                table: "IgAdLeads",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_IgAdPages_PageId",
                table: "IgAdPages",
                column: "PageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IgAdLeads");

            migrationBuilder.DropTable(
                name: "IgAdPages");

            migrationBuilder.DropColumn(
                name: "InstagramAdsLeadSource",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramLeadAdsEnabled",
                table: "CenterMeta");
        }
    }
}
