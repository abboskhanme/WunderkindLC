namespace WunderkindLC.Application.Services;

/// <summary>
/// ULANISH DIAGNOSTIKASI natijasidagi NOSOZLIK TURI.
///
/// <para><b>Nega enum, xato matni emas:</b> Meta tomonidagi nosozliklar adminga bir xil
/// ko'rinadi ("hech narsa kelmayapti"), sabablari esa har xil va HAR BIRI BOSHQA amalni
/// talab qiladi. Tur aniqlanmasa maslahat ham berib bo'lmaydi.</para>
/// </summary>
public enum IgDiagFault
{
    /// <summary>Xato yo'q.</summary>
    None,
    /// <summary>Sozlanmagan — id yoki token kiritilmagan (Meta'gacha bormadi).</summary>
    NotConfigured,
    /// <summary>Token muddati tugagan yoki bekor qilingan (Meta kodi 190).</summary>
    Token,
    /// <summary>Ruxsat yetishmaydi yoki App Review'dan o'tmagan (10 / 200 / 299).</summary>
    Permission,
    /// <summary>Id noto'g'ri yoki so'rov parametri xato (100).</summary>
    BadId,
    /// <summary>So'rov chegarasi — VAQTINCHALIK (4 / 17 / 32 / 613 / 80000 / 80004).</summary>
    RateLimit,
    /// <summary>Tarmoq: Meta javob bermadi, timeout yoki chiqish yo'q.</summary>
    Network,
    /// <summary>Tanib bo'lmadi.</summary>
    Unknown,
}

/// <summary>
/// "META BILAN ALOQANI TEKSHIRISH" tugmasining SOF QOIDALARI — modul kalitlari, yorliqlari,
/// "nima yetishmayapti" matnlari va <b>xato → NIMA QILISH kerak</b> xaritasi.
///
/// <para><b>Nega alohida sinf (controllerda emas):</b> loyihadagi odat — qaror qoidasi
/// Application'da sof funksiya bo'lib turadi va test bilan qoplanadi
/// (<c>ContactService</c>, <c>BookSalesService</c>, <c>PermissionRules</c> bilan bir xil).
/// Test loyihasi Server'ga bog'lanmagan, ya'ni controller ichidagi mantiq umuman
/// testlanmasdi.</para>
///
/// <para><b>⚠️ Bu yerda TARMOQ YO'Q.</b> HTTP so'rovlarni mavjud mijozlar
/// (<c>InstagramApi</c>, <c>MetaAdsApi</c>, <c>MetaInsightsApi</c>,
/// <c>InstagramPublishApi</c>) qiladi — ular allaqachon Meta xato kodini o'zbekcha matnga
/// aylantiradi. Bu sinf faqat SHU MATNNI (yoki kodni) "nima qilish kerak" maslahatiga
/// aylantiradi.</para>
/// </summary>
public static class IgDiagnostics
{
    /* ═════════════════════════ Modul kalitlari ═════════════════════════ */

    /// <summary>Instagram akkaunt — izoh/DM (Instagram Login yo'li).</summary>
    public const string KeyAccount = "account";
    /// <summary>Reklama lidlari (Lead Ads, Facebook Page tokeni).</summary>
    public const string KeyAdLeads = "adLeads";
    /// <summary>Reklama statistikasi (Ads Insights).</summary>
    public const string KeyAdsStats = "adsStats";
    /// <summary>Kontent joylash (Content Publishing).</summary>
    public const string KeyContent = "content";
    /// <summary>CAPI (Conversions API) — hodisa YUBORILMAYDI, faqat sozlama.</summary>
    public const string KeyCapi = "capi";

    /// <summary>Tekshiruv natijasidagi TARTIB — ekranda ham aynan shu tartib.
    /// (Avval "aloqa bormi" (akkaunt), keyin unga tayanadigan modullar.)</summary>
    public static readonly string[] All =
        [KeyAccount, KeyAdLeads, KeyAdsStats, KeyContent, KeyCapi];

    /// <summary>Foydalanuvchi ko'radigan nom. Noma'lum kalit uchun kalitning O'ZI qaytadi —
    /// yangi modul qo'shilib, yorlig'i unutilsa qator JIMGINA yo'qolmasin.</summary>
    public static string Label(string key) => key switch
    {
        KeyAccount => "Instagram akkaunt (izoh va DM)",
        KeyAdLeads => "Reklama lidlari",
        KeyAdsStats => "Reklama statistikasi",
        KeyContent => "Kontent joylash",
        KeyCapi => "CAPI (lid sifatini qaytarish)",
        _ => key,
    };

    /* ═════════════════════════ Standart matnlar ═════════════════════════ */

    /// <summary>Modul bayrog'i o'chiq — Meta'ga umuman so'rov ketmaydi.</summary>
    public const string DisabledText = "Modul o'chirilgan";

    /// <summary>Modul o'chiq bo'lgandagi maslahat.</summary>
    public const string DisabledHint =
        "Tekshirish uchun avval shu moduldagi bayroqni yoqing va sozlamalarni saqlang.";

    /// <summary>
    /// 🔴 CAPI qatorining matni. ATAYIN "sinalmadi" deb OCHIQ yoziladi.
    ///
    /// <para>CAPI'ni haqiqatan sinash uchun Meta'ga HODISA yuborish kerak bo'lardi, hodisa esa
    /// Events Manager'ga tushib, konversiya statistikasini va reklama optimallashtirishini
    /// buzadi (test hodisasini keyin "o'chirib" bo'lmaydi). Ya'ni diagnostika tugmasi
    /// ma'lumotni O'ZGARTIRIB yuborardi.</para>
    ///
    /// <para>Shuning uchun bu yerda faqat sozlama to'liqligi ko'riladi va yashil belgi
    /// QO'YILMAYDI: sinalmagan holatni "ishlayapti" deb ko'rsatish — eng yomon variant.</para>
    /// </summary>
    public const string CapiNotProbedText =
        "Sozlama to'liq — lekin Meta bilan aloqa SINALMADI (diagnostika hodisa yubormaydi).";

    /// <summary>CAPI sozlamasi to'liq bo'lgandagi maslahat.</summary>
    public const string CapiNotProbedHint =
        "Haqiqiy tekshiruv — Events Manager → «Test Events»: navbatdan bitta hodisa yuborilgach, "
        + "u o'sha ekranda ko'rinishi kerak.";

    /* ═════════════════════════ "Nima yetishmayapti" ═════════════════════════ */

    /// <summary>
    /// Sozlanmagan modul uchun matn: AYNAN nima kiritilmagani.
    ///
    /// <para>⚠️ "Sozlanmagan" deb qisqa yozish yetmaydi — admin uchta token bilan ishlaydi
    /// (Instagram tokeni, Page tokeni, reklama akkaunti tokeni) va qaysinisi tushib qolganini
    /// bilmasa, noto'g'risini qayta kiritib yurardi.</para>
    /// </summary>
    /// <returns>Hammasi joyida bo'lsa — bo'sh satr.</returns>
    public static string MissingText(string key, bool hasId, bool hasToken)
    {
        if (hasId && hasToken) return "";

        var (idName, tokenName) = key switch
        {
            KeyAccount or KeyContent => ("Instagram akkaunt", "kirish tokeni"),
            KeyAdLeads => ("Facebook Page ID", "Page Access Token"),
            KeyAdsStats => ("reklama akkaunti ID", "reklama akkaunti tokeni"),
            KeyCapi => ("Dataset (piksel) ID", "CAPI tokeni"),
            _ => ("id", "token"),
        };

        if (!hasId && !hasToken) return $"Sozlanmagan: {idName} ham, {tokenName} ham kiritilmagan.";
        return !hasId
            ? $"Sozlanmagan: {idName} kiritilmagan."
            : $"Sozlanmagan: {tokenName} kiritilmagan.";
    }

    /// <summary>Sozlanmagan modul uchun maslahat — QAYSI kartaga borish kerak.</summary>
    public static string MissingHint(string key) => key switch
    {
        KeyAccount => "Sozlamalardagi «Instagram akkaunt» kartasidan akkauntni ulang.",
        KeyContent => "«Instagram akkaunt» kartasidan akkauntni ulang — kontent joylash o'sha "
                      + "token bilan ishlaydi.",
        KeyAdLeads => "«Reklama lidlari» kartasida Page ID va Page Access Token'ni saqlang.",
        KeyAdsStats => "«Reklama statistikasi» kartasida akkaunt ID (act_…) va tokenni saqlang.",
        KeyCapi => "«CAPI» kartasida Dataset ID va tokenni saqlang.",
        _ => "Sozlamalar bo'limida kerakli maydonlarni to'ldiring.",
    };

    /* ═════════════════════════ Xato → tur ═════════════════════════ */

    /// <summary>
    /// Meta xatosini TURGA ajratadi.
    ///
    /// <para><b>Avval KOD, keyin matn.</b> Kod — barqaror shartnoma, matn esa yo'q: mijozdagi
    /// bitta so'z tahrirlansa matnga tayangan mantiq JIMGINA buzilardi
    /// (<c>MetaInsightsApi.LastErrorCode</c> izohidagi bir xil sabab). Lekin kod har doim ham
    /// bo'lmaydi — to'rt mijozdan faqat bittasi uni tashqariga chiqaradi, tarmoq uzilishida esa
    /// kod umuman yo'q (0). Shuning uchun matn ZAXIRA sifatida qoladi.</para>
    /// </summary>
    /// <param name="message">Mijoz qaytargan o'zbekcha xato matni.</param>
    /// <param name="metaCode">Meta <c>error.code</c> (ma'lum bo'lsa), aks holda 0.</param>
    public static IgDiagFault Classify(string message, int metaCode = 0)
    {
        switch (metaCode)
        {
            case 190: return IgDiagFault.Token;
            case 10 or 200 or 299: return IgDiagFault.Permission;
            case 100: return IgDiagFault.BadId;
            case 4 or 17 or 32 or 613 or 2 or 80000 or 80004: return IgDiagFault.RateLimit;
        }

        var t = Normalize(message);
        if (t.Length == 0) return IgDiagFault.Unknown;

        // ⚠️ Tarmoq TOKENDAN OLDIN: "so'rov bekor qilindi" (uzilish) va "bekor qilingan"
        // (token bekor qilingan) bir-biriga juda o'xshaydi.
        if (t.Contains("tarmoq xatosi") || t.Contains("javob bermadi")
            || t.Contains("vaqt tugadi") || t.Contains("bekor qilindi"))
            return IgDiagFault.Network;

        if (t.Contains("kiritilmagan") || t.Contains("bo'sh.")) return IgDiagFault.NotConfigured;
        if (t.Contains("ruxsat yetishmaydi")) return IgDiagFault.Permission;
        if (t.Contains("muddati tugagan") || t.Contains("yaroqsiz") || t.Contains("bekor qilingan"))
            return IgDiagFault.Token;
        if (t.Contains("chegara") || t.Contains("rate limit")) return IgDiagFault.RateLimit;
        if (t.Contains("noto'g'ri")) return IgDiagFault.BadId;

        return IgDiagFault.Unknown;
    }

    /* ═════════════════════════ Tur → maslahat ═════════════════════════ */

    /// <summary>
    /// "NIMA QILISH KERAK" — modul + nosozlik turi bo'yicha.
    ///
    /// <para>⚠️ Maslahat MODULGA bog'liq: uchala token har xil joydan kiritiladi va har xil
    /// ruxsat talab qiladi. "Tokenni yangilang" deb umumiy yozilsa admin noto'g'risini
    /// yangilab, muammo qolib ketardi.</para>
    /// </summary>
    public static string Hint(string key, IgDiagFault fault) => fault switch
    {
        IgDiagFault.None => "",

        IgDiagFault.NotConfigured => MissingHint(key),

        IgDiagFault.Token => key switch
        {
            KeyAccount or KeyContent =>
                "Token muddati tugagan yoki bekor qilingan — «Instagram akkaunt» kartasidagi "
                + "«Tokenni yangilash» tugmasini bosing, yordam bermasa akkauntni qayta ulang.",
            KeyAdLeads =>
                "Page Access Token muddati tugagan — «Reklama lidlari» kartasida yangisini "
                + "kiriting. Muddatsiz ishlashi uchun System User tokeni tavsiya etiladi.",
            KeyAdsStats =>
                "Reklama akkaunti tokeni muddati tugagan — «Reklama statistikasi» kartasida "
                + "yangisini kiriting (System User tokeni tavsiya etiladi).",
            _ => "Token muddati tugagan — Sozlamalarda yangisini kiriting.",
        },

        // ⚠️ App Review ALOHIDA sabab emas, chunki Meta uni AYNAN shu kod bilan qaytaradi:
        // ruxsat "berilgan", lekin ilova ko'rikdan o'tmagani uchun ishlamaydi. Shu sabab
        // ikkala ehtimol ham matnda aytiladi.
        IgDiagFault.Permission => key switch
        {
            KeyAccount =>
                "Ruxsat yetishmaydi — Meta ilovasida izoh va xabar ruxsatlari borligini "
                + "tekshiring, so'ng akkauntni qayta ulang (ruxsat aynan qayta ulashda so'raladi). "
                + "Ilova App Review'dan o'tmagan bo'lsa ham xato shunday ko'rinadi.",
            KeyContent =>
                "Ruxsat yetishmaydi — `instagram_business_content_publish` ruxsati yo'q. "
                + "Akkauntni qayta ulab, kontent joylash ruxsatini bering; ilova hali "
                + "App Review'dan o'tmagan bo'lishi ham mumkin.",
            KeyAdLeads =>
                "Ruxsat yetishmaydi — ilovada `leads_retrieval` ruxsati va token egasining "
                + "sahifa ustidan huquqi borligini tekshiring (App Review talab qilinadi).",
            KeyAdsStats =>
                "Ruxsat yetishmaydi — ilovada `ads_read` ruxsati borligini va token egasi shu "
                + "reklama akkauntiga kira olishini tekshiring.",
            _ => "Ruxsat yetishmaydi — Meta ilovasidagi ruxsatlar ro'yxatini tekshiring.",
        },

        IgDiagFault.BadId => key switch
        {
            KeyAccount or KeyContent =>
                "Instagram akkaunt id'si noto'g'ri — akkauntni uzib, qaytadan ulang.",
            KeyAdLeads =>
                "Page ID noto'g'ri — Meta Business Suite → Sahifa → «Ma'lumot»dagi id bilan "
                + "solishtiring (token ham AYNAN shu sahifaniki bo'lishi kerak).",
            KeyAdsStats =>
                "Reklama akkaunti ID noto'g'ri — u `act_1234567890` ko'rinishida bo'lishi kerak.",
            _ => "Id noto'g'ri — Sozlamalardagi qiymatni tekshiring.",
        },

        IgDiagFault.RateLimit =>
            "Meta so'rov chegarasiga yetildi. Bu VAQTINCHALIK — bir necha daqiqadan keyin "
            + "qayta tekshiring; modul o'zi ham keyinroq avtomatik urinadi.",

        IgDiagFault.Network =>
            "Meta javob bermadi — serverdan tashqi tarmoqqa chiqish bor-yo'qligini tekshiring "
            + "va qayta urinib ko'ring.",

        _ => "Sabab aniqlanmadi. Xato matnini saqlang; takrorlansa tokenni qayta kiritib ko'ring.",
    };

    /// <summary>Xato matnidan TO'G'RIDAN-TO'G'RI maslahat (turni o'zi aniqlaydi).</summary>
    public static string HintFor(string key, string message, int metaCode = 0) =>
        Hint(key, Classify(message, metaCode));

    /* ═════════════════════════ Ichki ═════════════════════════ */

    /// <summary>
    /// Solishtirish uchun matnni bir ko'rinishga keltiradi: kichik harf + apostroflar bir xil.
    ///
    /// <para>⚠️ Apostrof: matnlar turli fayllarda `'`, `ʻ`, `’` bilan yozilgan bo'lishi mumkin
    /// va "noto‘g‘ri" bilan "noto'g'ri" boshqa satr bo'lib qolardi
    /// (<c>ContactService.TopWords</c> dagi bir xil sabab).</para>
    /// </summary>
    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var buf = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
            buf.Append(ch is 'ʻ' or 'ʼ' or '‘' or '’' or '`' or '´' ? '\'' : ch);
        return buf.ToString();
    }
}
