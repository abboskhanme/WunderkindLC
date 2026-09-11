using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// INSTAGRAM SUHBATINI CRM LIDIGA ULASH (<see cref="InstagramLeadBridge"/>) testlari.
/// Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §6.
///
/// <para>Eng qimmat qoida — <b>FIRST-TOUCH</b>: mavjud lidda <c>Source</c> ham, <c>Stage</c> ham
/// O'ZGARMAYDI. Aks holda telefonini Instagram'da qoldirgan mijoz tufayli "Sayt" lidi
/// "Instagram"ga aylanib, menejer kanbanda qo'lda qo'ygan bosqich ham tashlanib ketardi.</para>
///
/// <para><c>UpsertAsync</c> ATAYIN <c>SaveChangesAsync</c> chaqirmaydi (chaqiruvchining
/// tranzaksiyasida saqlanadi) — shuning uchun testlar o'zi saqlaydi.</para>
/// </summary>
public class InstagramLeadBridgeTests
{
    private const string FirstStage = "stage-yangi";

    private static void Seed(TestDb db)
    {
        db.Context.LeadStages.AddRange(
            new LeadStage { Id = FirstStage, Title = "Yangi", Order = 0 },
            new LeadStage { Id = "stage-ikkinchi", Title = "Aloqada", Order = 1 });
        db.Context.SaveChanges();
    }

    private static IgConversation Conversation(string username = "ali_valiyev", string? leadId = null)
    {
        var conv = new IgConversation
        {
            IgUserId = "5550001112223",
            Username = username,
            Status = IgConst.StatusBot,
            LeadId = leadId,
            CreatedAt = AppClock.Iso(),
        };
        return conv;
    }

    private static IgAgentOutput Output(
        string contact = "", string name = "", string interest = "IELTS", int score = 80,
        string summary = "Narx so'radi, yozilmoqchi") =>
        new("Javob", "uz-Latn", "buying_intent", score, true, false, false,
            name, contact, interest, summary);

    // ===================== (a) Yangi lid =====================

    [Fact]
    public async Task Telefonli_yangi_suhbat_uchun_lid_yaratiladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "+998 90 123 45 67", name: "Ali Valiyev"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.True(isNew);
        var lead = Assert.Single(db.Context.Leads);
        Assert.Equal(leadId, lead.Id);
        Assert.Equal("Ali Valiyev", lead.FullName);
        Assert.Equal("+998-90-123-45-67", lead.Phone);
        Assert.Equal("IELTS", lead.InterestSubject);
        Assert.Equal(FirstStage, lead.Stage);           // birinchi bosqichga tushadi
        Assert.Equal(conv.LeadId, lead.Id);             // suhbat lidga bog'landi
    }

    [Fact]
    public async Task Yangi_lid_manbasi_sozlamadagi_nom_boladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "Instagram (reklama)");
        await db.Context.SaveChangesAsync();

        Assert.Equal("Instagram (reklama)", db.Context.Leads.Single().Source);
    }

    [Fact]
    public async Task Manba_nomi_bosh_bolsa_Instagram_yoziladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "   ");
        await db.Context.SaveChangesAsync();

        Assert.Equal("Instagram", db.Context.Leads.Single().Source);
    }

    [Fact]
    public async Task Yangi_lid_uchun_yaratilgan_hodisasi_yoziladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        var (leadId, _) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        var ev = Assert.Single(db.Context.LeadEvents);
        Assert.Equal(leadId, ev.LeadId);
        Assert.Equal("created", ev.Type);
        Assert.Equal(InstagramLeadBridge.ActorName, ev.ActorName);
        // ⚠️ "@ali_valiyev" EMAS, BOSILADIGAN havola — bu matn Telegram kartasidagi izohlar
        // lentasiga tushadi.
        Assert.Contains("https://instagram.com/ali_valiyev", ev.Text);
        Assert.DoesNotContain("@ali_valiyev", ev.Text);
        Assert.Equal(FirstStage, ev.ToStage);
    }

    // ===================== (b) MAVJUD LID — first-touch =====================

    [Fact]
    public async Task Osha_telefonli_lid_bor_bolsa_yangisi_yaratilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            FullName = "Ali Valiyev",
            Phone = "+998-90-123-45-67",
            Source = "Sayt",
            Stage = "stage-ikkinchi",
            CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();   // PhoneKey shu yerda hisoblanadi

        var conv = Conversation();
        db.Context.IgConversations.Add(conv);
        var (_, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.False(isNew);
        Assert.Single(db.Context.Leads);
    }

    [Fact]
    public async Task Mavjud_lidning_manbasi_va_bosqichi_OZGARMAYDI()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            FullName = "Ali Valiyev",
            Phone = "901234567",
            Source = "Sayt",
            Stage = "stage-ikkinchi",
            CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var conv = Conversation();
        db.Context.IgConversations.Add(conv);
        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        var lead = db.Context.Leads.Single();
        Assert.Equal("Sayt", lead.Source);              // ⚠️ FIRST-TOUCH
        Assert.Equal("stage-ikkinchi", lead.Stage);     // ⚠️ menejerning qo'lda qo'ygan bosqichi
    }

    [Fact]
    public async Task Mavjud_lidda_takroriy_murojaat_hisobi_oshadi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            FullName = "Ali", Phone = "901234567", Source = "Sayt",
            Stage = FirstStage, RepeatCount = 2, CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var conv = Conversation();
        db.Context.IgConversations.Add(conv);
        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        var lead = db.Context.Leads.Single();
        Assert.Equal(3, lead.RepeatCount);
        Assert.NotEqual("", lead.LastRepeatAt);
        var ev = Assert.Single(db.Context.LeadEvents);
        Assert.Equal("note", ev.Type);
        Assert.Contains("yana yozdi", ev.Text);
        Assert.Contains("https://instagram.com/ali_valiyev", ev.Text);
    }

    [Fact]
    public async Task Mavjud_lidning_toldirilgan_maydonlari_ustiga_yozilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            FullName = "Menejer kiritgan ism",
            Phone = "901234567",
            InterestSubject = "Matematika",
            Source = "Sayt",
            Stage = FirstStage,
            CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var conv = Conversation();
        db.Context.IgConversations.Add(conv);
        await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567", name: "AI topgan ism", interest: "IELTS"), "Instagram");
        await db.Context.SaveChangesAsync();

        var lead = db.Context.Leads.Single();
        Assert.Equal("Menejer kiritgan ism", lead.FullName);
        Assert.Equal("Matematika", lead.InterestSubject);
    }

    [Fact]
    public async Task Mavjud_lidning_BOSH_maydonlari_toldiriladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        db.Context.Leads.Add(new Lead
        {
            FullName = "", Phone = "901234567", InterestSubject = "",
            Source = "Sayt", Stage = FirstStage, CreatedAt = AppClock.Iso(),
        });
        await db.Context.SaveChangesAsync();

        var conv = Conversation();
        db.Context.IgConversations.Add(conv);
        await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567", name: "Ali Valiyev", interest: "IELTS"), "Instagram");
        await db.Context.SaveChangesAsync();

        var lead = db.Context.Leads.Single();
        Assert.Equal("Ali Valiyev", lead.FullName);
        Assert.Equal("IELTS", lead.InterestSubject);
    }

    // ===================== (c) Telefonsiz qaynoq lid =====================

    [Fact]
    public async Task Telefonsiz_qaynoq_lid_ham_yoziladi_va_nomida_username_boladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        var (_, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "", name: ""), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.True(isNew);
        var lead = db.Context.Leads.Single();
        // ⚠️ "@" YO'Q: bu nom Telegram lid kartasiga tushadi va u yerda "@nom" Telegram
        // mentioni bo'lib chizilardi (bosilganda Instagram'ga OLIB BORMASDI).
        Assert.Equal("ali_valiyev (Instagram)", lead.FullName);
        Assert.Equal("", lead.Phone);
    }

    [Fact]
    public async Task Username_ham_yoq_bolsa_umumiy_nom_qoyiladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation(username: "");
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "", name: ""), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.Equal("Instagram mijozi", db.Context.Leads.Single().FullName);
    }

    [Fact]
    public async Task Telefonsiz_ikki_suhbat_ikki_alohida_lid_ochadi()
    {
        // Telefon yo'q ⇒ dedup uchun kalit ham yo'q. Bu KUTILGAN xulq: ikki xil odam bo'lishi
        // mumkin, birlashtirish menejerning ishi (aks holda begona lidlar qo'shilib ketardi).
        using var db = TestDb.Sqlite();
        Seed(db);
        var a = Conversation(username: "ali");
        var b = Conversation(username: "vali");
        b.IgUserId = "777";
        db.Context.IgConversations.AddRange(a, b);

        await InstagramLeadBridge.UpsertAsync(db.Context, a, Output(contact: ""), "Instagram");
        await InstagramLeadBridge.UpsertAsync(db.Context, b, Output(contact: ""), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.Equal(2, db.Context.Leads.Count());
    }

    // ===================== (d) conv.LeadId — takroriy lid yaratilmaydi =====================

    [Fact]
    public async Task Suhbat_allaqachon_lidga_boglangan_bolsa_yangisi_yaratilmaydi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var bor = new Lead
        {
            FullName = "Ali", Phone = "", Source = "Instagram",
            Stage = FirstStage, CreatedAt = AppClock.Iso(),
        };
        db.Context.Leads.Add(bor);
        await db.Context.SaveChangesAsync();

        var conv = Conversation(leadId: bor.Id);
        db.Context.IgConversations.Add(conv);

        // ⚠️ Endi mijoz TELEFON qoldirdi — baribir yangi lid ochilmaydi, mavjudi to'ldiriladi.
        var (leadId, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.False(isNew);
        Assert.Equal(bor.Id, leadId);
        Assert.Single(db.Context.Leads);
        Assert.Equal("+998-90-123-45-67", db.Context.Leads.Single().Phone);
    }

    [Fact]
    public async Task Ikki_marta_chaqirilsa_ham_bitta_lid_qoladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();
        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.Single(db.Context.Leads);
        Assert.Equal(1, db.Context.Leads.Single().RepeatCount);
    }

    // ===================== (e) PhoneKey — qo'lda yozilmaydi =====================

    [Fact]
    public async Task PhoneKey_avtomatik_hisoblanadi()
    {
        // ⚠️ Bridge `PhoneKey` ni YOZMAYDI — uni AppDbContext.SaveChanges hisoblaydi.
        // Aks holda telefon bo'yicha dublikat qidiruvi (FindByPhoneAsync) ishlamay qolardi.
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(db.Context, conv, Output(contact: "+998 90 123 45 67"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.Equal("901234567", db.Context.Leads.Single().PhoneKey);
    }

    [Fact]
    public async Task Keyingi_suhbat_osha_telefon_boyicha_mavjud_lidni_topadi()
    {
        // Yuqoridagi PhoneKey haqiqatan ISHLAYAPTIMI — uchdan-uchgacha tekshiruv.
        using var db = TestDb.Sqlite();
        Seed(db);
        var birinchi = Conversation(username: "ali");
        db.Context.IgConversations.Add(birinchi);
        await InstagramLeadBridge.UpsertAsync(db.Context, birinchi, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        var ikkinchi = Conversation(username: "ali_ikkinchi");
        ikkinchi.IgUserId = "777";
        db.Context.IgConversations.Add(ikkinchi);
        var (_, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, ikkinchi, Output(contact: "+998901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.False(isNew);
        Assert.Single(db.Context.Leads);
    }

    // ===================== Izoh (Note) =====================

    [Fact]
    public async Task Lid_izohida_username_xulosa_va_ball_boladi()
    {
        using var db = TestDb.Sqlite();
        Seed(db);
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567", score: 95), "Instagram");
        await db.Context.SaveChangesAsync();

        var note = db.Context.Leads.Single().Note ?? "";
        // Izoh Telegram kartasiga tushadi — profil BOSILADIGAN havola bo'lishi shart.
        Assert.Contains("Instagram: https://instagram.com/ali_valiyev", note);
        Assert.DoesNotContain("@ali_valiyev", note);
        Assert.Contains("Narx so'radi", note);
        Assert.Contains("Qiziqish bali: 95", note);
    }

    [Fact]
    public async Task Bosqich_yoq_bolsa_ham_lid_yaratiladi()
    {
        // LeadStages jadvali bo'sh (yangi o'rnatish) — lid yo'qolib ketmasin.
        using var db = TestDb.Sqlite();
        var conv = Conversation();
        db.Context.IgConversations.Add(conv);

        var (_, isNew) = await InstagramLeadBridge.UpsertAsync(
            db.Context, conv, Output(contact: "901234567"), "Instagram");
        await db.Context.SaveChangesAsync();

        Assert.True(isNew);
        Assert.Equal("", db.Context.Leads.Single().Stage);
    }
}
