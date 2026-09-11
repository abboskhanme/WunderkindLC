using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WunderkindLC.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstagramAiModel",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InstagramAppId",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "InstagramAutoReplyComments",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "InstagramAutoReplyDm",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InstagramDailyReplyLimit",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                // ⚠️ Entity default'i (200) bilan BIR XIL bo'lishi shart (books.md saboqi):
                // EF avtomatik `0` qo'ygan edi — bu eski bazada "kunlik limit 0" degani, ya'ni
                // modul yoqilsa ham bironta javob ketmasdi.
                defaultValue: 200);

            migrationBuilder.AddColumn<bool>(
                name: "InstagramEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InstagramGreeting",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InstagramLeadSource",
                table: "CenterMeta",
                type: "text",
                nullable: false,
                // Entity default'i bilan BIR XIL — bo'sh qolsa lidlar manbasiz yozilardi.
                defaultValue: "Instagram");

            migrationBuilder.AddColumn<bool>(
                name: "InstagramNotifyTelegram",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                // Entity default'i `true` — qaynoq lid haqida xabar JIM qolib ketmasin.
                // (Bu XAVFSIZLIK bayrog'i emas: tashqi mijozga hech narsa yubormaydi,
                // faqat markazning O'Z Telegram adminlariga xabar beradi.)
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "InstagramPrivateReplyEnabled",
                table: "CenterMeta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InstagramReplyDelaySeconds",
                table: "CenterMeta",
                type: "integer",
                nullable: false,
                // Entity default'i bilan BIR XIL (5 s) — bir zumda kelgan javob spamga o'xshaydi.
                defaultValue: 5);

            migrationBuilder.CreateTable(
                name: "IgAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IgUserId = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    TokenExpiresAt = table.Column<string>(type: "text", nullable: false),
                    TokenRefreshedAt = table.Column<string>(type: "text", nullable: false),
                    WebhookSubscribed = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<string>(type: "text", nullable: false),
                    ConnectedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgAutoRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Keywords = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    ReplyText = table.Column<string>(type: "text", nullable: false),
                    StopAi = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    MatchCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgAutoRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgConversations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IgUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OperatorPausedUntil = table.Column<string>(type: "text", nullable: false),
                    LastInboundAt = table.Column<string>(type: "text", nullable: false),
                    LastOutboundAt = table.Column<string>(type: "text", nullable: false),
                    LastMessageText = table.Column<string>(type: "text", nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    Unread = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsOperator = table.Column<bool>(type: "boolean", nullable: false),
                    NeedsOperatorReason = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    Intent = table.Column<string>(type: "text", nullable: false),
                    LeadScore = table.Column<int>(type: "integer", nullable: false),
                    LeadId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgKnowledges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgKnowledges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    MediaId = table.Column<string>(type: "text", nullable: false),
                    CommentId = table.Column<string>(type: "text", nullable: false),
                    IgMessageId = table.Column<string>(type: "text", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    IsAi = table.Column<bool>(type: "boolean", nullable: false),
                    AiIntent = table.Column<string>(type: "text", nullable: false),
                    AiScore = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgOAuthStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<string>(type: "text", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgOAuthStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<string>(type: "text", nullable: false),
                    ProcessedAt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IgConversations_IgUserId",
                table: "IgConversations",
                column: "IgUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IgMessages_ConversationId",
                table: "IgMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_IgWebhookEvents_EventKey",
                table: "IgWebhookEvents",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgWebhookEvents_Status",
                table: "IgWebhookEvents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IgAccounts");

            migrationBuilder.DropTable(
                name: "IgAutoRules");

            migrationBuilder.DropTable(
                name: "IgConversations");

            migrationBuilder.DropTable(
                name: "IgKnowledges");

            migrationBuilder.DropTable(
                name: "IgMessages");

            migrationBuilder.DropTable(
                name: "IgOAuthStates");

            migrationBuilder.DropTable(
                name: "IgWebhookEvents");

            migrationBuilder.DropColumn(
                name: "InstagramAiModel",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramAppId",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramAutoReplyComments",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramAutoReplyDm",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramDailyReplyLimit",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramGreeting",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramLeadSource",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramNotifyTelegram",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramPrivateReplyEnabled",
                table: "CenterMeta");

            migrationBuilder.DropColumn(
                name: "InstagramReplyDelaySeconds",
                table: "CenterMeta");
        }
    }
}
