using IntellectCRM.Application.Services;
using Xunit;

namespace IntellectCRM.Tests;

/// <summary>
/// META WEBHOOK PAYLOADI PARSERI (<see cref="InstagramEventParser"/>) testlari.
/// Rasmiy manba: <c>.claude/rules/marketing-instagram.md</c> §4–§5.
///
/// <para>Payloadlar Meta yuboradigan HAQIQIY shaklda yozilgan (izoh — <c>entry[].changes[]</c>,
/// DM — <c>entry[].messaging[]</c>), chunki parser aynan shu tuzilishga tayanadi.</para>
///
/// <para>Ikkita qoida bu yerda QULFLANADI:
/// (1) <b>o'z izohimiz tashlanadi</b> — aks holda bot o'z javobiga javob yozib cheksiz halqaga
/// tushadi; (2) <b>dedup kaliti deterministik</b> — bir xil payload qayta kelganda AYNAN bir xil
/// kalit chiqishi shart (Meta muvaffaqiyatsiz yetkazishni 36 soat qayta yuboradi).</para>
/// </summary>
public class InstagramEventParserTests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";

    /// <summary>Izoh payloadi — Meta'ning haqiqiy shakli.</summary>
    private static string CommentPayload(
        string fromId = ClientId, string text = "Narxi qancha?", string commentId = "17900000000000001") => $$"""
        {
          "object": "instagram",
          "entry": [{
            "id": "{{OurId}}",
            "time": 1786500000,
            "changes": [{
              "field": "comments",
              "value": {
                "id": "{{commentId}}",
                "text": "{{text}}",
                "timestamp": "2026-08-12T09:15:00+0000",
                "from": { "id": "{{fromId}}", "username": "ali_valiyev" },
                "media": { "id": "18000000000000001", "media_product_type": "FEED" }
              }
            }]
          }]
        }
        """;

    /// <summary>DM payloadi (echo bayrog'i bilan yoki bo'lmasdan).</summary>
    private static string DmPayload(
        string senderId = ClientId, string text = "Salom, kurslar haqida", string mid = "aWdfZG1fMQ",
        bool isEcho = false, string? recipientId = null)
    {
        var recipient = recipientId ?? (isEcho ? ClientId : OurId);
        var echoFlag = isEcho ? ", \"is_echo\": true" : "";
        return $$"""
        {
          "object": "instagram",
          "entry": [{
            "id": "{{OurId}}",
            "time": 1786500001,
            "messaging": [{
              "sender": { "id": "{{senderId}}" },
              "recipient": { "id": "{{recipient}}" },
              "timestamp": 1786500001000,
              "message": { "mid": "{{mid}}", "text": "{{text}}"{{echoFlag}} }
            }]
          }]
        }
        """;
    }

    // ===================== 1) Izoh =====================

    [Fact]
    public void Izoh_hodisasi_oqiladi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(CommentPayload(), OurId));

        Assert.Equal(IgConst.KindComment, ev.Kind);
        Assert.Equal("Narxi qancha?", ev.Text);
        Assert.Equal(ClientId, ev.SenderId);
        Assert.Equal("ali_valiyev", ev.Username);
        Assert.Equal("17900000000000001", ev.CommentId);
        Assert.Equal("18000000000000001", ev.MediaId);
        Assert.False(ev.IsEcho);
    }

    [Fact]
    public void Izoh_kaliti_comment_id_dan_quriladi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(CommentPayload(), OurId));
        Assert.Equal("comment:17900000000000001", ev.EventKey);
    }

    [Fact]
    public void Matnsiz_izoh_ham_qaytariladi()
    {
        // Jimgina tashlab yuborilsa mijoz yo'qolardi — pipeline uni "operator kerak" qilib qo'yadi.
        var ev = Assert.Single(InstagramEventParser.Parse(CommentPayload(text: ""), OurId));
        Assert.Equal("", ev.Text);
    }

    [Fact]
    public void Qollab_quvvatlanmaydigan_field_tashlanadi()
    {
        // `mentions`, `live_comments` — ishlanmaydi (hodisa navbatda `skipped` bo'lib ko'rinadi).
        var json = CommentPayload().Replace("\"field\": \"comments\"", "\"field\": \"mentions\"");
        Assert.Empty(InstagramEventParser.Parse(json, OurId));
    }

    // ===================== 2) CHEKSIZ HALQA HIMOYASI =====================

    [Fact]
    public void Oz_izohimiz_tashlanadi()
    {
        // `from.id` bizning saqlangan akkaunt id'miz — bu bizning javobimiz, unga javob yozilmaydi.
        Assert.Empty(InstagramEventParser.Parse(CommentPayload(fromId: OurId), OurId));
    }

    [Fact]
    public void Oz_izohimiz_entry_id_bilan_ham_tanib_olinadi()
    {
        // Saqlangan id hali yo'q (akkaunt endi ulangan) — zaxira solishtiruv `entry.id` bo'yicha.
        Assert.Empty(InstagramEventParser.Parse(CommentPayload(fromId: OurId), ourIgUserId: ""));
    }

    [Fact]
    public void Ozimiz_yuborgan_DM_echo_deb_belgilanadi_bayroqsiz_ham()
    {
        // `is_echo` kelmasa ham jo'natuvchi biz bo'lsak — bu bizning xabarimiz.
        var ev = Assert.Single(InstagramEventParser.Parse(
            DmPayload(senderId: OurId, recipientId: ClientId), OurId));
        Assert.True(ev.IsEcho);
        Assert.Equal(IgConst.KindEcho, ev.Kind);
    }

    // ===================== 3) DM va echo =====================

    [Fact]
    public void DM_hodisasi_oqiladi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(DmPayload(), OurId));

        Assert.Equal(IgConst.KindDm, ev.Kind);
        Assert.Equal("Salom, kurslar haqida", ev.Text);
        Assert.Equal(ClientId, ev.SenderId);
        Assert.Equal("aWdfZG1fMQ", ev.IgMessageId);
        Assert.Equal("dm:aWdfZG1fMQ", ev.EventKey);
        Assert.False(ev.IsEcho);
        Assert.Equal("", ev.Username);   // DM'da username kelmaydi
    }

    [Fact]
    public void Echo_hodisasida_suhbatdosh_qabul_qiluvchi_boladi()
    {
        // ⚠️ Echo'da `sender` — BIZ. Pauza mijozning suhbatiga qo'yilishi kerak, ya'ni `recipient`.
        var ev = Assert.Single(InstagramEventParser.Parse(
            DmPayload(senderId: OurId, text: "Assalomu alaykum!", mid: "echo-1", isEcho: true), OurId));

        Assert.True(ev.IsEcho);
        Assert.Equal(IgConst.KindEcho, ev.Kind);
        Assert.Equal(ClientId, ev.SenderId);
        Assert.Equal("echo:echo-1", ev.EventKey);
    }

    [Fact]
    public void Xabar_bolmagan_hodisa_tashlanadi()
    {
        // `reaction` / `read` / `delivery` — xabar emas.
        var json = $$"""
            { "entry": [{ "id": "{{OurId}}", "messaging": [
                { "sender": {"id":"{{ClientId}}"}, "recipient": {"id":"{{OurId}}"},
                  "reaction": { "mid": "m1", "action": "react", "emoji": "❤️" } }
            ]}]}
            """;
        Assert.Empty(InstagramEventParser.Parse(json, OurId));
    }

    // ===================== 4) Bitta bodyda bir nechta hodisa =====================

    [Fact]
    public void Bitta_entry_ichida_izoh_ham_DM_ham_oqiladi()
    {
        // ⚠️ Ikkala massiv bitta `entry` ichida bo'lishi mumkin — parser IKKALASINI ko'radi.
        var json = $$"""
            {
              "object": "instagram",
              "entry": [{
                "id": "{{OurId}}",
                "time": 1786500000,
                "changes": [{ "field": "comments", "value": {
                    "id": "c-1", "text": "Izoh", "from": { "id": "{{ClientId}}", "username": "ali" } } } ],
                "messaging": [{
                    "sender": { "id": "{{ClientId}}" }, "recipient": { "id": "{{OurId}}" },
                    "timestamp": 1786500001000,
                    "message": { "mid": "m-1", "text": "DM" } }]
              }]
            }
            """;

        var list = InstagramEventParser.Parse(json, OurId);

        Assert.Equal(2, list.Count);
        Assert.Equal(IgConst.KindComment, list[0].Kind);
        Assert.Equal(IgConst.KindDm, list[1].Kind);
    }

    [Fact]
    public void Bir_nechta_entry_ham_oqiladi()
    {
        var json = $$"""
            { "entry": [
              { "id": "{{OurId}}", "changes": [{ "field": "comments", "value": {
                  "id": "c-1", "text": "Birinchi", "from": { "id": "{{ClientId}}" } } } ] },
              { "id": "{{OurId}}", "changes": [{ "field": "comments", "value": {
                  "id": "c-2", "text": "Ikkinchi", "from": { "id": "999" } } } ] }
            ]}
            """;

        var list = InstagramEventParser.Parse(json, OurId);

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "comment:c-1", "comment:c-2" }, list.Select(e => e.EventKey).ToArray());
    }

    // ===================== 5) Buzuq / kutilmagan JSON =====================

    [Theory]
    [InlineData("{buzuq")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]                       // massiv — obyekt kutilgan
    [InlineData("\"shunchaki matn\"")]
    [InlineData("{}")]                       // `entry` yo'q
    [InlineData("{\"entry\": {}}")]          // `entry` massiv emas
    [InlineData("{\"entry\": [null, 5]}")]   // elementlar obyekt emas
    public void Buzuq_yoki_kutilmagan_JSON_bosh_royxat_qaytaradi(string raw)
    {
        // ⚠️ Istisno OTILMAYDI: bitta noto'g'ri payload butun navbatni to'xtatib qo'ymasin.
        Assert.Empty(InstagramEventParser.Parse(raw, OurId));
    }

    [Fact]
    public void Taniqli_id_siz_izoh_tashlanadi()
    {
        var json = "{\"entry\":[{\"id\":\"" + OurId + "\",\"changes\":[{\"field\":\"comments\"," +
                   "\"value\":{\"text\":\"kim yozdi noma'lum\"}}]}]}";
        Assert.Empty(InstagramEventParser.Parse(json, OurId));
    }

    // ===================== 6) DEDUP KALITI — DETERMINISTIK =====================

    [Fact]
    public void Bir_xil_payload_ikki_marta_parse_qilinsa_kalit_bir_xil()
    {
        // ⚠️ Kalit `Guid`/`Random`/`GetHashCode()` ga tayanmaydi — aks holda restartdan keyin
        // dedup umuman ishlamasdi va Meta'ning qayta yuborishlari takroriy javobga aylanardi.
        var json = DmPayload();
        var birinchi = Assert.Single(InstagramEventParser.Parse(json, OurId)).EventKey;
        var ikkinchi = Assert.Single(InstagramEventParser.Parse(json, OurId)).EventKey;

        Assert.Equal(birinchi, ikkinchi);
    }

    [Fact]
    public void Kalit_id_siz_hodisada_ham_takrorlanadigan_hash()
    {
        // `comment_id` ham, `mid` ham yo'q — kalit jo'natuvchi + vaqt + matn hash'idan.
        var a = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "", "555", "1786500000", "Salom");
        var b = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "", "555", "1786500000", "Salom");

        Assert.Equal(a, b);
        Assert.StartsWith("dm:555:1786500000:", a);
    }

    [Fact]
    public void Turli_hodisa_turli_kalit_beradi()
    {
        var izoh = InstagramEventParser.EventKeyOf(IgConst.KindComment, "c-1", "", "555", "1", "Salom");
        var boshqaIzoh = InstagramEventParser.EventKeyOf(IgConst.KindComment, "c-2", "", "555", "1", "Salom");
        var dm = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "m-1", "555", "1", "Salom");

        Assert.NotEqual(izoh, boshqaIzoh);
        Assert.NotEqual(izoh, dm);
    }

    [Fact]
    public void Kalit_matn_ozgarsa_ozgaradi()
    {
        var a = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "", "555", "1", "Salom");
        var b = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "", "555", "1", "Salom!");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Echo_va_DM_bir_xil_mid_da_turli_kalit_beradi()
    {
        // Aks holda bizning javobimiz mijozning xabari bilan bir hodisa deb sanalardi.
        var dm = InstagramEventParser.EventKeyOf(IgConst.KindDm, "", "m-1", "555", "1", "x");
        var echo = InstagramEventParser.EventKeyOf(IgConst.KindEcho, "", "m-1", "555", "1", "x");
        Assert.NotEqual(dm, echo);
    }

    /* ═══════════ QO'LLAB-QUVVATLANMAYDIGAN MAYDONLAR ═══════════ */

    /// <summary>Meta'da keraksiz maydonga obuna bo'lib qolish oson. Undan kelgan hodisa
    /// tashlanadi, lekin SABABI navbat yozuvida ko'rinishi kerak — aks holda admin
    /// "hodisa kelyapti, hech narsa bo'lmayapti" holatining sababini topa olmasdi.</summary>
    [Fact]
    public void Qollab_quvvatlanmaydigan_maydon_nomi_qaytariladi()
    {
        const string raw = """
            { "entry": [{ "id": "1", "changes": [
                { "field": "mentions", "value": {} },
                { "field": "live_comments", "value": {} },
                { "field": "mentions", "value": {} } ]}]}
            """;

        var fields = InstagramEventParser.UnsupportedFields(raw);

        Assert.Equal("mentions, live_comments", fields);   // takrorsiz va tartibi saqlanadi
    }

    /// <summary>
    /// 🔴 Meta konsolidagi <b>«Test»</b> tugmasi DM'ni BOSHQA konvertda yuboradi:
    /// <c>changes[].field = "messages"</c>, <c>value</c> ichida esa aynan <c>messaging[]</c>
    /// elementi. Qo'llab-quvvatlanmasa sinov "Qo'llab-quvvatlanmaydigan hodisa: messages"
    /// degan CHALG'ITUVCHI xato berardi — biz ISHLAYDIGAN maydon nomi bilan.
    /// (2026-08-22, prodda sozlash paytida aniqlandi.)
    /// </summary>
    [Fact]
    public void Meta_TEST_konvertidagi_DM_ham_oqiladi()
    {
        const string raw = """
            {"object":"instagram","entry":[{"id":"0","time":1787422449,"changes":[{"field":"messages",
              "value":{"sender":{"id":"12334"},"recipient":{"id":"23245"},"timestamp":"1527459824",
                       "message":{"mid":"random_mid","text":"random_text"}}}]}]}
            """;

        var events = InstagramEventParser.Parse(raw, new InstagramEventParser.IgSelf("23245", "", "biz"));

        var e = Assert.Single(events);
        Assert.Equal(IgConst.KindDm, e.Kind);
        Assert.Equal("random_text", e.Text);
        Assert.Equal("12334", e.SenderId);          // jo'natuvchi — mijoz, biz emas
        Assert.Equal("random_mid", e.IgMessageId);
        Assert.False(e.IsEcho);
        // Endi "qo'llab-quvvatlanmaydi" ro'yxatiga TUSHMAYDI.
        Assert.Equal("", InstagramEventParser.UnsupportedFields(raw));
    }

    /// <summary>
    /// 🔴 DM tomonida hodisa turi <c>changes[].field</c> da EMAS, <c>messaging[]</c>
    /// obyektining KALITIDA keladi. 2026-08-22 da prodda aynan shu holat bo'ldi: Meta faqat
    /// <c>message_edit</c> yuborardi (ilova darajasida <c>messages</c> belgilanmagan), navbatda
    /// esa "qo'llab-quvvatlanmaydigan tur" degan umumiy matn turardi va "DM nega kelmayapti"
    /// savoli javobsiz qolardi.
    /// </summary>
    [Fact]
    public void Messaging_ichidagi_hodisa_turi_ham_nomlanadi()
    {
        const string raw = """
            {"object":"instagram","entry":[{"time":1787421411124,"id":"178414632",
              "messaging":[{"timestamp":1787421411020,"message_edit":{"mid":"abc","num_edit":0}}]}]}
            """;

        Assert.Equal("message_edit", InstagramEventParser.UnsupportedFields(raw));
    }

    // ===================== ObjectType — RAD ETILGAN so'rov uchun tashxis =====================

    /// <summary>
    /// Imzo mos kelmagan so'rov qayta ishlanmaydi, ya'ni "qaysi mahsulot yuboryapti" savoli
    /// FAQAT shu funksiya orqali javob topadi — tuzatish esa aynan shunga bog'liq
    /// (<c>instagram</c> → Instagram App Secret, <c>page</c> → so'rov <c>/leadgen</c> ga).
    /// </summary>
    [Theory]
    [InlineData("""{"object":"instagram","entry":[]}""", "instagram (izoh/DM)")]
    [InlineData("""{"object":"page","entry":[]}""", "page (Facebook Page / reklama lidi)")]
    public void ObjectType_manbani_nomlaydi(string raw, string kutilgan) =>
        Assert.Equal(kutilgan, InstagramEventParser.ObjectType(raw));

    /// <summary>
    /// ⚠️ Qiymatni TASHQARIDAGI yuboruvchi belgilaydi va u to'g'ridan-to'g'ri logga tushadi.
    /// Oq ro'yxatda yo'q qiymat xom holicha CHIQMAYDI — aks holda soxta qatorli
    /// <c>object</c> log fayliga yozib qo'yardi (<c>ShortUa</c> bilan bir xil mulohaza).
    /// </summary>
    [Fact]
    public void ObjectType_notanish_qiymatni_XOM_HOLICHA_qaytarmaydi()
    {
        const string raw = """{"object":"begona\nwarn: soxta qator","entry":[]}""";

        Assert.Equal("boshqa", InstagramEventParser.ObjectType(raw));
    }

    /// <summary>Buzuq/bo'sh payload ham yiqilmasin — tashxis logi asosiy oqimni buzmaydi.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{buzuq")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"entry":[]}""")]
    public void ObjectType_buzuq_payloadda_yiqilmaydi(string raw) =>
        Assert.Equal("noma'lum", InstagramEventParser.ObjectType(raw));

    /// <summary>Konvert maydonlari (kim/kimga/qachon) hodisa turi EMAS — ro'yxatga tushmasin,
    /// aks holda har xabarda "sender, recipient, timestamp" degan shovqin chiqardi.</summary>
    [Fact]
    public void Oddiy_DM_da_hech_narsa_nomlanmaydi()
    {
        const string raw = """
            {"object":"instagram","entry":[{"id":"1","messaging":[{
              "sender":{"id":"5"},"recipient":{"id":"1"},"timestamp":1787421411020,
              "message":{"mid":"m1","text":"Salom"}}]}]}
            """;

        Assert.Equal("", InstagramEventParser.UnsupportedFields(raw));
    }

    /// <summary>Izoh va DM tomonidagi turlar BIRGA to'planadi va takrorlanmaydi.</summary>
    [Fact]
    public void Izoh_va_DM_turlari_birga_toplanadi()
    {
        const string raw = """
            {"entry":[
              {"id":"1","changes":[{"field":"mentions","value":{}}]},
              {"id":"1","messaging":[{"message_edit":{"mid":"a"}},{"reaction":{"mid":"b"}},
                                     {"message_edit":{"mid":"c"}}]}]}
            """;

        Assert.Equal("mentions, message_edit, reaction", InstagramEventParser.UnsupportedFields(raw));
    }

    /// <summary>Ishlanadigan maydonlar ro'yxatga TUSHMAYDI — aks holda normal izoh hodisasi
    /// ham "qo'llab-quvvatlanmaydi" deb belgilanardi.</summary>
    [Theory]
    [InlineData("""{ "entry": [{ "id": "1", "changes": [{ "field": "comments", "value": {} }]}]}""")]
    [InlineData("""{ "entry": [{ "id": "1", "changes": [{ "field": "messaging_policy_enforcement", "value": {} }]}]}""")]
    [InlineData("""{ "entry": [{ "id": "1", "messaging": [] }]}""")]
    [InlineData("")]
    [InlineData("{ buzuq json")]
    public void Ishlanadigan_va_buzuq_payloadda_bosh_satr(string raw)
    {
        Assert.Equal("", InstagramEventParser.UnsupportedFields(raw));
}

/// <summary>
/// HALQA HIMOYASINING 1-QAVATI — "o'zimizni tanish" UCHALA identifikator bo'yicha, va Meta
/// bergan VAQTNING o'qilishi.
///
/// <para>⚠️ Ilgari faqat IKKI qiymat solishtirilardi (<c>IgUserId</c> + <c>entry.id</c>), holbuki
/// hujjat uchtasini talab qilardi. Webhook'da <c>from.id</c> ba'zan app-scoped id bo'lib keladi —
/// o'shanda bot O'Z izohini begona deb bilib, unga javob yozib CHEKSIZ HALQAGA tushardi.</para>
/// </summary>
public class InstagramSelfIdentityTests
{
    private const string IgUserId = "17841400000000000";
    private const string AppScoped = "9988776655";
    private const string Username = "intellect_kokand";

    private static string Comment(string fromId, string username = "begona") => $$"""
        { "entry": [{ "id": "entry-boshqa", "time": 1786500000, "changes": [{ "field": "comments",
            "value": { "id": "c-1", "text": "salom",
                       "from": { "id": "{{fromId}}", "username": "{{username}}" } } }]}]}
        """;

    private static InstagramEventParser.IgSelf Self =>
        new(IgUserId: IgUserId, AppScopedId: AppScoped, Username: Username);

    [Fact]
    public void OZ_izohimiz_IG_id_boyicha_tashlanadi()
    {
        Assert.Empty(InstagramEventParser.Parse(Comment(IgUserId), Self));
    }

    [Fact]
    public void OZ_izohimiz_APP_SCOPED_id_boyicha_ham_tashlanadi()
    {
        // ⚠️ AYNAN SHU holat ilgari o'tib ketardi — halqaning sababi.
        Assert.Empty(InstagramEventParser.Parse(Comment(AppScoped), Self));
    }

    [Fact]
    public void OZ_izohimiz_USERNAME_boyicha_ham_tashlanadi()
    {
        // Id kutilmagan formatda kelsa ham o'z username'imizga javob yozmaymiz (zaxira qavat).
        Assert.Empty(InstagramEventParser.Parse(Comment("kutilmagan-id", Username), Self));
        // "@" bilan va boshqa registrda kelsa ham.
        Assert.Empty(InstagramEventParser.Parse(Comment("kutilmagan-id", "@Intellect_Kokand"), Self));
    }

    [Fact]
    public void BEGONA_izoh_baribir_otadi()
    {
        var events = InstagramEventParser.Parse(Comment("5550001112223"), Self);
        Assert.Single(events);
        Assert.Equal("salom", events[0].Text);
    }

    [Fact]
    public void Eski_chaqiruv_shakli_ISHLAYVERADI()
    {
        // Bitta id beradigan eski ko'rinish saqlanib qolgan (mavjud chaqiruvchilar buzilmasin).
        Assert.Empty(InstagramEventParser.Parse(Comment(IgUserId), IgUserId));
        Assert.Single(InstagramEventParser.Parse(Comment("5550001112223"), IgUserId));
    }

    // ─────────────── META VAQTI ───────────────

    [Theory]
    [InlineData("1786500000000")]   // millisekund
    [InlineData("1786500000")]      // soniya
    public void Meta_epoch_vaqti_ISO_ga_ogiriladi(string raw)
    {
        var iso = InstagramEventParser.ToIso(raw);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$", iso);
        // Ikkala ko'rinish ham AYNAN bir xil vaqtni bildiradi.
        Assert.Equal(InstagramEventParser.ToIso("1786500000"), InstagramEventParser.ToIso("1786500000000"));
    }

    [Fact]
    public void Meta_ISO_vaqti_ham_oqiladi_buzuq_bolsa_BOSH()
    {
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T", InstagramEventParser.ToIso("2026-08-13T10:00:00+0000"));
        // O'qib bo'lmasa — BO'SH: noto'g'ri vaqt yozgandan ko'ra "noma'lum" yaxshiroq
        // (chaqiruvchi joriy vaqtga qaytadi).
        Assert.Equal("", InstagramEventParser.ToIso("allaqachon"));
        Assert.Equal("", InstagramEventParser.ToIso(""));
        Assert.Equal("", InstagramEventParser.ToIso(null));
    }

    [Fact]
    public void Hodisaga_Meta_vaqti_yoziladi()
    {
        var events = InstagramEventParser.Parse(Comment("5550001112223"), Self);
        // `entry.time` dan olinadi (izohda alohida `timestamp` bo'lmasa).
        Assert.NotEqual("", events[0].SentAtIso);
    }
}

/// <summary>
/// E6 — STORY JAVOBI · STORY MENTION · ULASHILGAN IG POST · XABARNI O'CHIRISH ·
/// META SIYOSATI OGOHLANTIRISHI.
///
/// <para>Bu payloadlar ilgari parserga UMUMAN tushmasdi: story javobi oddiy DM bo'lib
/// ko'rinardi (AI kontekstsiz javob yozardi), o'chirish hodisasi esa asl xabar bilan bir xil
/// kalit olib, navbatdagi unikal indeks tomonidan "takror" deb rad etilardi.</para>
/// </summary>
public class InstagramEventParserE6Tests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";

    /// <summary><c>message</c> obyektining ichini tashqaridan beradigan DM payloadi.</summary>
    private static string Messaging(string messageBody) => $$"""
        { "object": "instagram", "entry": [{
            "id": "{{OurId}}", "time": 1786500001,
            "messaging": [{
              "sender": { "id": "{{ClientId}}" },
              "recipient": { "id": "{{OurId}}" },
              "timestamp": 1786500001000,
              "message": {{messageBody}} }]}]}
        """;

    // ===================== 9.1) STORY JAVOBI =====================

    [Fact]
    public void Story_javobi_id_va_url_bilan_oqiladi()
    {
        var json = Messaging("""
            { "mid": "m-story-1", "text": "Zo'r ekan!",
              "reply_to": { "story": { "id": "18000000000000009",
                                       "url": "https://cdn.example/story.jpg?sig=abc" } } }
            """);

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(IgConst.KindDm, ev.Kind);
        Assert.Equal("18000000000000009", ev.StoryId);
        Assert.Equal("https://cdn.example/story.jpg?sig=abc", ev.StoryUrl);
        // ⚠️ CDN havolasi tez o'ladi — hech bo'lmaganda id va manzil KONTEKSTDA saqlanadi.
        var note = InstagramEventParser.ContextNote(ev);
        Assert.Contains("Story'ga javob", note);
        Assert.Contains("18000000000000009", note);
    }

    [Fact]
    public void Oddiy_xabarga_javob_story_deb_hisoblanmaydi()
    {
        // `reply_to` odatiy javobda ham keladi — u yerda faqat `mid` bo'ladi.
        var json = Messaging("""{ "mid": "m-1", "text": "ha", "reply_to": { "mid": "m-0" } }""");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal("", ev.StoryId);
        Assert.Equal("", ev.StoryUrl);
        Assert.Equal("", InstagramEventParser.ContextNote(ev));   // kontekst YO'Q — eski xulq
    }

    // ===================== 9.2) STORY MENTION =====================

    [Fact]
    public void Story_mention_ajratib_belgilanadi()
    {
        var json = Messaging("""
            { "mid": "m-mention-1", "text": "",
              "attachments": [{ "type": "story_mention",
                                "payload": { "url": "https://cdn.example/mention.jpg" } }] }
            """);

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.True(ev.IsStoryMention);
        Assert.Equal("https://cdn.example/mention.jpg", ev.StoryUrl);
        Assert.Contains("eslatib", InstagramEventParser.ContextNote(ev));
    }

    // ===================== 9.3) ULASHILGAN IG POST =====================

    [Fact]
    public void Ig_post_attachmenti_oqiladi()
    {
        var json = Messaging("""
            { "mid": "m-post-1", "text": "Bu qanaqa kurs?",
              "attachments": [{ "type": "ig_post",
                                "payload": { "url": "https://instagram.com/p/XYZ" } }] }
            """);

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.True(ev.HasSharedPost);
        Assert.Equal("https://instagram.com/p/XYZ", ev.SharedPostUrl);
        Assert.Contains("IG post ulashildi", InstagramEventParser.ContextNote(ev));
    }

    [Fact]
    public void Eski_share_turi_ig_post_deb_hisoblanmaydi()
    {
        // ⚠️ `share` 2026-02-01 da olib tashlangan — uni "ulashilgan post" deb ko'rsatsak
        // hisobot eski payloadlarda ham yangi turdek ko'rinardi.
        var json = Messaging("""
            { "mid": "m-share-1", "text": "qara",
              "attachments": [{ "type": "share", "payload": { "url": "https://x" } }] }
            """);

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.False(ev.HasSharedPost);
        Assert.Equal("", InstagramEventParser.ContextNote(ev));
    }

    // ===================== 9.4) XABAR O'CHIRILDI =====================

    [Fact]
    public void Is_deleted_alohida_hodisa_turi_beradi()
    {
        var json = Messaging("""{ "mid": "m-del-1", "is_deleted": true }""");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(InstagramEventParser.KindDeleted, ev.Kind);
        Assert.True(ev.IsDeleted);
        Assert.Equal("m-del-1", ev.IgMessageId);
    }

    [Fact]
    public void Ochirish_kaliti_asl_xabar_kalitidan_FARQ_qiladi()
    {
        // ⚠️ Aks holda navbatdagi UNIKAL indeks o'chirish so'rovini "takror" deb rad etardi va
        // matn bazada abadiy qolib ketardi (Platform Terms buzilishi).
        var dm = Assert.Single(InstagramEventParser.Parse(
            Messaging("""{ "mid": "m-del-1", "text": "salom" }"""), OurId));
        var del = Assert.Single(InstagramEventParser.Parse(
            Messaging("""{ "mid": "m-del-1", "is_deleted": true }"""), OurId));

        Assert.Equal("dm:m-del-1", dm.EventKey);
        Assert.Equal("deleted:m-del-1", del.EventKey);
        Assert.NotEqual(dm.EventKey, del.EventKey);
    }

    // ===================== 9.7) META SIYOSATI OGOHLANTIRISHI =====================

    private const string PolicyJson = $$"""
        { "object": "instagram", "entry": [{
            "id": "{{OurId}}", "time": 1786500002,
            "messaging": [{
              "recipient": { "id": "{{OurId}}" },
              "timestamp": 1786500002000,
              "policy-enforcement": { "action": "warning",
                                      "reason": "Spammy automated messaging" } }]}]}
        """;

    [Fact]
    public void Siyosat_ogohlantirishi_tanilib_olinadi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(PolicyJson, OurId));

        Assert.Equal(InstagramEventParser.KindPolicy, ev.Kind);
        Assert.Equal("warning", ev.PolicyAction);
        Assert.Contains("Spammy", ev.PolicyReason);
    }

    [Fact]
    public void Siyosat_kaliti_DETERMINISTIK()
    {
        var a = Assert.Single(InstagramEventParser.Parse(PolicyJson, OurId)).EventKey;
        var b = Assert.Single(InstagramEventParser.Parse(PolicyJson, OurId)).EventKey;

        Assert.Equal(a, b);
        Assert.StartsWith("policy:", a);
    }

    [Fact]
    public void Siyosat_changes_korinishida_ham_oqiladi()
    {
        // Shakl Meta hujjatlarida qat'iy qotirilmagan — parser ATAYIN kechirimli.
        var json = $$"""
            { "object": "page", "entry": [{ "id": "{{OurId}}", "time": 1786500002,
                "changes": [{ "field": "messaging_policy_enforcement",
                              "value": { "action": "block", "reason": "Policy violation" } }]}]}
            """;

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(InstagramEventParser.KindPolicy, ev.Kind);
        Assert.Equal("block", ev.PolicyAction);
    }

    [Fact]
    public void Payloadda_siyosat_borligi_tez_tekshiriladi()
    {
        Assert.True(InstagramEventParser.ContainsPolicyEnforcement(PolicyJson));
        Assert.False(InstagramEventParser.ContainsPolicyEnforcement(
            Messaging("""{ "mid": "m-1", "text": "salom" }""")));
        Assert.False(InstagramEventParser.ContainsPolicyEnforcement("{buzuq"));
        Assert.False(InstagramEventParser.ContainsPolicyEnforcement(null));
    }
}

/// <summary>
/// E7 — REKLAMA ATRIBUTSIYASI DM'DA (<c>referral</c>).
///
/// <para>«Click to Instagram Direct» reklamasidan kelgan DM'da Meta e'lon id'sini O'ZI beradi.
/// Ilgari parser <c>referral</c> ni UMUMAN o'qimasdi va yo'qotish TO'LIQ JIMGINA edi: suhbat
/// <c>?source=ads</c> filtriga tushmasdi, ROI hisobotida reklama samarasi kam ko'rinardi,
/// hech qanday xato esa chiqmasdi.</para>
///
/// <para>⚠️ Bu — izohdagi <see cref="IgAdAttribution"/> dan FARQLI o'laroq <b>ANIQ</b>
/// atributsiya: qiymat tiklanmaydi, Meta bergan holicha olinadi.</para>
/// </summary>
public class InstagramEventParserAdReferralTests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";
    private const string AdId = "120212345678901234";

    /// <summary><c>message</c> ichini va (ixtiyoriy) <c>messaging[]</c> elementiga qo'shimcha
    /// maydonlarni tashqaridan beradigan DM payloadi.</summary>
    private static string Messaging(string messageBody, string extra = "") => $$"""
        { "object": "instagram", "entry": [{
            "id": "{{OurId}}", "time": 1786500001,
            "messaging": [{
              "sender": { "id": "{{ClientId}}" },
              "recipient": { "id": "{{OurId}}" },
              "timestamp": 1786500001000{{extra}},
              "message": {{messageBody}} }]}]}
        """;

    /// <summary>Meta'ning haqiqiy <c>referral</c> obyekti (reklama bosilib DM yozilganda).</summary>
    private const string Referral = $$"""
        "referral": {
          "ref": "kurs-sentabr",
          "ad_id": "{{AdId}}",
          "source": "ADS",
          "type": "OPEN_THREAD",
          "ads_context_data": { "ad_title": "Ingliz tili — 50% chegirma",
                                "photo_url": "https://cdn.example/ad.jpg" } }
        """;

    // ===================== 1) ASOSIY HOLAT =====================

    [Fact]
    public void Reklamadan_kelgan_DM_da_ad_id_oqiladi()
    {
        var json = Messaging($$"""{ "mid": "m-ad-1", "text": "Salom", {{Referral}} }""");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(IgConst.KindDm, ev.Kind);
        Assert.Equal(AdId, ev.AdId);
        Assert.Equal("ADS", ev.AdReferralSource);              // Meta bergan HOLICHA
        Assert.Equal("Ingliz tili — 50% chegirma", ev.AdTitle);
        Assert.Equal("dm:m-ad-1", ev.EventKey);                // dedup kaliti O'ZGARMAYDI
    }

    [Fact]
    public void Kontekstda_elon_NOMI_boladi_ad_id_ning_ozi_EMAS()
    {
        // ⚠️ 17 xonali raqam na AI'ga, na operatorga hech narsa aytmaydi (va AI uni mijozga
        // qaytarib yozib qo'yishi mumkin edi) — atributsiya MAYDON sifatida saqlanadi.
        var json = Messaging($$"""{ "mid": "m-ad-2", "text": "Narxi?", {{Referral}} }""");

        var note = InstagramEventParser.ContextNote(Assert.Single(InstagramEventParser.Parse(json, OurId)));

        Assert.Contains("Reklamadan keldi", note);
        Assert.Contains("Ingliz tili — 50% chegirma", note);
        Assert.DoesNotContain(AdId, note);
    }

    [Fact]
    public void Nomsiz_reklamada_kontekst_baribir_yoziladi()
    {
        var json = Messaging($$"""
            { "mid": "m-ad-3", "text": "Salom",
              "referral": { "ad_id": "{{AdId}}", "source": "ADS" } }
            """);

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(AdId, ev.AdId);
        Assert.Equal("", ev.AdTitle);
        Assert.Equal("[Reklamadan keldi]", InstagramEventParser.ContextNote(ev));
    }

    [Fact]
    public void Ad_id_SON_bolib_kelsa_ham_oqiladi()
    {
        // Meta uni odatda MATN qilib beradi; son bo'lib kelgani jimgina yo'qolmasin.
        var json = Messaging($$"""
            { "mid": "m-ad-4", "text": "Salom", "referral": { "ad_id": {{AdId}}, "source": "ADS" } }
            """);

        Assert.Equal(AdId, Assert.Single(InstagramEventParser.Parse(json, OurId)).AdId);
    }

    // ===================== 2) REFERRAL YO'Q — ESKI XULQ =====================

    [Fact]
    public void Referral_yoq_bolsa_maydonlar_BOSH_qoladi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(
            Messaging("""{ "mid": "m-1", "text": "Salom" }"""), OurId));

        Assert.Equal("", ev.AdId);
        Assert.Equal("", ev.AdReferralSource);
        Assert.Equal("", ev.AdTitle);
        Assert.Equal("", InstagramEventParser.ContextNote(ev));   // kontekst YO'Q — eski xulq
    }

    // ===================== 3) BOSHQA JOYLAR: messaging[] va postback =====================

    [Fact]
    public void Xabardan_TASHQARIDAGI_referral_ham_oqiladi()
    {
        // m.me havolasi / ice breaker: `referral` `messaging[]` elementining O'ZIDA keladi.
        var json = Messaging("""{ "mid": "m-ad-5", "text": "Salom" }""", $", {Referral}");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal(AdId, ev.AdId);
        Assert.Equal("ADS", ev.AdReferralSource);
    }

    [Fact]
    public void Postback_ichidagi_referral_ham_oqiladi()
    {
        var json = Messaging("""{ "mid": "m-ad-6", "text": "Salom" }""",
            $$""", "postback": { "title": "Boshlash", {{Referral}} }""");

        Assert.Equal(AdId, Assert.Single(InstagramEventParser.Parse(json, OurId)).AdId);
    }

    [Fact]
    public void Xabardagi_referral_TASHQARIDAGIDAN_ustun()
    {
        // Xabar bilan birga kelgan referral AYNAN shu xabarga tegishli.
        var json = Messaging(
            """{ "mid": "m-ad-7", "text": "Salom", "referral": { "ad_id": "111", "source": "ADS" } }""",
            """, "referral": { "ad_id": "222", "source": "SHORTLINK" }""");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.Equal("111", ev.AdId);
        Assert.Equal("ADS", ev.AdReferralSource);
    }

    // ===================== 4) BUZUQ / QISMAN REFERRAL — YIQILMAYDI =====================

    [Theory]
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": "ADS" }""")]              // obyekt emas
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": null }""")]               // null
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": [] }""")]                 // massiv
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": {} }""")]                 // bo'sh
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": { "source": "ADS" } }""")] // ad_id yo'q
    // konteksti satr (obyekt emas)
    [InlineData("""{ "mid": "m-b1", "text": "Salom", "referral": { "ad_id": "1", "ads_context_data": "x" } }""")]
    public void Buzuq_yoki_qisman_referral_hodisani_YIQITMAYDI(string messageBody)
    {
        var ev = Assert.Single(InstagramEventParser.Parse(Messaging(messageBody), OurId));

        Assert.Equal(IgConst.KindDm, ev.Kind);       // xabarning O'ZI baribir qaytadi
        Assert.Equal("dm:m-b1", ev.EventKey);
        Assert.Equal("", ev.AdTitle);                // nomi yo'q — kontekst matni ham qo'shilmaydi
    }

    [Fact]
    public void Reklama_konteksti_story_kontekstidan_OLDIN_turadi()
    {
        // "Qayerdan keldi" javobning MAVZUSINI belgilaydi, story esa faqat "nimaga javob".
        var json = Messaging($$"""
            { "mid": "m-ad-8", "text": "Zo'r!",
              "reply_to": { "story": { "id": "18000000000000009" } }, {{Referral}} }
            """);

        var note = InstagramEventParser.ContextNote(Assert.Single(InstagramEventParser.Parse(json, OurId)));

        Assert.StartsWith("[Reklamadan keldi", note);
        Assert.Contains("Story'ga javob", note);
    }
}

/// <summary>
/// E3 — REKLAMA IZOHI ATRIBUTSIYASI (<see cref="IgAdAttribution"/>), sof funksiyalar.
///
/// <para>⚠️ Bu — <b>TAXMINIY</b> atributsiya: <c>comments</c> webhook'ida <c>ad_id</c> umuman
/// yo'q, bog'lanish <c>media.id</c> ↔ <c>CreativeStoryId</c> orqali TIKLANADI. Boostlangan
/// organik postda ishlaydi, dark post va dinamik katalog reklamasida — yo'q.</para>
/// </summary>
public class IgAdAttributionTests
{
    private static IgAdAttribution.AdRow Ad(string id, string parent, string story) =>
        new(id, IgAdAttribution.LevelAd, parent, story);

    private static IgAdAttribution.AdRow AdSet(string id, string parent) =>
        new(id, IgAdAttribution.LevelAdset, parent, "");

    // ===================== media qismi =====================

    [Theory]
    [InlineData("17841400000000000_18000000000000001", "18000000000000001")]  // "{page}_{post}"
    [InlineData("18000000000000001", "18000000000000001")]                    // yalang id
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Media_qismi_ajratiladi(string story, string kutilgan)
    {
        Assert.Equal(kutilgan, IgAdAttribution.MediaPart(story));
    }

    [Fact]
    public void Buzuq_qiymatda_bosh_kalit_chiqmaydi()
    {
        // "abc_" — dumi bo'sh: bo'sh kalit HAMMA narsaga mos kelib ketardi.
        Assert.Equal("abc_", IgAdAttribution.MediaPart("abc_"));
    }

    // ===================== moslik =====================

    [Fact]
    public void Prefiksli_creative_yalang_media_bilan_moslashadi()
    {
        Assert.True(IgAdAttribution.Matches("18000000000000001", "17841400000000000_18000000000000001"));
    }

    [Fact]
    public void Aynan_teng_qiymatlar_moslashadi()
    {
        Assert.True(IgAdAttribution.Matches("18000000000000001", "18000000000000001"));
    }

    [Fact]
    public void Prefiksli_media_ham_moslashadi()
    {
        // Teskari holat: media id prefiks bilan kelsa ham ikkala tomon normallashtiriladi.
        Assert.True(IgAdAttribution.Matches("17841400000000000_18000000000000001", "18000000000000001"));
    }

    [Theory]
    [InlineData("18000000000000001", "17841400000000000_18000000000000002")]  // boshqa post
    [InlineData("", "17841_1800")]
    [InlineData("1800", "")]
    [InlineData(null, null)]
    public void Mos_kelmagan_va_bosh_kirish_moslik_BERMAYDI(string? media, string? story)
    {
        Assert.False(IgAdAttribution.Matches(media, story));
    }

    // ===================== e'lonni topish =====================

    [Fact]
    public void Mos_elon_topiladi()
    {
        var rows = new List<IgAdAttribution.AdRow>
        {
            Ad("ad-1", "adset-1", "17841_18000000000000001"),
            Ad("ad-2", "adset-2", "17841_18000000000000002"),
        };

        var found = IgAdAttribution.FindAd("18000000000000002", rows);

        Assert.NotNull(found);
        Assert.Equal("ad-2", found!.Value.ExternalId);
    }

    [Fact]
    public void Bir_post_bir_necha_elonda_bolsa_tanlov_DETERMINISTIK()
    {
        // ⚠️ Aks holda bir xil izoh har safar boshqa e'longa biriktirilib, hisobot beqaror bo'lardi.
        var rows = new List<IgAdAttribution.AdRow>
        {
            Ad("ad-9", "adset-1", "17841_1800"),
            Ad("ad-2", "adset-1", "17841_1800"),
            Ad("ad-5", "adset-1", "1800"),
        };

        var a = IgAdAttribution.FindAd("1800", rows);
        var b = IgAdAttribution.FindAd("1800", rows.AsEnumerable().Reverse().ToList());

        Assert.Equal("ad-2", a!.Value.ExternalId);
        Assert.Equal(a.Value.ExternalId, b!.Value.ExternalId);
    }

    [Fact]
    public void Topilmasa_null_qaytadi_ORGANIK()
    {
        var rows = new List<IgAdAttribution.AdRow> { Ad("ad-1", "adset-1", "17841_1800") };

        Assert.Null(IgAdAttribution.FindAd("9999", rows));
        Assert.Null(IgAdAttribution.FindAd("", rows));
        Assert.Null(IgAdAttribution.FindAd("1800", new List<IgAdAttribution.AdRow>()));
        Assert.Null(IgAdAttribution.FindAd("1800", null));
    }

    // ===================== kampaniya zanjiri =====================

    [Fact]
    public void Kampaniya_ad_adset_campaign_zanjiri_orqali_topiladi()
    {
        var ad = Ad("ad-1", "adset-1", "17841_1800");
        var parents = new List<IgAdAttribution.AdRow> { AdSet("adset-1", "camp-1") };

        Assert.Equal("camp-1", IgAdAttribution.CampaignOf(ad, parents));
    }

    [Fact]
    public void Ota_topilmasa_kampaniya_BOSH_lekin_elon_saqlanadi()
    {
        var ad = Ad("ad-1", "adset-1", "17841_1800");

        var match = IgAdAttribution.Resolve("1800", new List<IgAdAttribution.AdRow> { ad }, null);

        Assert.True(match.Found);
        Assert.Equal("ad-1", match.AdId);
        Assert.Equal("", match.CampaignId);          // yarim ma'lumot — hech qanaqasidan yaxshiroq
    }

    [Fact]
    public void Adset_va_campaign_darajasi_ham_togri_hisoblanadi()
    {
        var adset = new IgAdAttribution.AdRow("adset-1", IgAdAttribution.LevelAdset, "camp-1", "17841_1800");
        var campaign = new IgAdAttribution.AdRow("camp-1", IgAdAttribution.LevelCampaign, "", "17841_1800");

        Assert.Equal("camp-1", IgAdAttribution.CampaignOf(adset, null));
        Assert.Equal("camp-1", IgAdAttribution.CampaignOf(campaign, null));
    }

    [Fact]
    public void Topilmagan_natija_BOSH_SATR_beradi_null_emas()
    {
        // ⚠️ Loyihada "yo'q" qiymat — bo'sh satr; `default(AdMatch)` da satrlar `null` bo'lardi
        // va ular bazaga tushib ketardi.
        var none = IgAdAttribution.AdMatch.None;

        Assert.False(none.Found);
        Assert.Equal("", none.AdId);
        Assert.Equal("", none.CampaignId);
    }

    }

}

/// <summary>
/// FAQ TUGMASI (ice breaker) POSTBACK'i — <c>messaging[].postback</c> (FAQ moduli).
///
/// <para>Uch qoida qulflanadi: (1) <c>message</c>siz postback elementi TASHLANMAYDI (ilgari
/// «xabar emas» deb jimgina yo'qolardi — tugmani bosgan mijoz javobsiz qolardi);
/// (2) dedup kaliti <c>postback:</c> prefiksi bilan va DETERMINISTIK (§5 qoidasi);
/// (3) halqa himoyasi postback'da ham ishlaydi.</para>
/// </summary>
public class InstagramPostbackParserTests
{
    private const string OurId = "17841400000000000";
    private const string ClientId = "5550001112223";

    private static string PostbackJson(
        string senderId = ClientId, string payload = "FAQ:faq-1", string title = "Narxlar qancha?",
        string? mid = "pb-mid-1")
    {
        var midPart = mid is null ? "" : $"\"mid\": \"{mid}\", ";
        return $$"""
        {
          "object": "instagram",
          "entry": [{
            "id": "{{OurId}}",
            "time": 1786500002,
            "messaging": [{
              "sender": { "id": "{{senderId}}" },
              "recipient": { "id": "{{OurId}}" },
              "timestamp": 1786500002000,
              "postback": { {{midPart}}"title": "{{title}}", "payload": "{{payload}}" }
            }]
          }]
        }
        """;
    }

    [Fact]
    public void Postback_hodisasi_oqiladi()
    {
        var ev = Assert.Single(InstagramEventParser.Parse(PostbackJson(), OurId));

        Assert.Equal(InstagramEventParser.KindPostback, ev.Kind);
        Assert.Equal("Narxlar qancha?", ev.Text);              // matn — tugma SARLAVHASI
        Assert.Equal("FAQ:faq-1", ev.PostbackPayload);
        Assert.Equal(ClientId, ev.SenderId);
        Assert.Equal("pb-mid-1", ev.IgMessageId);
        Assert.False(ev.IsEcho);
    }

    [Fact]
    public void Postback_kaliti_mid_dan_alohida_prefiks_bilan_quriladi()
    {
        // ⚠️ `dm:` EMAS: kalit turlari aralashsa unikal indeks postback'ni DM takrori deb
        // rad etishi mumkin edi (KindDeleted bilan bir xil sabab).
        var ev = Assert.Single(InstagramEventParser.Parse(PostbackJson(), OurId));
        Assert.Equal("postback:pb-mid-1", ev.EventKey);
    }

    [Fact]
    public void Mid_bolmasa_kalit_deterministik_hash_dan_quriladi()
    {
        var json = PostbackJson(mid: null);
        var first = Assert.Single(InstagramEventParser.Parse(json, OurId));
        var second = Assert.Single(InstagramEventParser.Parse(json, OurId));

        Assert.StartsWith("postback:", first.EventKey);
        Assert.Equal(first.EventKey, second.EventKey);         // §5: restartdan keyin ham bir xil
    }

    [Fact]
    public void Oz_akkauntimizdan_kelgan_postback_tashlanadi()
    {
        Assert.Empty(InstagramEventParser.Parse(PostbackJson(senderId: OurId), OurId));
    }

    [Fact]
    public void Postback_qollab_quvvatlanmaydigan_maydon_deb_belgilanmaydi()
    {
        // Aks holda navbatda «Qo'llab-quvvatlanmaydigan hodisa: postback» degan CHALG'ITUVCHI
        // xato ko'rinardi (Meta «Test» tugmasidagi `messages` sabog'i bilan bir xil).
        Assert.Equal("", InstagramEventParser.UnsupportedFields(PostbackJson()));
    }

    [Fact]
    public void Postback_ichidagi_referral_reklama_atributsiyasini_beradi()
    {
        var json = PostbackJson().Replace(
            "\"payload\": \"FAQ:faq-1\"",
            "\"payload\": \"FAQ:faq-1\", \"referral\": { \"ad_id\": \"ad-77\", \"source\": \"ADS\" }");

        var ev = Assert.Single(InstagramEventParser.Parse(json, OurId));
        Assert.Equal("ad-77", ev.AdId);
        Assert.Equal("ADS", ev.AdReferralSource);
    }

    [Fact]
    public void Oddiy_dm_regressiyasi_postback_qoshilgani_bilan_buzilmaydi()
    {
        // `message` majburiyligi FAQAT postback uchun yumshadi — reaction/read kabi boshqa
        // `message`siz elementlar avvalgidek tashlanadi.
        var reaction = PostbackJson().Replace("\"postback\"", "\"reaction\"");
        Assert.Empty(InstagramEventParser.Parse(reaction, OurId));
    }
}
