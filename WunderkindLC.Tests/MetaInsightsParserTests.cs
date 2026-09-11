using System.Text.Json;
using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// META ADS INSIGHTS — sof funksiyalar testlari (HTTP ham, DB ham yo'q).
/// Rasmiy manba: <c>KENGAYTIRISH-PROMPT.md</c> §4.2–§4.4, §4.6.
///
/// <para>Eng qimmat ikki qoida:</para>
/// <list type="number">
///   <item><b>PUL ASSIMETRIYASI</b> — byudjet MINOR (butun son), <c>spend</c> esa MAJOR
///         (matn). Ularni chalkashtirish 100 barobar xato beradi.</item>
///   <item><b>LIDNI IKKI MARTA SANAMASLIK</b> — <c>lead</c> turi
///         <c>lead_grouped + fb_pixel_lead</c> ga teng, uchtasini qo'shish "lid narxi ikki
///         barobar arzon" degan yolg'on xulosa berardi.</item>
/// </list>
/// </summary>
public class MetaInsightsParserTests
{
    /* ═══════════════════════ MetaCurrency ═══════════════════════ */

    /// <summary>UZS — kasrli (tiyin) valyuta, ya'ni offset 2. Bu markazning ASOSIY valyutasi.</summary>
    [Fact]
    public void Valyuta_offseti_UZS_ikki()
    {
        Assert.Equal(2, MetaCurrency.OffsetOf("UZS"));
        Assert.Equal(2, MetaCurrency.OffsetOf("usd"));
        Assert.Equal(2, MetaCurrency.OffsetOf("EUR"));
    }

    /// <summary>"Zero-decimal" valyutalar — minor = major (kasr qismi yo'q).</summary>
    [Fact]
    public void Valyuta_offseti_JPY_nol()
    {
        Assert.Equal(0, MetaCurrency.OffsetOf("JPY"));
        Assert.Equal(0, MetaCurrency.OffsetOf("jpy"));
        Assert.Equal(0, MetaCurrency.OffsetOf("KRW"));
    }

    /// <summary>⚠️ Noma'lum yoki bo'sh kod → 2 (xavfsiz default), istisno EMAS: yangi valyuta
    /// chiqib qolsa sinxronizatsiya to'xtab qolmasin.</summary>
    [Fact]
    public void Valyuta_offseti_nomalum_ikki()
    {
        Assert.Equal(2, MetaCurrency.OffsetOf("XYZ"));
        Assert.Equal(2, MetaCurrency.OffsetOf(""));
        Assert.Equal(2, MetaCurrency.OffsetOf(null));
        Assert.Equal(2, MetaCurrency.OffsetOf("   "));
    }

    /// <summary>§4.2 dagi asosiy misol: <c>spend "312.45"</c> + offset 2 → 31245 minor.</summary>
    [Fact]
    public void Spend_matndan_minorga()
    {
        Assert.Equal(31245L, MetaCurrency.ParseSpendToMinor("312.45", 2));
        Assert.Equal(0L, MetaCurrency.ParseSpendToMinor("0", 2));
        Assert.Equal(100L, MetaCurrency.ParseSpendToMinor("1", 2));
    }

    /// <summary>Kasrsiz valyutada 312 → 312 (31200 EMAS) — offset 0 ni e'tiborsiz qoldirish
    /// sarfni 100 barobar shishirardi.</summary>
    [Fact]
    public void Spend_kasrsiz_valyutada()
    {
        Assert.Equal(312L, MetaCurrency.ParseSpendToMinor("312", 0));
        Assert.Equal(313L, MetaCurrency.ParseSpendToMinor("312.6", 0));
    }

    /// <summary>Bo'sh/buzuq qiymat → 0, istisno OTILMAYDI.</summary>
    [Fact]
    public void Spend_bosh_yoki_buzuq_nol()
    {
        Assert.Equal(0L, MetaCurrency.ParseSpendToMinor("", 2));
        Assert.Equal(0L, MetaCurrency.ParseSpendToMinor(null, 2));
        Assert.Equal(0L, MetaCurrency.ParseSpendToMinor("   ", 2));
        Assert.Equal(0L, MetaCurrency.ParseSpendToMinor("abc", 2));
    }

    /// <summary>
    /// Vergul va probel bilan yozilgan qiymat. Meta har doim invariant format yuboradi, lekin
    /// qo'lda kiritilgan/ko'chirilgan qiymat ham 100 barobar xato bermasligi kerak:
    /// probel — guruh ajratgichi, vergul esa oxirida 1–2 raqam qolsa KASR ajratgichi.
    /// </summary>
    [Fact]
    public void Spend_vergul_va_probel_bilan()
    {
        Assert.Equal(123456L, MetaCurrency.ParseSpendToMinor("1 234.56", 2));   // probel — guruh
        Assert.Equal(123456L, MetaCurrency.ParseSpendToMinor("1,234.56", 2));   // vergul — guruh
        Assert.Equal(31245L, MetaCurrency.ParseSpendToMinor("312,45", 2));      // vergul — kasr
        Assert.Equal(123400L, MetaCurrency.ParseSpendToMinor("1,234", 2));      // 3 raqam → guruh
        Assert.Equal(31245L, MetaCurrency.ParseSpendToMinor("  312.45  ", 2));
    }

    /// <summary>Yaxlitlash — yarmi YUQORIGA (bank yaxlitlashi kassaga tushunarsiz bo'lardi).</summary>
    [Fact]
    public void Spend_yaxlitlash_yuqoriga()
    {
        Assert.Equal(1L, MetaCurrency.ParseSpendToMinor("0.005", 2));
        Assert.Equal(31246L, MetaCurrency.ParseSpendToMinor("312.455", 2));
    }

    /// <summary>MINOR → MATN va teskarisi (round-trip): ikki funksiya bir-birining teskarisi
    /// bo'lishi shart, aks holda eksport qilingan son qayta yuklanganda o'zgarib ketardi.</summary>
    [Fact]
    public void Minor_va_major_teskari()
    {
        Assert.Equal("312.45", MetaCurrency.ToMajorString(31245, 2));
        Assert.Equal("312", MetaCurrency.ToMajorString(312, 0));
        Assert.Equal(31245L, MetaCurrency.ParseSpendToMinor(MetaCurrency.ToMajorString(31245, 2), 2));
    }

    /// <summary>UI matni: guruh ajratgichi — probel, kasr NOL bo'lsa umuman chizilmaydi
    /// (so'mda tiyin ko'rsatish hisobotni shovqin bilan to'ldirardi).</summary>
    [Fact]
    public void Format_odam_oqiydigan_matn()
    {
        Assert.Equal("1 200 000 UZS", MetaCurrency.FormatMinor(120_000_000, 2, "UZS"));
        Assert.Equal("312.45 USD", MetaCurrency.FormatMinor(31245, 2, "usd"));
        Assert.Equal("0", MetaCurrency.FormatMinor(0, 2));
        Assert.Equal("-312.45", MetaCurrency.FormatMinor(-31245, 2));
    }

    /* ═══════════════════════ actions massivi ═══════════════════════ */

    private const string ActionsRow = """
    {
      "ad_id": "111",
      "date_start": "2026-08-10",
      "actions": [
        { "action_type": "link_click", "value": "45" },
        { "action_type": "onsite_conversion.lead_grouped", "value": "4" },
        { "action_type": "offsite_conversion.fb_pixel_lead", "value": "3" }
      ]
    }
    """;

    /// <summary>⚠️ Qiymati 0 bo'lgan <c>action_type</c> massivda UMUMAN bo'lmaydi — topilmasa
    /// 0 qaytadi, istisno emas.</summary>
    [Fact]
    public void Actions_yoq_tur_nol_qaytaradi()
    {
        using var doc = JsonDocument.Parse(ActionsRow);
        var row = doc.RootElement;

        Assert.Equal(0, MetaInsightsParser.ActionValue(row, MetaInsightsParser.ActMsgStarted));
        Assert.Equal(0, MetaInsightsParser.ActionValue(row, "umuman_yoq_tur"));
        Assert.Equal(45, MetaInsightsParser.ActionValue(row, MetaInsightsParser.ActLinkClick));
    }

    /// <summary><c>actions</c> maydonining o'zi yo'q qator ham yiqilmaydi.</summary>
    [Fact]
    public void Actions_maydoni_yoq_bolsa_nol()
    {
        using var doc = JsonDocument.Parse("""{ "ad_id": "111", "date_start": "2026-08-10" }""");
        Assert.Equal(0, MetaInsightsParser.ActionValue(doc.RootElement, MetaInsightsParser.ActLeadGrouped));
    }

    /// <summary>
    /// ⚠️ <c>action_breakdowns</c> ishlatilganda BITTA <c>action_type</c> bir necha qator bo'lib
    /// keladi. Birinchi mosligini olish lidlarning bir qismini yo'qotardi — hammasi YIG'ILADI.
    /// </summary>
    [Fact]
    public void Actions_takrorlangan_tur_yigiladi()
    {
        const string json = """
        {
          "ad_id": "111",
          "actions": [
            { "action_type": "onsite_conversion.lead_grouped", "action_device": "desktop", "value": "2" },
            { "action_type": "onsite_conversion.lead_grouped", "action_device": "mobile_app", "value": "5" },
            { "action_type": "onsite_conversion.lead_grouped", "action_device": "mobile_web", "value": "1" }
          ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(8, MetaInsightsParser.ActionValue(doc.RootElement, MetaInsightsParser.ActLeadGrouped));
    }

    /// <summary>Qiymat SON bo'lib kelsa ham o'qiladi (Meta odatda matn yuboradi, lekin
    /// kafolat yo'q).</summary>
    [Fact]
    public void Actions_qiymati_son_bolsa_ham_oqiladi()
    {
        using var doc = JsonDocument.Parse("""{ "actions": [ { "action_type": "link_click", "value": 12 } ] }""");
        Assert.Equal(12, MetaInsightsParser.ActionValue(doc.RootElement, MetaInsightsParser.ActLinkClick));
    }

    /* ═══════════════════════ ParseRows ═══════════════════════ */

    /// <summary>Haqiqiy javob shakli: <c>breakdowns=publisher_platform</c> bilan bitta
    /// reklama-kun IKKI qator bo'lib keladi (Instagram + Facebook).</summary>
    private const string InsightsJson = """
    {
      "data": [
        {
          "campaign_id": "23851", "campaign_name": "IELTS avgust",
          "adset_id": "23852", "adset_name": "Toshkent 18-35",
          "ad_id": "23853", "ad_name": "Video A",
          "impressions": "12040", "reach": "9800", "clicks": "310",
          "inline_link_clicks": "245", "spend": "312.45",
          "publisher_platform": "instagram",
          "attribution_setting": "7d_click",
          "date_start": "2026-08-10", "date_stop": "2026-08-10",
          "actions": [
            { "action_type": "lead", "value": "7" },
            { "action_type": "onsite_conversion.lead_grouped", "value": "4" },
            { "action_type": "offsite_conversion.fb_pixel_lead", "value": "3" },
            { "action_type": "onsite_conversion.messaging_conversation_started_7d", "value": "2" },
            { "action_type": "link_click", "value": "245" }
          ]
        },
        {
          "campaign_id": "23851", "adset_id": "23852", "ad_id": "23853",
          "impressions": "5000", "reach": "4100", "clicks": "90",
          "inline_link_clicks": "70", "spend": "80.00",
          "publisher_platform": "facebook",
          "date_start": "2026-08-10", "date_stop": "2026-08-10",
          "actions": [
            { "action_type": "onsite_conversion.lead_grouped", "value": "1" }
          ]
        }
      ],
      "paging": { "next": "https://graph.facebook.com/v23.0/act_1/insights?after=CURSOR" }
    }
    """;

    [Fact]
    public void ParseRows_toliq_javobni_oqiydi()
    {
        var rows = MetaInsightsParser.ParseRows(InsightsJson, 2);

        Assert.Equal(2, rows.Count);

        var ig = rows[0];
        Assert.Equal(MetaInsightsParser.LevelAd, ig.Level);
        Assert.Equal("23853", ig.ExternalId);          // ⚠️ eng past daraja — ad_id
        Assert.Equal("2026-08-10", ig.StatDate);
        Assert.Equal("instagram", ig.Platform);
        Assert.Equal(12040L, ig.Impressions);
        Assert.Equal(9800L, ig.Reach);
        Assert.Equal(310L, ig.Clicks);
        Assert.Equal(245L, ig.LinkClicks);
        Assert.Equal(31245L, ig.SpendMinor);           // "312.45" → MINOR
        Assert.Equal("7d_click", ig.AttributionSetting);
        Assert.NotEqual("", ig.ActionsJson);           // xom massiv saqlanadi

        Assert.Equal("facebook", rows[1].Platform);
        Assert.Equal(8000L, rows[1].SpendMinor);
    }

    /// <summary>
    /// 🔴 ENG MUHIM: <c>lead</c> turi HISOBGA OLINMAYDI.
    /// Javobda <c>lead=7</c> bor, lekin biz 4 + 3 ni ALOHIDA saqlaymiz. Uchtasini qo'shsak
    /// 14 chiqardi — lidlar ikki barobar ko'p, lid narxi ikki barobar arzon ko'rinardi.
    /// </summary>
    [Fact]
    public void ParseRows_lead_turini_hisobga_olmaydi()
    {
        var row = MetaInsightsParser.ParseRows(InsightsJson, 2)[0];

        Assert.Equal(4, row.LeadsOnsite);
        Assert.Equal(3, row.LeadsPixel);
        Assert.Equal(2, row.MsgStarted);

        // Jami — aynan 7 (4+3), "lead" qatoridagi 7 qo'shimcha ravishda QO'SHILMAGAN.
        Assert.Equal(7, row.LeadsOnsite + row.LeadsPixel);
    }

    /// <summary>Buzuq/bo'sh JSON → bo'sh ro'yxat, istisno OTILMAYDI.</summary>
    [Fact]
    public void ParseRows_buzuq_json_bosh_royxat()
    {
        Assert.Empty(MetaInsightsParser.ParseRows("", 2));
        Assert.Empty(MetaInsightsParser.ParseRows("   ", 2));
        Assert.Empty(MetaInsightsParser.ParseRows("{buzuq", 2));
        Assert.Empty(MetaInsightsParser.ParseRows("{}", 2));
        Assert.Empty(MetaInsightsParser.ParseRows("""{ "data": "matn" }""", 2));
        Assert.Empty(MetaInsightsParser.ParseRows("""{ "error": { "code": 190 } }""", 2));
    }

    /// <summary>⚠️ Unikal kalitning bir qismi yo'q qator TASHLANADI — aks holda upsert
    /// dublikat yaratardi.</summary>
    [Fact]
    public void ParseRows_kalitsiz_qator_tashlanadi()
    {
        const string json = """
        {
          "data": [
            { "impressions": "10", "date_start": "2026-08-10" },
            { "ad_id": "111", "impressions": "10" },
            { "ad_id": "222", "date_start": "2026-08-10", "impressions": "10" }
          ]
        }
        """;

        var rows = MetaInsightsParser.ParseRows(json, 2);
        var one = Assert.Single(rows);
        Assert.Equal("222", one.ExternalId);
    }

    /// <summary>Platforma breakdown'i so'ralmagan bo'lsa qator "all" bo'ladi (bazadagi unikal
    /// kalit bo'sh satr bilan ishlamasdi).</summary>
    [Fact]
    public void ParseRows_platforma_korsatilmasa_all()
    {
        const string json = """
        { "data": [ { "campaign_id": "9", "date_start": "2026-08-10", "spend": "1.00" } ] }
        """;

        var one = Assert.Single(MetaInsightsParser.ParseRows(json, 2));
        Assert.Equal(MetaInsightsParser.PlatformAll, one.Platform);
        Assert.Equal(MetaInsightsParser.LevelCampaign, one.Level);
        Assert.Equal("9", one.ExternalId);
    }

    /// <summary>Bazada saqlangan xom <c>actions</c> dan qayta hisoblash (yangi metrika kerak
    /// bo'lganda qayta sinxronizatsiya qilmaslik uchun).</summary>
    [Fact]
    public void ActionsJson_dan_qayta_hisoblanadi()
    {
        var row = MetaInsightsParser.ParseRows(InsightsJson, 2)[0];

        Assert.Equal(245, MetaInsightsParser.ActionValueFromJson(row.ActionsJson, MetaInsightsParser.ActLinkClick));
        Assert.Equal(0, MetaInsightsParser.ActionValueFromJson("", MetaInsightsParser.ActLinkClick));
        Assert.Equal(0, MetaInsightsParser.ActionValueFromJson("{buzuq", MetaInsightsParser.ActLinkClick));
    }

    /* ═══════════════════════ Sahifalash ═══════════════════════ */

    /// <summary>⚠️ <c>paging.next</c> ichida token bor — faqat <c>https://</c> manzilga
    /// ergashiladi, aks holda token begona xostga ketardi.</summary>
    [Fact]
    public void NextPageUrl_faqat_https_manzil()
    {
        Assert.Equal("https://graph.facebook.com/v23.0/act_1/insights?after=CURSOR",
                     MetaInsightsParser.NextPageUrl(InsightsJson));

        Assert.Equal("", MetaInsightsParser.NextPageUrl("""{ "data": [] }"""));
        Assert.Equal("", MetaInsightsParser.NextPageUrl("""{ "paging": { "cursors": {} } }"""));
        Assert.Equal("", MetaInsightsParser.NextPageUrl("""{ "paging": { "next": "http://evil.example/x" } }"""));
        Assert.Equal("", MetaInsightsParser.NextPageUrl("{buzuq"));
        Assert.Equal("", MetaInsightsParser.NextPageUrl(""));
    }

    /* ═══════════════════════ Iyerarxiya ═══════════════════════ */

    /// <summary>⚠️ Byudjet MINOR unit bo'lib KELADI — unga <c>MetaCurrency</c> qo'llanmaydi
    /// (spend bilan bo'lgan assimetriya, §4.2).</summary>
    [Fact]
    public void ParseEntities_kampaniya()
    {
        const string json = """
        {
          "data": [
            {
              "id": "23851", "name": "IELTS avgust", "status": "ACTIVE",
              "effective_status": "ACTIVE", "objective": "OUTCOME_LEADS",
              "daily_budget": "5000", "lifetime_budget": "0",
              "start_time": "2026-08-01T00:00:00+0500", "stop_time": "2026-08-31T23:59:59+0500"
            }
          ]
        }
        """;

        var one = Assert.Single(MetaInsightsParser.ParseEntities(json, MetaInsightsParser.LevelCampaign));
        Assert.Equal(MetaInsightsParser.LevelCampaign, one.Level);
        Assert.Equal("23851", one.ExternalId);
        Assert.Equal("", one.ParentId);                 // kampaniyaning otasi yo'q
        Assert.Equal("OUTCOME_LEADS", one.Objective);
        Assert.Equal(5000L, one.DailyBudgetMinor);      // 50.00 — MINOR holicha
        Assert.NotEqual("", one.StartTime);
    }

    /// <summary>⚠️ Ad set'da tugash vaqti <c>end_time</c> deb ataladi (kampaniyada —
    /// <c>stop_time</c>); ikkalasi ham o'qilishi kerak.</summary>
    [Fact]
    public void ParseEntities_adset_end_time_ni_oqiydi()
    {
        const string json = """
        {
          "data": [
            {
              "id": "23852", "name": "Toshkent 18-35", "campaign_id": "23851",
              "status": "ACTIVE", "effective_status": "ACTIVE",
              "daily_budget": "2500", "end_time": "2026-08-31T23:59:59+0500"
            }
          ]
        }
        """;

        var one = Assert.Single(MetaInsightsParser.ParseEntities(json, MetaInsightsParser.LevelAdset));
        Assert.Equal("23851", one.ParentId);
        Assert.Equal(2500L, one.DailyBudgetMinor);
        Assert.NotEqual("", one.StopTime);
    }

    /// <summary>Reklamada ota — ad set, va E3 (reklama izohlari) uchun
    /// <c>effective_object_story_id</c> saqlanadi.</summary>
    [Fact]
    public void ParseEntities_reklama_creative_story_id()
    {
        const string json = """
        {
          "data": [
            {
              "id": "23853", "name": "Video A", "adset_id": "23852", "campaign_id": "23851",
              "status": "ACTIVE", "effective_status": "ACTIVE",
              "creative": { "id": "777", "effective_object_story_id": "1122_3344" }
            },
            {
              "id": "23854", "name": "Creative'siz", "adset_id": "23852"
            }
          ]
        }
        """;

        var rows = MetaInsightsParser.ParseEntities(json, MetaInsightsParser.LevelAd);
        Assert.Equal(2, rows.Count);
        Assert.Equal("23852", rows[0].ParentId);
        Assert.Equal("1122_3344", rows[0].CreativeStoryId);
        Assert.Equal("", rows[1].CreativeStoryId);     // creative yo'q — reklama baribir qoladi
    }

    [Fact]
    public void ParseEntities_buzuq_json_bosh_royxat()
    {
        Assert.Empty(MetaInsightsParser.ParseEntities("{buzuq", MetaInsightsParser.LevelAd));
        Assert.Empty(MetaInsightsParser.ParseEntities("", MetaInsightsParser.LevelAd));
        Assert.Empty(MetaInsightsParser.ParseEntities("""{ "data": [ { "name": "id'siz" } ] }""",
                                                      MetaInsightsParser.LevelAd));
    }

    /* ═══════════════════════ Akkaunt ═══════════════════════ */

    /// <summary>Javobda <c>currency_offset</c> BO'LMASA — offset BIZNING jadvaldan
    /// (<c>MetaCurrency.OffsetOf</c>) va manba <c>"jadval"</c>. Bu — Meta maydonni bermaydigan
    /// (yoki so'rovni rad etadigan) holatdagi xatti-harakat, ya'ni eski, tekshirilgan yo'l.</summary>
    [Fact]
    public void ParseAccount_offset_valyutadan_hisoblanadi()
    {
        const string json = """
        {
          "id": "act_1234567890", "name": "WunderkindLC Ads",
          "currency": "UZS", "timezone_name": "Asia/Tashkent", "account_status": 1
        }
        """;

        var (info, status) = MetaInsightsParser.ParseAccount(json);

        Assert.NotNull(info);
        Assert.Equal("act_1234567890", info!.Id);
        Assert.Equal("UZS", info.Currency);
        Assert.Equal(2, info.CurrencyOffset);
        Assert.Equal(MetaOffsetSource.Table, info.OffsetSource);
        Assert.Equal("Asia/Tashkent", info.TimezoneName);
        Assert.Equal(1, status);
    }

    /// <summary>
    /// ⚠️ Meta <c>currency_offset</c> QAYTARSA — HAQIQAT MANBAI o'sha, bizning jadval emas.
    /// Bu yerda ataylab jadvaldan FARQ qiladigan holat olingan (USD → jadvalda 2, Meta 0):
    /// jadval "g'olib" bo'lib qolsa, ish vaqtida aniqlashning ma'nosi qolmasdi.
    /// </summary>
    [Fact]
    public void ParseAccount_Meta_bergan_offset_ishlatiladi()
    {
        const string json = """
        { "id": "act_9", "currency": "USD", "currency_offset": 0, "account_status": 1 }
        """;

        var (info, _) = MetaInsightsParser.ParseAccount(json);

        Assert.NotNull(info);
        Assert.Equal(0, info!.CurrencyOffset);
        Assert.Equal(MetaOffsetSource.Meta, info.OffsetSource);

        // Meta uni MATN qilib yuborsa ham o'qiladi (metrikalarni shunday yuboradi).
        var (asText, _) = MetaInsightsParser.ParseAccount(
            """{ "id": "act_9", "currency": "JPY", "currency_offset": "3" }""");
        Assert.Equal(3, asText!.CurrencyOffset);
        Assert.Equal(MetaOffsetSource.Meta, asText.OffsetSource);
    }

    /// <summary>
    /// 🔴 MANTIQSIZ qiymat jimgina QABUL QILINMAYDI — jadvalga qaytiladi.
    ///
    /// <para>Eskirgan <c>Currency</c> tugunida <c>offset</c> KO'PAYTUVCHI edi (<c>100</c>),
    /// bizga esa kasr xonalari soni kerak. <c>100</c> ni ko'r-ko'rona ishlatish sarfni
    /// tasavvur qilib bo'lmaydigan darajada buzardi. Buzuq matn ("abc") ham 0 deb
    /// o'qilmasligi kerak: 0 — HAQIQIY offset (JPY).</para>
    /// </summary>
    [Fact]
    public void ParseAccount_mantiqsiz_offset_jadvalga_qaytadi()
    {
        foreach (var raw in new[] { "100", "-1", "\"abc\"", "\"\"", "null", "2.5", "true" })
        {
            var (info, _) = MetaInsightsParser.ParseAccount(
                "{ \"id\": \"act_9\", \"currency\": \"UZS\", \"currency_offset\": " + raw + " }");

            Assert.NotNull(info);
            Assert.Equal(2, info!.CurrencyOffset);
            Assert.Equal(MetaOffsetSource.Table, info.OffsetSource);
        }
    }

    [Fact]
    public void ParseAccount_buzuq_yoki_xato_javob_null()
    {
        Assert.Null(MetaInsightsParser.ParseAccount("{buzuq").Info);
        Assert.Null(MetaInsightsParser.ParseAccount("").Info);
        Assert.Null(MetaInsightsParser.ParseAccount("""{ "error": { "code": 190 } }""").Info);
    }

    /// <summary>⚠️ <c>act_</c> prefiksi: admin id'ni ikkala ko'rinishda ham ko'chirishi mumkin.</summary>
    [Fact]
    public void Akkaunt_id_normallashtiriladi()
    {
        Assert.Equal("act_1234567890", MetaInsightsParser.NormalizeAccountId("1234567890"));
        Assert.Equal("act_1234567890", MetaInsightsParser.NormalizeAccountId("act_1234567890"));
        Assert.Equal("act_1234567890", MetaInsightsParser.NormalizeAccountId("  ACT_1234567890 "));
        Assert.Equal("", MetaInsightsParser.NormalizeAccountId(""));
        Assert.Equal("", MetaInsightsParser.NormalizeAccountId(null));
        Assert.Equal("", MetaInsightsParser.NormalizeAccountId("act_abc"));
        Assert.Equal("", MetaInsightsParser.NormalizeAccountId("act_"));
    }

    /* ═══════════════════════ Rate limit (§4.6) ═══════════════════════ */

    /// <summary>Kvota sarlavhalari o'qiladi: <c>estimated_time_to_regain_access</c> —
    /// DAQIQA, ya'ni shuncha kutish kerak.</summary>
    [Fact]
    public void Throttle_sarlavhalari_oqiladi()
    {
        const string insights = """{"app_id_util_pct":100,"acc_id_util_pct":10,"ads_api_access_tier":"standard_access"}""";
        const string buc = """{"1122":[{"type":"ads_insights","call_count":42,"total_cputime":10,"total_time":15,"estimated_time_to_regain_access":7}]}""";

        var rl = MetaInsightsParser.ParseThrottle(insights, buc);

        Assert.NotNull(rl);
        Assert.Equal(100, rl!.AppUtilPct);
        Assert.Equal(10, rl.AccountUtilPct);
        Assert.Equal("standard_access", rl.Tier);
        Assert.Equal(42, rl.CallCountPct);
        Assert.Equal(7, rl.RegainMinutes);

        Assert.Contains("7", MetaInsightsParser.ThrottleSummary(rl));
    }

    /// <summary>⚠️ BUC sarlavhasidagi kalit — BUSINESS ID, oldindan noma'lum; boshqa
    /// <c>type</c> lar (masalan <c>ads_management</c>) bu so'rovga tegishli emas.</summary>
    [Fact]
    public void Throttle_faqat_ads_insights_turini_oladi()
    {
        const string buc = """{"999":[{"type":"ads_management","call_count":90,"estimated_time_to_regain_access":30},{"type":"ads_insights","call_count":5,"estimated_time_to_regain_access":0}]}""";

        var rl = MetaInsightsParser.ParseThrottle(null, buc);

        Assert.NotNull(rl);
        Assert.Equal(5, rl!.CallCountPct);
        Assert.Equal(0, rl.RegainMinutes);
    }

    /// <summary>Sarlavha yo'q yoki buzuq bo'lsa — <c>null</c>, istisno emas (kvota ma'lumoti
    /// yo'qligi so'rovni yiqitmaydi).</summary>
    [Fact]
    public void Throttle_sarlavhasiz_null()
    {
        Assert.Null(MetaInsightsParser.ParseThrottle(null, null));
        Assert.Null(MetaInsightsParser.ParseThrottle("", ""));
        Assert.Null(MetaInsightsParser.ParseThrottle("{buzuq", "ham buzuq"));
        Assert.Equal("", MetaInsightsParser.ThrottleSummary(null));
    }
}
