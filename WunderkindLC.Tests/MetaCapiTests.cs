using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CAPI (Conversions API) — PII HASHLASH testlari.
/// Rasmiy manba: <c>KENGAYTIRISH-PROMPT.md</c> §7.4, <c>META-API-MALUMOTNOMA.md</c> §12.2.
///
/// <para>Kutilgan hex qiymatlar mustaqil hisoblangan (<c>printf '%s' "…" | shasum -a 256</c>) —
/// ya'ni test kodning O'ZI bilan emas, <b>Meta kutadigan qiymat</b> bilan solishtiradi.
/// Normallashtirish bir belgi bilan farq qilsa Meta hodisani hech kimga bog'lay olmaydi va
/// nosozlik <b>jimgina</b> yuz beradi (so'rov 200 OK qaytadi) — shuning uchun bu testlar
/// modulning eng qimmat qismi.</para>
/// </summary>
public class MetaCapiHashTests
{
    /// <summary>sha256("998901234567") — mamlakat kodi bilan.</summary>
    private const string PhoneHash = "172443bcef1ac1d3905d3ff30a9b7c1d188a583c54f24ab8327a91f82054f2d3";

    /// <summary>sha256("ali@example.com")</summary>
    private const string EmailHash = "9d86ec2b59bb107bf110722c28601160b33aee06109ad44a5404231287b5fd29";

    [Fact]
    public void Telefon_malum_sha256_qiymatini_beradi()
    {
        Assert.Equal(PhoneHash, MetaCapiHash.Phone("+998 90 123-45-67"));
    }

    /// <summary>⚠️ Turli formatda kiritilgan BITTA raqam BITTA hash berishi shart — aks holda
    /// bir xil odam Meta uchun uch xil kishi bo'lib ko'rinardi.</summary>
    [Theory]
    [InlineData("+998 90 123 45 67")]
    [InlineData("998901234567")]
    [InlineData("901234567")]        // mahalliy — `998` o'zimiz qo'shamiz
    [InlineData("0901234567")]       // boshida nol (mahalliy yozuv)
    [InlineData("00998901234567")]   // xalqaro terish prefiksi
    [InlineData("(90) 123-45-67")]
    [InlineData(" +998901234567 ")]
    public void Turli_formatdagi_telefon_bir_xil_hash_beradi(string raw)
    {
        Assert.Equal(PhoneHash, MetaCapiHash.Phone(raw));
    }

    [Fact]
    public void Email_trim_va_lowercase_qilinadi()
    {
        Assert.Equal(EmailHash, MetaCapiHash.Email("  ALI@Example.COM "));
        Assert.Equal(EmailHash, MetaCapiHash.Email("ali@example.com"));
    }

    /// <summary>
    /// ⚠️ Apostrofning BARCHA ko'rinishi (turli klaviaturalar) BIR XIL hash berishi shart —
    /// aks holda bitta odam Meta uchun bir necha kishi bo'lib ko'rinardi.
    ///
    /// <para>🔴 <c>ʻ</c> (U+02BB) va <c>ʼ</c> (U+02BC) — Unicode'da <b>HARF</b>
    /// (ModifierLetter), ya'ni <c>char.IsLetterOrDigit</c> ularni O'TKAZIB YUBORADI. Aynan shu
    /// yerda xato bor edi: qolgan variantlar tashlanardi-yu, o'zbekcha klaviaturaning asosiy
    /// belgisi (U+02BB) qolib ketardi.</para>
    /// </summary>
    [Theory]
    [InlineData("To'lqin")]        // ASCII apostrof
    [InlineData("Toʻlqin")]        // U+02BB — MODIFIER LETTER TURNED COMMA (o'zbek yozuvi)
    [InlineData("Toʼlqin")]        // U+02BC — MODIFIER LETTER APOSTROPHE
    [InlineData("To‘lqin")]        // U+2018 — chap qo'shtirnoq
    [InlineData("To’lqin")]        // U+2019 — o'ng qo'shtirnoq (Word avtomatik qo'yadi)
    [InlineData("To`lqin")]        // teskari apostrof
    [InlineData("  TOLQIN.  ")]
    public void Ism_tinish_belgisiz_va_kichik_harfda_hashlanadi(string raw)
    {
        Assert.Equal("d012bbf2d3e85f066f23b3a28d99922eaf93f20527aae6739dff71a791f0cf69",
                     MetaCapiHash.Name(raw));
    }

    /// <summary>Ketma-ket bo'shliqlar bittaga keltiriladi → sha256("aziz karimov").</summary>
    [Fact]
    public void Ismdagi_ortiqcha_boshliqlar_bittaga_keltiriladi()
    {
        Assert.Equal("8c21db99a4bcce9752d0312375b6e2594553e12cc184e7c17487ff960e7fc037",
                     MetaCapiHash.Name("  Aziz   Karimov!  "));
    }

    /// <summary>⚠️ Bo'sh/yaroqsiz kirish — ISTISNO EMAS, bo'sh satr: chaqiruvchi maydonni
    /// payloadga umuman qo'shmaydi.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("-")]
    [InlineData("yo'q")]
    [InlineData("12345")]                    // juda qisqa
    [InlineData("9989012345671234567")]      // juda uzun — telefon emas
    public void Bosh_yoki_yarogsiz_telefon_bosh_satr_qaytaradi(string? raw)
    {
        Assert.Equal("", MetaCapiHash.Phone(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("yoq")]           // `@` yo'q
    [InlineData("@example.com")]  // `@` boshida
    [InlineData("ali@")]          // `@` oxirida
    [InlineData("ali @ mail.uz")] // bo'shliq bor
    public void Bosh_yoki_yarogsiz_email_bosh_satr_qaytaradi(string? raw)
    {
        Assert.Equal("", MetaCapiHash.Email(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("...")]
    public void Bosh_ism_bosh_satr_qaytaradi(string? raw)
    {
        Assert.Equal("", MetaCapiHash.Name(raw));
    }
}

/// <summary>
/// CAPI payload va Meta cheklovlari (§7.3, §7.5).
/// </summary>
public class MetaCapiPayloadTests
{
    private const string LeadgenId = "1234567890123456";

    private static MetaCapiEventInput Event(long unix, string? phone = "+998901234567") =>
        new("Sifatli lid", unix, MetaCapiUserData.FromRaw(LeadgenId, phone, email: ""));

    private static long Now => MetaCapiPayload.NowUnix();

    /* ───────── lead_id ───────── */

    /// <summary>🔴 ENG MUHIM: <c>lead_id</c> HASHLANMAYDI — payloadda XOM RAQAM turadi.
    /// Hashlab yuborilsa Meta hodisani lidga bog'lay olmaydi.</summary>
    [Fact]
    public void Lead_id_hashlanmaydi_xom_raqam_boladi()
    {
        var json = MetaCapiPayload.BuildEvent(Event(Now));

        using var doc = JsonDocument.Parse(json);
        var user = doc.RootElement.GetProperty("user_data");

        var leadId = user.GetProperty("lead_id");
        Assert.Equal(JsonValueKind.Number, leadId.ValueKind);   // satr emas, RAQAM
        Assert.Equal(1234567890123456L, leadId.GetInt64());
        Assert.Contains(LeadgenId, json);                        // xom holda ko'rinadi
    }

    /// <summary>Raqamga aylanmaydigan lead_id UMUMAN qo'shilmaydi — aks holda Meta butun
    /// so'rovni rad etardi.</summary>
    [Fact]
    public void Raqam_bolmagan_lead_id_payloadga_tushmaydi()
    {
        var json = MetaCapiPayload.BuildEvent(
            new MetaCapiEventInput("Sifatli lid", Now,
                MetaCapiUserData.FromRaw("abc-xyz", "+998901234567", "")));

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("user_data").TryGetProperty("lead_id", out _));
    }

    /* ───────── Maxfiylik ───────── */

    /// <summary>🔴 §7.7: <c>PayloadJson</c> bazaga yoziladi — unda XOM telefon/email
    /// BO'LMASLIGI shart (DPA aynan shuni tekshiradi).</summary>
    [Fact]
    public void Payloadda_xom_telefon_va_email_yoq()
    {
        var json = MetaCapiPayload.BuildEvent(
            new MetaCapiEventInput("Sifatli lid", Now,
                MetaCapiUserData.FromRaw(LeadgenId, "+998 90 123-45-67", "Ali@Example.com",
                                         "To'lqin", "Karimov")));

        // Xom ko'rinishlarning HECH BIRI bo'lmasin
        Assert.DoesNotContain("901234567", json);
        Assert.DoesNotContain("998901234567", json);
        Assert.DoesNotContain("example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Karimov", json, StringComparison.OrdinalIgnoreCase);

        // Hashlangan qiymatlar esa BOR
        Assert.Contains("172443bcef1ac1d3905d3ff30a9b7c1d188a583c54f24ab8327a91f82054f2d3", json);
        Assert.Contains("9d86ec2b59bb107bf110722c28601160b33aee06109ad44a5404231287b5fd29", json);
    }

    /// <summary>Bo'sh (yaroqsiz) qiymat maydon sifatida UMUMAN qo'shilmaydi — bo'sh hash
    /// yuborish "match rate" ni pasaytirardi.</summary>
    [Fact]
    public void Bosh_qiymatli_maydon_payloadga_qoshilmaydi()
    {
        var json = MetaCapiPayload.BuildEvent(Event(Now, phone: ""));

        using var doc = JsonDocument.Parse(json);
        var user = doc.RootElement.GetProperty("user_data");
        Assert.False(user.TryGetProperty("ph", out _));
        Assert.False(user.TryGetProperty("em", out _));
    }

    /* ───────── Payload shakli ───────── */

    [Fact]
    public void Payload_tuzilishi_spetsifikatsiyaga_mos()
    {
        const long unix = 1755600000;
        var json = MetaCapiPayload.BuildEvent(
            new MetaCapiEventInput("To'lov qildi", unix,
                MetaCapiUserData.FromRaw(LeadgenId, "+998901234567", ""),
                Value: 450000m));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("To'lov qildi", root.GetProperty("event_name").GetString());
        Assert.Equal(unix, root.GetProperty("event_time").GetInt64());
        Assert.Equal("system_generated", root.GetProperty("action_source").GetString());
        Assert.Equal($"{LeadgenId}_{unix}", root.GetProperty("event_id").GetString());

        var custom = root.GetProperty("custom_data");
        Assert.Equal("WunderkindLC", custom.GetProperty("lead_event_source").GetString());
        Assert.Equal("crm", custom.GetProperty("event_source").GetString());
        Assert.Equal(450000m, custom.GetProperty("value").GetDecimal());
        Assert.Equal("UZS", custom.GetProperty("currency").GetString());

        // `ph` — MASSIV (Meta shu shaklni kutadi)
        Assert.Equal(JsonValueKind.Array, root.GetProperty("user_data").GetProperty("ph").ValueKind);
    }

    /// <summary>Summasiz hodisada <c>value</c>/<c>currency</c> umuman bo'lmaydi.</summary>
    [Fact]
    public void Summasiz_hodisada_value_yozilmaydi()
    {
        var json = MetaCapiPayload.BuildEvent(Event(Now));

        using var doc = JsonDocument.Parse(json);
        var custom = doc.RootElement.GetProperty("custom_data");
        Assert.False(custom.TryGetProperty("value", out _));
        Assert.False(custom.TryGetProperty("currency", out _));
    }

    /// <summary>⚠️ <c>event_id</c> DETERMINISTIK: bir xil kirish → bir xil kalit
    /// (Meta 48 soatlik oynada dedup qiladi, aks holda qayta urinish konversiyani ikki marta
    /// sanardi).</summary>
    [Fact]
    public void Event_id_deterministik()
    {
        var a = MetaCapiPayload.EventId(LeadgenId, 1755600000);
        var b = MetaCapiPayload.EventId(LeadgenId, 1755600000);

        Assert.Equal(a, b);
        Assert.Equal("1234567890123456_1755600000", a);
    }

    /// <summary>To'liq so'rov tanasi: <c>{"data":[…]}</c>, produksiyada
    /// <c>test_event_code</c> YO'Q.</summary>
    [Fact]
    public void Sorov_tanasi_data_massivi_va_test_kodsiz()
    {
        var body = MetaCapiPayload.BuildBody(new[] { Event(Now), Event(Now) });

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetArrayLength());
        Assert.False(doc.RootElement.TryGetProperty("test_event_code", out _));

        // Sinovda esa qo'shiladi
        var testBody = MetaCapiPayload.BuildBody(new[] { Event(Now) }, "TEST12345");
        using var testDoc = JsonDocument.Parse(testBody);
        Assert.Equal("TEST12345", testDoc.RootElement.GetProperty("test_event_code").GetString());
    }

    /// <summary>🔴 Token so'rov MANZILIDA ketadi — payloadga TUSHMAYDI (payload bazaga
    /// yoziladi va logga chiqishi mumkin).</summary>
    [Fact]
    public void Sorov_tanasida_token_yoq()
    {
        var body = MetaCapiPayload.BuildBody(new[] { Event(Now) });
        Assert.DoesNotContain("access_token", body);
    }

    /* ───────── §7.5 cheklovlari ───────── */

    /// <summary>🔴 7 kundan eski hodisa RAD ETILADI — bittasi butun so'rovni yiqitadi.</summary>
    [Fact]
    public void Yetti_kundan_eski_event_time_rad_etiladi()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0);

        var sixDays = MetaCapiPayload.ToUnix(now.AddDays(-6));
        var eightDays = MetaCapiPayload.ToUnix(now.AddDays(-8));

        Assert.True(MetaCapiPayload.IsEventTimeAcceptable(sixDays, now));
        Assert.False(MetaCapiPayload.IsEventTimeAcceptable(eightDays, now));
        Assert.Contains("7 kundan eski", MetaCapiPayload.EventTimeError(eightDays, now));
        Assert.Equal("", MetaCapiPayload.EventTimeError(sixDays, now));
    }

    /// <summary>Kelajakdagi vaqt ham rad etiladi (kichik siljish — server soati — kechiriladi).</summary>
    [Fact]
    public void Kelajakdagi_event_time_rad_etiladi()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0);

        Assert.False(MetaCapiPayload.IsEventTimeAcceptable(MetaCapiPayload.ToUnix(now.AddHours(2)), now));
        Assert.True(MetaCapiPayload.IsEventTimeAcceptable(MetaCapiPayload.ToUnix(now.AddSeconds(30)), now));
        Assert.False(MetaCapiPayload.IsEventTimeAcceptable(0, now));
    }

    /// <summary>⚠️ <see cref="MetaCapiPayload.ToUnix"/> ofsetni QO'LDA biriktiradi: Toshkent
    /// 17:00 = UTC 12:00. Aks holda Docker (UTC) da vaqt 5 soatga siljib ketardi.</summary>
    [Fact]
    public void ToUnix_Toshkent_ofsetini_hisobga_oladi()
    {
        var tashkent = new DateTime(2026, 8, 20, 17, 0, 0);
        var expected = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal(expected, MetaCapiPayload.ToUnix(tashkent));
        Assert.Equal("2026-08-20T17:00:00", MetaCapiPayload.IsoFromUnix(expected));
    }

    /// <summary>🔴 Bir so'rovda 1000 tadan ko'p hodisa bo'lmaydi — ro'yxat bo'laklarga
    /// bo'linadi (chegaradan oshgan so'rov TO'LIQ rad etiladi).</summary>
    [Fact]
    public void Mingdan_kop_hodisa_bolaklarga_bolinadi()
    {
        var events = Enumerable.Range(0, 2500).Select(_ => Event(Now)).ToList();

        var chunks = MetaCapiPayload.Chunk(events);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(1000, chunks[0].Count);
        Assert.Equal(1000, chunks[1].Count);
        Assert.Equal(500, chunks[2].Count);
        Assert.Equal(2500, chunks.Sum(c => c.Count));
        Assert.All(chunks, c => Assert.InRange(c.Count, 1, MetaCapiPayload.MaxEventsPerRequest));
    }

    [Fact]
    public void Aynan_ming_ta_hodisa_bitta_bolak()
    {
        var events = Enumerable.Range(0, 1000).Select(_ => Event(Now)).ToList();

        Assert.Single(MetaCapiPayload.Chunk(events));
        Assert.Empty(MetaCapiPayload.Chunk(Array.Empty<MetaCapiEventInput>()));
    }
}
