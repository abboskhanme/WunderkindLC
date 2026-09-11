using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⚠️ QO'LDA TUZATILGAN DEFAULT'LAR. EF entity'dagi initsializatorni MAVJUD qatorlarga
            // qo'llamaydi (u faqat YANGI qatorga tegishli) — `books.md` §4 dagi `BookSalesEnabled`
            // sabog'i. Agar `InstagramAdsSyncHour`/`BackfillDays` 0 bo'lib qolsa, ishlab turgan
            // markazda birinchi backfill UMUMAN bajarilmasdi; CAPI bosqich nomlari bo'sh qolsa esa
            // Events Manager'dagi bosqich bilan hech qachon mos kelmasdi.
            // Bayroqlar (`*Enabled`) esa ATAYIN `false` — modul o'chiq holda keladi.
            migrationBuilder.AddColumn<string>(
                name: "AdCampaignId",
                table: "IgMessages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdId",
                table: "IgMessages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdCampaignId",
                table: "IgConversations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdId",
                table: "IgConversations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "InstagramAdsBackfillDays",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<bool>(
                name: "InstagramAdsStatsEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InstagramAdsSyncHour",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "InstagramCapiDatasetId",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "InstagramCapiEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InstagramCapiStageQualified",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "Sifatli lid");

            migrationBuilder.AddColumn<string>(
                name: "InstagramCapiStageWon",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "To'lov qildi");

            migrationBuilder.AddColumn<string>(
                name: "InstagramCapiToken",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "InstagramPublishEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "IgAdAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AdAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    CurrencyOffset = table.Column<int>(type: "integer", nullable: false),
                    TimezoneName = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<string>(type: "text", nullable: false),
                    ConnectedBy = table.Column<string>(type: "text", nullable: false),
                    LastSyncAt = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAdAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgAdEntities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AdAccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EffectiveStatus = table.Column<string>(type: "text", nullable: false),
                    Objective = table.Column<string>(type: "text", nullable: false),
                    DailyBudgetMinor = table.Column<long>(type: "bigint", nullable: false),
                    LifetimeBudgetMinor = table.Column<long>(type: "bigint", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    StopTime = table.Column<string>(type: "text", nullable: false),
                    CreativeStoryId = table.Column<string>(type: "text", nullable: false),
                    SyncedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAdEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgAdInsights",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AdAccountId = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StatDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    LinkClicks = table.Column<long>(type: "bigint", nullable: false),
                    SpendMinor = table.Column<long>(type: "bigint", nullable: false),
                    LeadsOnsite = table.Column<int>(type: "integer", nullable: false),
                    LeadsPixel = table.Column<int>(type: "integer", nullable: false),
                    MsgStarted = table.Column<int>(type: "integer", nullable: false),
                    ActionsJson = table.Column<string>(type: "text", nullable: false),
                    AttributionSetting = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAdInsights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgCapiEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    LeadId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LeadgenId = table.Column<string>(type: "text", nullable: false),
                    EventName = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventTime = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgCapiEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgScheduledPosts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    PostType = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    MediaJson = table.Column<string>(type: "text", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContainerId = table.Column<string>(type: "text", nullable: false),
                    ContainerStatus = table.Column<string>(type: "text", nullable: false),
                    MediaId = table.Column<string>(type: "text", nullable: false),
                    Permalink = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgScheduledPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IgAdAccounts_AdAccountId",
                table: "IgAdAccounts",
                column: "AdAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgAdEntities_AdAccountId_Level",
                table: "IgAdEntities",
                columns: new[] { "AdAccountId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_IgAdEntities_ExternalId",
                table: "IgAdEntities",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgAdInsights_Level_ExternalId_StatDate_Platform",
                table: "IgAdInsights",
                columns: new[] { "Level", "ExternalId", "StatDate", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgAdInsights_StatDate",
                table: "IgAdInsights",
                column: "StatDate");

            migrationBuilder.CreateIndex(
                name: "IX_IgCapiEvents_EventId",
                table: "IgCapiEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgCapiEvents_LeadId",
                table: "IgCapiEvents",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_IgCapiEvents_Status",
                table: "IgCapiEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IgScheduledPosts_ScheduledAt",
                table: "IgScheduledPosts",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_IgScheduledPosts_Status",
                table: "IgScheduledPosts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IgAdAccounts");

            migrationBuilder.DropTable(
                name: "IgAdEntities");

            migrationBuilder.DropTable(
                name: "IgAdInsights");

            migrationBuilder.DropTable(
                name: "IgCapiEvents");

            migrationBuilder.DropTable(
                name: "IgScheduledPosts");

            migrationBuilder.DropColumn(
                name: "AdCampaignId",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "AdId",
                table: "IgMessages");

            migrationBuilder.DropColumn(
                name: "AdCampaignId",
                table: "IgConversations");

            migrationBuilder.DropColumn(
                name: "AdId",
                table: "IgConversations");

            migrationBuilder.DropColumn(
                name: "InstagramAdsBackfillDays",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramAdsStatsEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramAdsSyncHour",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramCapiDatasetId",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramCapiEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramCapiStageQualified",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramCapiStageWon",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramCapiToken",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramPublishEnabled",
                table: "CenterMeta");
        }
    }
}
