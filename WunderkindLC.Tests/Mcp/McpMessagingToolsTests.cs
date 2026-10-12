using System.Text.Json;
using ModelContextProtocol;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Mcp.Tools;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Mcp;

/// <summary>
/// MCP — CRM upgrade Phase 2 (U2m): `message_campaign_report`, `message_deliveries`, `auto_message_stats`,
/// `student_achievements` + kengaytirilgan `messages_history`, `auto_message_rules`, `students_search`,
/// `student_profile` (Telegram holati). Panel bilan bir xil sanoqlar, filtrlar, ruxsat va — ENG MUHIMI — Telegram
/// chat id, fayl manzili va Telegram file_id natijaga HECH QACHON chiqmaydi.
/// </summary>
public class McpMessagingToolsTests
{
    private static string D(int offset) => McpToolContext.Iso(McpToolContext.Today.AddDays(offset));

    private static DateTime At(int dayOffset, int hour) =>
        McpToolContext.Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0));

    private static List<string?> Ids(JsonElement json, string prop = "id") =>
        json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(prop).GetString()).ToList();

    // Chat ids — none of these numbers may ever appear in a tool result.
    private const long ChatFather = 700111222;
    private const long ChatMother = 700333444;
    private const long ChatSelf = 700555666;
    private const long ChatBobur = 700777888;

    private const string Salom = "<b>Salom</b> ota-onalar";

    private static void Seed(McpTestHost host)
    {
        var db = host.Rw;
        db.AddRange(
            new Subject { Id = "c1", Name = "Ingliz tili" },
            new Teacher { Id = "t1", FullName = "Dilnoza Karimova" },
            new Group { Id = "g1", Name = "English A1", CourseId = "c1", TeacherId = "t1" });
        db.Students.AddRange(
            new Student { Id = "s1", FullName = "Ali Valiyev", FatherPhone = "+998901112233" },
            new Student { Id = "s2", FullName = "Bobur Aliyev" },
            new Student { Id = "s3", FullName = "Charos Arxivda", IsArchived = true },
            new Student { Id = "s4", FullName = "Davron Bloklagan" });
        db.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01" },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "active", JoinedAt = "2026-09-01" });
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = "s1", ChatId = ChatFather, Relation = "father", Phone = "998901112233", ParentName = "Vali" },
            new TelegramRegistration { StudentId = "s1", ChatId = ChatMother, Relation = "mother", BlockedAt = At(-2, 9) },
            new TelegramRegistration { StudentId = "s1", ChatId = ChatSelf, Relation = "student" },
            new TelegramRegistration { StudentId = "s2", ChatId = ChatBobur, Relation = "" },   // eski — ota-ona deb o'qiladi
            new TelegramRegistration { StudentId = "s4", ChatId = 700999000, Relation = "father", BlockedAt = At(-1, 9) });

        db.MessageAttachments.Add(new MessageAttachment
        {
            Id = "att1", Kind = "photo", FileName = "elon.jpg", FileUrl = "/uploads/messaging/maxfiy-fayl.jpg",
            ContentType = "image/jpeg", SizeBytes = 1000, TelegramFileId = "MAXFIY-FILE-ID",
        });
        db.Broadcasts.AddRange(
            new Broadcast
            {
                Id = "bc1", Kind = "broadcast", Status = "done", ClassName = "Barcha ota-onalar", Text = "<b>Salom</b> ota-onalar",
                SenderName = "Nodira Menejer", CreatedAt = At(-1, 10), AckRequired = true, AttachmentId = "att1",
                AudienceJson = "{\"kind\":\"active\",\"groupIds\":[\"g1\"],\"relations\":[\"father\",\"mother\",\"parent\",\"other\"]}",
                RecipientCount = 4, SentCount = 0,
            },
            new Broadcast
            {
                Id = "bc2", ClassName = "English A1", Text = "Eski e'lon", SenderName = "Olim Operator", CreatedAt = At(-20, 10),
                RecipientCount = 10, SentCount = 9, Kind = "", Status = "",
            },
            new Broadcast
            {
                Id = "bc3", Kind = "debt", Status = "sending", ClassName = "Qarzdorlar", Text = "", SenderName = "Nodira Menejer",
                CreatedAt = At(0, 9),
            });

        MessageDelivery Del(string id, string? bc, string source, string trigger, string? student, string name, long chat,
            string relation, string status, DateTime created, DateTime? ack = null, string error = "", string? teacher = null,
            string? text = null) =>
            new()
            {
                Id = id, DedupeKey = id, BroadcastId = bc, Source = source, Trigger = trigger, StudentId = student,
                TeacherId = teacher, RecipientName = name, ChatId = chat, Relation = relation, Status = status,
                Text = text ?? "<b>" + trigger + "</b> matn " + id, CreatedAt = created, SentAt = status == "sent" ? created.AddMinutes(1) : null,
                AckAt = ack, AckRequired = ack is not null, Error = error, ExpiresAt = created.AddHours(12),
            };
        db.MessageDeliveries.AddRange(
            Del("d1", "bc1", "campaign", "broadcast", "s1", "Ali Valiyev", ChatFather, "father", "sent", At(-1, 10), ack: At(-1, 11), text: Salom),
            Del("d2", "bc1", "campaign", "broadcast", "s1", "Ali Valiyev", ChatMother, "mother", "blocked", At(-1, 10), text: Salom),
            Del("d3", "bc1", "campaign", "broadcast", "s2", "Bobur Aliyev", ChatBobur, "", "failed", At(-1, 10), error: "Bad Request: xato", text: Salom),
            Del("d4", "bc1", "campaign", "broadcast", "s2", "Bobur Aliyev", ChatBobur + 1, "", "pending", At(-1, 10), text: Salom),
            Del("d5", null, "auto", "attendance_absent", "s1", "Ali Valiyev", ChatFather, "father", "sent", At(0, 15), ack: At(0, 16)),
            Del("d6", null, "auto", "birthday", "s1", "Ali Valiyev", ChatFather, "father", "sent", At(-10, 9)),
            Del("d7", null, "auto", "lesson_attendance", null, "Dilnoza Karimova", 700123123, "", "sent", At(0, 8), teacher: "t1"),
            Del("d8", null, "auto", "birthday", "s1", "Ali Valiyev", ChatFather, "father", "sent", At(-60, 9)),
            Del("d9", null, "auto", "attendance_absent", "s2", "Bobur Aliyev", ChatBobur, "", "expired", At(0, 15)));

        db.StudentAchievements.AddRange(
            new StudentAchievement
            {
                Id = "a1", StudentId = "s1", Kind = "ielts", Title = "IELTS", Result = "7.5", Date = D(-10), Note = "Birinchi urinish",
                AttachmentId = "att2", CongratulatedAt = At(-9, 10), CongratulationCount = 1, CreatedAt = At(-9, 9), CreatedByName = "Nodira Menejer",
            },
            new StudentAchievement { Id = "a2", StudentId = "s2", Kind = "olympiad", Title = "Matematika olimpiadasi", Result = "2-o'rin", Date = D(-40), CreatedAt = At(-39, 9) },
            new StudentAchievement { Id = "a3", StudentId = "s3", Kind = "cefr", Title = "CEFR", Result = "B2", Date = D(-5), CreatedAt = At(-4, 9) });

        db.AutoMessageRules.AddRange(
            new AutoMessageRule
            {
                Id = "am1", Trigger = "payment_debt", Name = "Qarz eslatmasi", Enabled = true, SendTelegram = true, Audience = "parents",
                Template = "{fish}: {qarzdorlik}", ScheduleType = "monthly", ScheduleDayOfMonth = 10, ScheduleTime = "10:30",
                AckButton = true, AttachmentId = "att1", CreatedAt = At(-30, 9),
            },
            new AutoMessageRule { Id = "am2", Trigger = "attendance_absent", Name = "Kelmadi", Enabled = true, SendTelegram = true, Template = "{ism}", CreatedAt = At(-20, 9) });
        db.SaveChanges();
    }

    private static void AssertNoSecrets(string raw)
    {
        foreach (var chat in new[] { ChatFather, ChatMother, ChatSelf, ChatBobur, ChatBobur + 1, 700999000L, 700123123L })
            Assert.DoesNotContain(chat.ToString(), raw);
        Assert.DoesNotContain("/uploads/", raw);
        Assert.DoesNotContain("MAXFIY-FILE-ID", raw);
        Assert.DoesNotContain("chatId", raw, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================== message_campaign_report

    [Fact]
    public async Task Kampaniya_hisoboti_panel_sanoqlari_qarindoshlik_va_oquvchi_boyicha()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<MessagingTools>(McpTestHost.Staff("messages.broadcast")).MessageCampaignReport("bc1");
        var json = McpTestHost.Parse(raw);

        var c = json.GetProperty("campaign");
        Assert.Equal("broadcast", c.GetProperty("kind").GetString());
        Assert.Equal("E'lon", c.GetProperty("kindLabel").GetString());
        Assert.Equal("Salom ota-onalar", c.GetProperty("text").GetString());   // yuborilgan matn — oddiy matn ko'rinishida
        Assert.True(c.GetProperty("ackRequired").GetBoolean());
        Assert.Equal("photo", c.GetProperty("attachment").GetProperty("kind").GetString());
        Assert.Equal("elon.jpg", c.GetProperty("attachment").GetProperty("fileName").GetString());
        Assert.Equal("active", c.GetProperty("audience").GetProperty("kind").GetString());
        Assert.Equal(1, c.GetProperty("audience").GetProperty("groups").GetInt32());

        var counts = json.GetProperty("counts");
        Assert.Equal(4, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("sent").GetInt32());
        Assert.Equal(1, counts.GetProperty("failed").GetInt32());
        Assert.Equal(1, counts.GetProperty("blocked").GetInt32());
        Assert.Equal(1, counts.GetProperty("pending").GetInt32());
        Assert.Equal(1, counts.GetProperty("acked").GetInt32());

        var rel = json.GetProperty("byRelation").EnumerateArray().ToList();
        Assert.Equal(new[] { "father", "mother", "parent" }, rel.Select(r => r.GetProperty("relation").GetString()));
        Assert.Equal(2, rel[2].GetProperty("total").GetInt32());   // bo'sh qarindoshlik = ota-ona
        Assert.Equal(1, rel[0].GetProperty("acked").GetInt32());

        Assert.Equal(new[] { "s1", "s2" }, Ids(json, "studentId"));
        var ali = json.GetProperty("items")[0];
        Assert.Equal(new[] { "English A1" }, ali.GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        var chats = ali.GetProperty("chats").EnumerateArray().ToList();
        Assert.Equal(new[] { "father", "mother" }, chats.Select(x => x.GetProperty("relation").GetString()));
        Assert.Equal("Otasi", chats[0].GetProperty("relationLabel").GetString());
        Assert.NotEqual(JsonValueKind.Null, chats[0].GetProperty("ackAt").ValueKind);
        Assert.Equal("Bloklagan", chats[1].GetProperty("statusLabel").GetString());
        Assert.Contains(json.GetProperty("items")[1].GetProperty("chats").EnumerateArray(),
            x => x.GetProperty("error").ValueKind == JsonValueKind.String && x.GetProperty("error").GetString() == "Bad Request: xato");

        AssertNoSecrets(raw);
        host.AssertReadsOnlyDeclared(typeof(MessagingTools));
        host.AssertNoSecretColumns();
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"ChatId\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("acked", new[] { "s1" })]
    [InlineData("failed", new[] { "s2" })]
    [InlineData("blocked", new[] { "s1" })]
    [InlineData("pending", new[] { "s2" })]
    public async Task Kampaniya_hisoboti_holat_filtri(string status, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .MessageCampaignReport("bc1", status));

        Assert.Equal(expected, Ids(json, "studentId"));
        Assert.Equal(4, json.GetProperty("counts").GetProperty("total").GetInt32());   // jamlanma filtrsiz
    }

    [Fact]
    public async Task Kampaniya_hisoboti_eski_elon_topilmagan_va_xatolar()
    {
        using var host = new McpTestHost();
        Seed(host);
        MessagingTools Tool() => host.Tool<MessagingTools>(McpTestHost.Admin());

        var legacy = McpTestHost.Parse(await Tool().MessageCampaignReport("bc2"));
        Assert.Equal(10, legacy.GetProperty("counts").GetProperty("total").GetInt32());   // jurnalsiz — saqlangan sanoq
        Assert.Equal(9, legacy.GetProperty("counts").GetProperty("sent").GetInt32());
        Assert.Equal("broadcast", legacy.GetProperty("campaign").GetProperty("kind").GetString());

        var missing = McpTestHost.Parse(await Tool().MessageCampaignReport("yoq"));
        Assert.Equal("Kampaniya topilmadi.", missing.GetProperty("message").GetString());
        await Assert.ThrowsAsync<McpException>(() => Tool().MessageCampaignReport("bc1", "read"));
        await Assert.ThrowsAsync<McpException>(() => Tool().MessageCampaignReport(" "));
    }

    // ================================================================== messages_history (kengaytirilgan)

    [Fact]
    public async Task Elonlar_tarixi_tur_holat_va_jurnal_sanoqlari_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("messages.broadcast")).MessagesHistory();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "bc3", "bc1", "bc2" }, Ids(json));
        var bc1 = json.GetProperty("items")[1];
        Assert.Equal("broadcast", bc1.GetProperty("kind").GetString());
        Assert.Equal("done", bc1.GetProperty("status").GetString());
        Assert.True(bc1.GetProperty("hasAttachment").GetBoolean());
        Assert.True(bc1.GetProperty("ackRequired").GetBoolean());
        var counts = bc1.GetProperty("counts");
        Assert.Equal(4, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("acked").GetInt32());
        Assert.Equal(1, counts.GetProperty("blocked").GetInt32());
        Assert.Equal(4, bc1.GetProperty("recipients").GetInt32());   // eski maydon = jurnal bo'yicha jami
        Assert.Equal(1, bc1.GetProperty("sent").GetInt32());

        // Eski e'lon: bo'sh tur/holat → broadcast/done, sanoq saqlangandan.
        var bc2 = json.GetProperty("items")[2];
        Assert.Equal("broadcast", bc2.GetProperty("kind").GetString());
        Assert.Equal("done", bc2.GetProperty("status").GetString());
        Assert.Equal(10, bc2.GetProperty("counts").GetProperty("total").GetInt32());
        Assert.Equal(1, bc2.GetProperty("counts").GetProperty("failed").GetInt32());

        var bc3 = json.GetProperty("items")[0];
        Assert.Equal("debt", bc3.GetProperty("kind").GetString());
        Assert.Equal("Qarz eslatmasi", bc3.GetProperty("kindLabel").GetString());
        Assert.Equal("sending", bc3.GetProperty("status").GetString());

        AssertNoSecrets(raw);
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"ChatId\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("broadcast", null, new[] { "bc1", "bc2" })]   // eski (bo'sh tur) ham e'lon
    [InlineData("debt", null, new[] { "bc3" })]
    [InlineData(null, "done", new[] { "bc1", "bc2" })]        // eski (bo'sh holat) ham tugagan
    [InlineData(null, "sending", new[] { "bc3" })]
    public async Task Elonlar_tarixi_tur_va_holat_filtrlari(string? kind, string? status, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<OperationsTools>(McpTestHost.Admin())
            .MessagesHistory(kind: kind, status: status));

        Assert.Equal(expected, Ids(json));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.Admin()).MessagesHistory(kind: "sms"));
        await Assert.ThrowsAsync<McpException>(() => host.Tool<OperationsTools>(McpTestHost.Admin()).MessagesHistory(status: "sent"));
    }

    // ================================================================== auto_message_rules (kengaytirilgan)

    [Fact]
    public async Task Avto_qoidalar_tugma_fayl_va_jadval_rejimi_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<OperationsTools>(McpTestHost.Staff("messages.broadcast")).AutoMessageRules();
        var json = McpTestHost.Parse(raw);
        var am1 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "am1");
        var am2 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "am2");

        Assert.True(am1.GetProperty("ackButton").GetBoolean());
        Assert.True(am1.GetProperty("hasAttachment").GetBoolean());
        Assert.Equal("monthly_day_time", am1.GetProperty("schedule").GetProperty("mode").GetString());
        Assert.Equal(10, am1.GetProperty("schedule").GetProperty("dayOfMonth").GetInt32());
        Assert.False(am2.GetProperty("ackButton").GetBoolean());
        Assert.False(am2.GetProperty("hasAttachment").GetBoolean());
        Assert.Equal("none", am2.GetProperty("schedule").GetProperty("mode").GetString());
        AssertNoSecrets(raw);
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"FileUrl\"", StringComparison.Ordinal));
        host.AssertReadsOnlyDeclared(typeof(OperationsTools));
    }

    // ================================================================== message_deliveries

    [Fact]
    public async Task Yetkazish_jurnali_sukut_30_kun_xodim_qatorlarisiz()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<MessagingTools>(McpTestHost.Staff("messages.broadcast")).MessageDeliveries();
        var json = McpTestHost.Parse(raw);

        // 30 kun ichida: d9, d5 (bugun), d1..d4 (kecha), d6 (10 kun oldin). Eski d8 (60 kun) va o'qituvchi qatori d7 yo'q.
        Assert.Equal(7, json.GetProperty("total").GetInt32());
        var ids = Ids(json);
        Assert.Equal(new[] { "d5", "d9" }, ids.Take(2).OrderBy(x => x));
        Assert.Equal("d6", ids[^1]);
        Assert.DoesNotContain("d7", ids);
        Assert.DoesNotContain("d8", ids);

        var d5 = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "d5");
        Assert.Equal("auto", d5.GetProperty("source").GetString());
        Assert.Equal("attendance_absent", d5.GetProperty("trigger").GetString());
        Assert.Equal("s1", d5.GetProperty("student").GetProperty("id").GetString());
        Assert.Equal("Otasi", d5.GetProperty("relationLabel").GetString());
        Assert.Equal("attendance_absent matn d5", d5.GetProperty("text").GetString());   // HTML — oddiy matn
        Assert.NotEqual(JsonValueKind.Null, d5.GetProperty("ackAt").ValueKind);
        var sum = json.GetProperty("summary");
        Assert.Equal(2, sum.GetProperty("acked").GetInt32());
        Assert.Equal(3, sum.GetProperty("byStatus").EnumerateArray().Single(s => s.GetProperty("status").GetString() == "sent").GetProperty("count").GetInt32());

        AssertNoSecrets(raw);
        host.AssertReadsOnlyDeclared(typeof(MessagingTools));
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"ChatId\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("s2", null, null, null, null, null, new[] { "d9", "d3", "d4" })]
    [InlineData(null, "bc1", null, null, null, null, new[] { "d1", "d2", "d3", "d4" })]
    [InlineData(null, null, "birthday", null, null, null, new[] { "d6" })]
    [InlineData(null, null, null, "auto", null, null, new[] { "d5", "d9", "d6" })]
    [InlineData(null, null, null, null, "expired", null, new[] { "d9" })]
    [InlineData(null, null, null, null, null, true, new[] { "d5", "d1" })]
    public async Task Yetkazish_jurnali_filtrlari(
        string? studentId, string? campaignId, string? trigger, string? source, string? status, bool? acked, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .MessageDeliveries(studentId, campaignId, trigger, source, status, acked));

        Assert.Equal(expected.OrderBy(x => x), Ids(json).OrderBy(x => x));
        Assert.Equal(expected.Length, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Yetkazish_jurnali_sana_oraligi_va_ruxsat()
    {
        using var host = new McpTestHost();
        Seed(host);

        var old = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .MessageDeliveries(from: D(-91), to: D(-50)));
        Assert.Equal(new[] { "d8" }, Ids(old));
        await Assert.ThrowsAsync<McpException>(() => host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .MessageDeliveries(from: D(-120), to: D(0)));   // 92 kundan uzun
        await Assert.ThrowsAsync<McpException>(() => host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .MessageDeliveries(status: "read"));

        // Bitta o'quvchining jurnali — profil qismi: «O'quvchilar» ruxsati yetadi; butun jurnal — yo'q.
        var own = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.Staff("students.list")).MessageDeliveries("s1"));
        Assert.Equal(4, own.GetProperty("total").GetInt32());   // d1, d2, d5, d6

        using var denied = new McpTestHost();
        Seed(denied);
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            denied.Tool<MessagingTools>(McpTestHost.Staff("students.list")).MessageDeliveries());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            denied.Tool<MessagingTools>(McpTestHost.Staff("contacts")).MessageDeliveries("s1"));
        Assert.Empty(denied.Sql);
    }

    // ================================================================== auto_message_stats

    [Fact]
    public async Task Avto_xabarlar_statistikasi_kun_va_hodisa_boyicha()
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.Staff("messages.broadcast"))
            .AutoMessageStats(D(-10), D(0)));

        var byTrigger = json.GetProperty("byTrigger").EnumerateArray().ToList();
        var absent = byTrigger.Single(x => x.GetProperty("trigger").GetString() == "attendance_absent");
        Assert.Equal(2, absent.GetProperty("total").GetInt32());
        Assert.Equal(1, absent.GetProperty("sent").GetInt32());
        Assert.Equal(1, absent.GetProperty("failed").GetInt32());   // muddati o'tgan
        Assert.Equal(1, absent.GetProperty("acked").GetInt32());
        // O'qituvchiga ishchi eslatma ham avto-xabar jurnalida (panel bilan bir xil).
        Assert.Contains(byTrigger, x => x.GetProperty("trigger").GetString() == "birthday");
        Assert.Equal(4, json.GetProperty("totals").GetProperty("total").GetInt32());   // d5, d9, d7 (bugun) + d6 (10 kun)
        Assert.All(json.GetProperty("days").EnumerateArray(), d =>
            Assert.True(string.CompareOrdinal(d.GetProperty("date").GetString(), D(-10)) >= 0));

        var only = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.SuperAdmin())
            .AutoMessageStats(D(-10), D(0), "birthday"));
        Assert.Single(only.GetProperty("byTrigger").EnumerateArray());
        host.AssertReadsOnlyDeclared(typeof(MessagingTools));

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<MessagingTools>(McpTestHost.Staff("students.list")).AutoMessageStats());
    }

    // ================================================================== student_achievements

    [Fact]
    public async Task Yutuqlar_royxati_yangisi_tepada_turlar_jamlanmasi_bilan()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<MessagingTools>(McpTestHost.Staff("students.list")).StudentAchievements();
        var json = McpTestHost.Parse(raw);

        Assert.Equal(new[] { "a3", "a1", "a2" }, Ids(json));
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        var a1 = json.GetProperty("items")[1];
        Assert.Equal("IELTS", a1.GetProperty("kindLabel").GetString());
        Assert.Equal("7.5", a1.GetProperty("result").GetString());
        Assert.True(a1.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal(1, a1.GetProperty("congratulationCount").GetInt32());
        Assert.Equal("Ali Valiyev", a1.GetProperty("student").GetProperty("name").GetString());
        Assert.False(json.GetProperty("items")[2].GetProperty("hasPhoto").GetBoolean());
        Assert.True(json.GetProperty("items")[0].GetProperty("student").GetProperty("isArchived").GetBoolean());
        Assert.Equal(new[] { "ielts", "cefr", "olympiad" },
            json.GetProperty("byKind").EnumerateArray().Select(k => k.GetProperty("kind").GetString()));
        Assert.DoesNotContain("att2", raw);   // rasm — faqat "bor/yo'q"
        host.AssertReadsOnlyDeclared(typeof(MessagingTools));
    }

    [Theory]
    [InlineData("s1", null, null, null, null, new[] { "a1" })]
    [InlineData(null, "olympiad", null, null, null, new[] { "a2" })]
    [InlineData(null, null, -12, -1, null, new[] { "a3", "a1" })]
    [InlineData(null, null, null, null, "b2", new[] { "a3" })]          // natija bo'yicha
    [InlineData(null, null, null, null, "olimpiada", new[] { "a2" })]   // nomi bo'yicha
    public async Task Yutuqlar_filtrlari(string? studentId, string? kind, int? from, int? to, string? search, string[] expected)
    {
        using var host = new McpTestHost();
        Seed(host);

        var json = McpTestHost.Parse(await host.Tool<MessagingTools>(McpTestHost.SuperAdmin()).StudentAchievements(
            studentId, kind, from is null ? null : D(from.Value), to is null ? null : D(to.Value), search));

        Assert.Equal(expected, Ids(json));
        await Assert.ThrowsAsync<McpException>(() => host.Tool<MessagingTools>(McpTestHost.SuperAdmin()).StudentAchievements(kind: "toefl"));
    }

    [Fact]
    public async Task Yutuqlar_oquvchilar_ruxsatisiz_RAD_etiladi()
    {
        using var host = new McpTestHost();
        Seed(host);

        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<MessagingTools>(McpTestHost.Staff("messages.broadcast")).StudentAchievements());
        await Assert.ThrowsAsync<McpAccessDeniedException>(() =>
            host.Tool<MessagingTools>(McpTestHost.Staff("students.list")).MessageCampaignReport("bc1"));
        Assert.True(host.State.Denied);
        Assert.Empty(host.Sql);
    }

    // ================================================================== students_search / student_profile — Telegram holati

    [Fact]
    public async Task Oquvchilar_royxatida_Telegram_soni_qarindoshlik_va_bloklaganlar()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch();
        var json = McpTestHost.Parse(raw);
        JsonElement Row(string id) => json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == id);

        var ali = Row("s1");
        Assert.True(ali.GetProperty("telegramLinked").GetBoolean());
        Assert.Equal(2, ali.GetProperty("telegram").GetProperty("chats").GetInt32());   // bloklagan ona sanalmaydi
        Assert.Equal(1, ali.GetProperty("telegram").GetProperty("blocked").GetInt32());
        Assert.Equal(new[] { "father", "student" },
            ali.GetProperty("telegram").GetProperty("relations").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(new[] { "parent" },
            Row("s2").GetProperty("telegram").GetProperty("relations").EnumerateArray().Select(r => r.GetString()));
        // Faqat bloklagan chati bor o'quvchi — ULANMAGAN (panel ustuni bilan bir xil).
        var davron = Row("s4");
        Assert.False(davron.GetProperty("telegramLinked").GetBoolean());
        Assert.Equal(1, davron.GetProperty("telegram").GetProperty("blocked").GetInt32());

        var unlinked = McpTestHost.Parse(await host.Tool<StudentTools>(McpTestHost.SuperAdmin()).StudentsSearch(telegram: "unlinked"));
        Assert.Contains("s4", Ids(unlinked));
        Assert.DoesNotContain("s1", Ids(unlinked));

        AssertNoSecrets(raw);
        host.AssertReadsOnlyDeclared(typeof(StudentTools));
        Assert.DoesNotContain(host.Sql, s => s.Contains("\"ChatId\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oquvchi_kartochkasida_Telegram_qarindoshlik_kesimi()
    {
        using var host = new McpTestHost();
        Seed(host);

        var raw = await host.Tool<StudentTools>(McpTestHost.Staff("students.list")).StudentProfile("s1");
        var tg = McpTestHost.Parse(raw).GetProperty("telegram");

        Assert.True(tg.GetProperty("linked").GetBoolean());
        Assert.Equal(2, tg.GetProperty("chats").GetInt32());
        Assert.Equal(1, tg.GetProperty("blocked").GetInt32());
        var rel = tg.GetProperty("relations").EnumerateArray().ToList();
        Assert.Equal(new[] { "Otasi", "O'zi" }, rel.Select(r => r.GetProperty("label").GetString()));
        Assert.All(rel, r => Assert.Equal(1, r.GetProperty("chats").GetInt32()));
        AssertNoSecrets(raw);
        host.AssertReadsOnlyDeclared(typeof(StudentTools));
    }
}
