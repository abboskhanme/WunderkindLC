using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CAPI (Conversions API) uchun PII NORMALLASHTIRISH va HASHLASH — sof funksiyalar.
///
/// <para><b>Qoida: SHA-256, natija HEX va KICHIK harfda.</b> Meta hashni o'z bazasidagi
/// qiymat bilan bayt-ma-bayt solishtiradi, ya'ni normallashtirish bir belgi bilan farq qilsa
/// ham moslik (match rate) 0 bo'ladi va nosozlik <b>jimgina</b> yuz beradi: so'rov 200 OK
/// qaytadi, lekin hodisa hech kimga bog'lanmaydi. Aynan shuning uchun normallashtirish
/// KODNING BITTA JOYIDA turadi va testlar bilan qulflangan.</para>
///
/// <para>🔴 <b>HASHLANMAYDIGAN maydonlar</b> — bularni hashlab yuborish hodisani buzadi
/// (Meta ularni xom holda kutadi):</para>
/// <list type="bullet">
///   <item><c>lead_id</c> — Meta'ning lid ID'si, RAQAM sifatida yuboriladi;</item>
///   <item><c>client_ip_address</c>, <c>client_user_agent</c>;</item>
///   <item><c>fbc</c>, <c>fbp</c> (brauzer cookie'lari);</item>
///   <item><c>page_id</c>, <c>page_scoped_user_id</c>;</item>
///   <item><c>ig_sid</c> (va <c>instagram_business_account_id</c>, <c>ctwa_clid</c>).</item>
/// </list>
///
/// <para>⚠️ <b>Bo'sh/yaroqsiz kirish → BO'SH SATR</b> (istisno OTILMAYDI). Chaqiruvchi bo'sh
/// natijani ko'rib maydonni payloadga <b>umuman qo'shmaydi</b>: yaroqsiz qiymatning hashi
/// hech qachon mos kelmaydi, lekin Meta'ning "match rate" ko'rsatkichini pasaytiradi va
/// hisobotda "sifatsiz integratsiya" bo'lib ko'rinadi.</para>
/// </summary>
public static class MetaCapiHash
{
    /// <summary>O'zbekiston mamlakat kodi — 9 xonali mahalliy raqamga qo'shiladi.</summary>
    private const string UzCountryCode = "998";

    /// <summary>Mamlakat kodi bilan birga eng qisqa haqiqiy raqam (E.164 amaliyoti).</summary>
    private const int MinPhoneDigits = 10;

    /// <summary>E.164 bo'yicha eng uzun raqam — bundan uzuni telefon EMAS (tasodifiy matn).</summary>
    private const int MaxPhoneDigits = 15;

    /// <summary>
    /// Telefon → <c>sha256(faqat raqamlar, mamlakat kodi bilan)</c>.
    /// <c>"+998 90 123-45-67"</c> → <c>sha256("998901234567")</c>.
    ///
    /// <para>⚠️ <b>Mamlakat kodi SHART.</b> Meta raqamni xalqaro formatda saqlaydi; kodsiz
    /// yuborilgan <c>901234567</c> boshqa hash beradi va hech qachon mos kelmaydi. Shuning
    /// uchun 9 xonali (mahalliy) raqamga <c>998</c> o'zimiz qo'shamiz — markazning deyarli
    /// barcha lidlari O'zbekistondan.</para>
    ///
    /// <para>⚠️ <b>Boshidagi nollar olib tashlanadi</b> — <c>"0 90 123 45 67"</c> (mahalliy
    /// yozuv) va <c>"00998..."</c> (xalqaro terish prefiksi) ikkalasi ham bir xil raqamga
    /// keltiriladi.</para>
    ///
    /// <para>⚠️ Bu yerda <see cref="PhoneUtil.Key"/> ISHLATILMAYDI: u ataylab oxirgi 9 raqamni
    /// (mamlakat kodisiz) qaytaradi — CRM ichida solishtirish uchun to'g'ri, CAPI uchun esa
    /// aynan TESKARI talab.</para>
    /// </summary>
    public static string Phone(string? raw)
    {
        var digits = PhoneUtil.DigitsOnly(raw).TrimStart('0');
        if (digits.Length == 0) return "";

        if (digits.Length == 9) digits = UzCountryCode + digits;

        // Yaroqsiz uzunlik — hashlamaymiz (chaqiruvchi maydonni qo'shmaydi).
        if (digits.Length is < MinPhoneDigits or > MaxPhoneDigits) return "";

        return Sha256Hex(digits);
    }

    /// <summary>
    /// Email → <c>sha256(trim + lowercase)</c>.
    ///
    /// <para>Eng oddiy shakl tekshiruvi bor (<c>@</c> boshida ham, oxirida ham emas, ichida
    /// bo'shliq yo'q): forma maydoniga "yo'q", "-" kabi matn yozilgan bo'lsa uni hashlab
    /// yuborishning ma'nosi yo'q.</para>
    /// </summary>
    public static string Email(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0 || v.Contains(' ')) return "";

        var at = v.IndexOf('@');
        if (at <= 0 || at == v.Length - 1) return "";

        return Sha256Hex(v);
    }

    /// <summary>
    /// Ism/familiya → <c>sha256(lowercase, tinish belgilarisiz)</c>.
    ///
    /// <para>⚠️ <b>Apostroflar ham tinish belgisi</b> va OLIB TASHLANADI: <c>"To'lqin"</c>,
    /// <c>"Toʻlqin"</c>, <c>"To’lqin"</c> — uchalasi ham <c>"tolqin"</c> bo'ladi. Matn turli
    /// klaviaturalardan kiritilgani uchun aks holda bitta odam uchta xil hash berardi
    /// (xuddi shu muammo loyihada <c>ContactService.TopWords</c> da ham hal qilingan).</para>
    ///
    /// <para>Ketma-ket bo'shliqlar bittaga keltiriladi, chekkalari kesiladi.</para>
    /// </summary>
    public static string Name(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var sb = new StringBuilder(raw.Length);
        var prevSpace = true;                        // boshidagi bo'shliqlar tushib qolsin
        foreach (var ch in raw)
        {
            if (IsNameChar(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) { sb.Append(' '); prevSpace = true; }
            }
            // qolgani (tinish belgilari, APOSTROFLAR, emoji) — TASHLANADI
        }

        var v = sb.ToString().TrimEnd();
        return v.Length == 0 ? "" : Sha256Hex(v);
    }

    /// <summary>
    /// Ismda QOLADIGAN belgi: haqiqiy harf yoki raqam.
    ///
    /// <para>⚠️ <b><c>char.IsLetterOrDigit</c> YETARLI EMAS.</b> O'zbekcha yozuvda ishlatiladigan
    /// <c>ʻ</c> (U+02BB) va <c>ʼ</c> (U+02BC) Unicode'da <b>HARF</b> hisoblanadi
    /// (<see cref="UnicodeCategory.ModifierLetter"/>), ya'ni oddiy tekshiruvdan O'TIB KETADI va
    /// <c>"Toʻlqin"</c> → <c>"toʻlqin"</c> bo'lib qolardi. Natijada bir xil ism uchta xil hash
    /// berardi: <c>"To'lqin"</c> (ASCII apostrof, tinish belgisi) va <c>"To’lqin"</c> (U+2019,
    /// tinish belgisi) tashlanardi-yu, <c>"Toʻlqin"</c> tashlanmasdi. Shuning uchun
    /// <b>modifikator harflar ATAYIN chiqarib tashlanadi</b> — ular hech qachon ismning
    /// ma'noli qismi emas.</para>
    /// </summary>
    private static bool IsNameChar(char ch) =>
        char.IsLetterOrDigit(ch)
        && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.ModifierLetter;

    /// <summary>SHA-256 → kichik harfli hex (Meta talab qiladigan yagona ko'rinish).</summary>
    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// CAPI hodisasining <c>user_data</c> qismi — <b>faqat HASHLANGAN</b> qiymatlar
/// (+ hashlanmaydigan <see cref="LeadId"/>).
///
/// <para>🔴 Record ATAYIN xom telefon/email SAQLAMAYDI: <c>IgCapiEvent.PayloadJson</c>
/// bazaga yoziladi va DPA (Data Protection Assessment) aynan shuni tekshiradi. Xom PII faqat
/// <c>Lead</c> jadvalida qoladi. Xom qiymatdan qurish uchun <see cref="FromRaw"/> ishlatiladi —
/// ya'ni hashlamay o'tkazib yuborishning texnik imkoni yo'q.</para>
/// </summary>
/// <param name="LeadId">Meta lid ID (15–17 raqam). 🔴 HASHLANMAYDI.</param>
public sealed record MetaCapiUserData(
    string LeadId,
    string PhoneHash = "",
    string EmailHash = "",
    string FirstNameHash = "",
    string LastNameHash = "")
{
    /// <summary>Xom qiymatlardan quradi — hammasi shu yerda hashlanadi.</summary>
    public static MetaCapiUserData FromRaw(
        string leadgenId, string? phone, string? email,
        string? firstName = null, string? lastName = null) =>
        new(
            LeadId: (leadgenId ?? "").Trim(),
            PhoneHash: MetaCapiHash.Phone(phone),
            EmailHash: MetaCapiHash.Email(email),
            FirstNameHash: MetaCapiHash.Name(firstName),
            LastNameHash: MetaCapiHash.Name(lastName));

    /// <summary>Meta hodisani hech kimga bog'lay olmaydigan holat — hech bo'lmasa bitta
    /// identifikator bo'lishi kerak.</summary>
    public bool HasAnyIdentifier =>
        LeadId.Length > 0 || PhoneHash.Length > 0 || EmailHash.Length > 0;
}

/// <summary>
/// Bitta CAPI hodisasining kirish ma'lumoti (payload quruvchi uchun).
/// </summary>
/// <param name="EventName">🔴 ERKIN MATN — Events Manager'da sozlangan bosqich nomi bilan
/// AYNAN bir xil bo'lishi shart (<c>CenterMeta.InstagramCapiStageQualified</c> va h.k.).
/// Kodga yozib qo'yilmaydi.</param>
/// <param name="EventTimeUnix">Hodisa vaqti (unix, soniyalarda). 7 kundan eski bo'lmasin —
/// <see cref="MetaCapiPayload.IsEventTimeAcceptable(long, DateTime)"/>.</param>
/// <param name="Value">Faqat "to'lov qildi" hodisasida — to'lov summasi.</param>
/// <param name="Currency">Valyuta (bo'sh bo'lsa <c>UZS</c>). Faqat <paramref name="Value"/>
/// berilganda yoziladi.</param>
public sealed record MetaCapiEventInput(
    string EventName,
    long EventTimeUnix,
    MetaCapiUserData User,
    decimal? Value = null,
    string Currency = "");

/// <summary>
/// CAPI so'rov TANASINI quruvchi sof funksiyalar (§7.3) va Meta cheklovlarini qulflaydigan
/// tekshiruvlar (§7.5).
///
/// <para>HTTP'dan ATAYIN ajratilgan: payload shakli va cheklovlar test bilan qoplanadi,
/// <see cref="MetaCapiApi"/> esa faqat transport bo'lib qoladi.</para>
/// </summary>
public static class MetaCapiPayload
{
    /// <summary>Bizning hodisalarimiz brauzer/piksel emas, CRM tomonidan generatsiya qilinadi.</summary>
    public const string ActionSource = "system_generated";

    /// <summary><c>custom_data.lead_event_source</c> — Meta hisobotida CRM nomi shunday ko'rinadi.</summary>
    public const string LeadEventSource = "WunderkindLC";

    /// <summary><c>custom_data.event_source</c> — Conversion Leads oqimida qat'iy <c>"crm"</c>.</summary>
    public const string EventSource = "crm";

    /// <summary>Bo'sh qoldirilsa ishlatiladigan valyuta.</summary>
    public const string DefaultCurrency = "UZS";

    /// <summary>Bir so'rovdagi hodisalar chegarasi (Meta talabi).</summary>
    public const int MaxEventsPerRequest = 1000;

    /// <summary>Hodisa vaqti chegarasi — 7 kun.</summary>
    public const int MaxEventAgeDays = 7;

    /// <summary>
    /// 🔴 Xavfsizlik zaxirasi (1 soat). Meta chegarani <b>O'Z soati</b> bo'yicha tekshiradi va
    /// bitta eski hodisa <b>BUTUN so'rovni</b> rad ettiradi. Ya'ni "roppa-rosa 7 kun" chegarasida
    /// turgan hodisa yo'lda (navbat + qayta urinishlar) chegaradan chiqib ketishi mumkin edi.
    /// </summary>
    private const int SafetyMarginSeconds = 3600;

    /// <summary>
    /// Kelajakka yo'l qo'yiladigan farq (5 daqiqa). Kelajakdagi <c>event_time</c> ham rad
    /// etiladi; kichik farq esa server soatining siljishi bo'lishi mumkin.
    /// </summary>
    private const int MaxFutureSkewSeconds = 300;

    /// <summary>Toshkent ofseti — <see cref="AppClock"/> mintaqasi (UTC+5, yozgi vaqt yo'q).</summary>
    private static readonly TimeSpan TashkentOffset = TimeSpan.FromHours(5);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Payload bazaga (`IgCapiEvent.PayloadJson`) yoziladi va uni odam o'qiydi —
        // o'zbekcha hodisa nomi (`To'lov qildi`) ' bo'lib ketmasin.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /* ═════════════════════════ Vaqt ═════════════════════════ */

    /// <summary>
    /// Toshkent vaqtini unix soniyaga o'giradi.
    ///
    /// <para>⚠️ <b>Ofset QO'LDA biriktiriladi.</b> <see cref="AppClock.Now"/>
    /// <c>Kind=Unspecified</c> bo'lgan "devor soati"ni qaytaradi; uni to'g'ridan-to'g'ri
    /// <c>DateTimeOffset</c> ga bersak, .NET SERVER mintaqasini qo'llaydi — Docker'da bu UTC,
    /// ya'ni natija 5 soatga kelajakka siljib, Meta hodisani rad etardi.</para>
    /// </summary>
    public static long ToUnix(DateTime tashkentTime) =>
        new DateTimeOffset(
            DateTime.SpecifyKind(tashkentTime, DateTimeKind.Unspecified), TashkentOffset)
            .ToUnixTimeSeconds();

    /// <summary>Hozirgi vaqt unix'da (<see cref="AppClock"/> orqali — <c>DateTime.Now</c> TAQIQ).</summary>
    public static long NowUnix() => ToUnix(AppClock.Now);

    /// <summary>Unix → loyihaning standart ISO satri (Toshkent vaqti) — bazaga yozish uchun.</summary>
    public static string IsoFromUnix(long unix) =>
        DateTimeOffset.FromUnixTimeSeconds(unix).ToOffset(TashkentOffset)
            .DateTime.ToString("yyyy-MM-ddTHH:mm:ss");

    /// <summary>
    /// 🔴 <c>event_time</c> yaroqlimi? <b>Bitta eski hodisa butun so'rovni rad ettiradi</b>,
    /// shuning uchun bu tekshiruv yuborishdan OLDIN, har bir hodisa uchun alohida qilinadi.
    /// </summary>
    /// <param name="now">Hozirgi vaqt — PARAMETR (funksiya sof bo'lsin, test soatni o'zi beradi).</param>
    public static bool IsEventTimeAcceptable(long eventTimeUnix, DateTime now)
    {
        if (eventTimeUnix <= 0) return false;

        var nowUnix = ToUnix(now);
        var age = nowUnix - eventTimeUnix;

        if (age < -MaxFutureSkewSeconds) return false;                          // kelajak
        return age <= MaxEventAgeDays * 86400L - SafetyMarginSeconds;           // 7 kundan eski emas
    }

    /// <summary>Xato matni bilan (bo'sh satr — hammasi joyida). UI/loglar uchun.</summary>
    public static string EventTimeError(long eventTimeUnix, DateTime now)
    {
        if (eventTimeUnix <= 0) return "Hodisa vaqti ko'rsatilmagan.";
        if (IsEventTimeAcceptable(eventTimeUnix, now)) return "";

        return ToUnix(now) < eventTimeUnix
            ? "Hodisa vaqti kelajakda — Meta bunday so'rovni rad etadi."
            : $"Hodisa vaqti {MaxEventAgeDays} kundan eski — Meta butun so'rovni rad etadi.";
    }

    /* ═════════════════════════ Dedup kaliti ═════════════════════════ */

    /// <summary>
    /// <c>event_id</c> = <c>"{leadgenId}_{unix}"</c> — DETERMINISTIK.
    ///
    /// <para>⚠️ <c>Guid</c>/<c>GetHashCode</c> ISHLATILMAYDI: Meta dedupni
    /// <c>event_name</c> + <c>event_id</c> juftligi bo'yicha <b>48 soatlik</b> oynada qiladi.
    /// Kalit har safar boshqacha chiqsa, qayta urinishda bitta konversiya ikki marta
    /// sanalardi va Meta optimizatsiyasi buzilardi.</para>
    /// </summary>
    public static string EventId(string leadgenId, long eventTimeUnix) =>
        $"{(leadgenId ?? "").Trim()}_{eventTimeUnix}";

    /* ═════════════════════════ Payload ═════════════════════════ */

    /// <summary>Bitta hodisa (JSON satri) — <c>IgCapiEvent.PayloadJson</c> ga aynan shu yoziladi.</summary>
    public static string BuildEvent(MetaCapiEventInput e) =>
        JsonSerializer.Serialize(EventNode(e), JsonOpts);

    /// <summary>
    /// Bitta hodisa — hashlangan qiymatlar bevosita berilganda (§7.4 imzosi).
    /// </summary>
    /// <param name="hashedPhone">🔴 AYNAN hash (<see cref="MetaCapiHash.Phone"/> natijasi),
    /// xom telefon EMAS.</param>
    public static string BuildEvent(
        string eventName, long eventTimeUnix, string leadgenId,
        string hashedPhone = "", string hashedEmail = "",
        decimal? value = null, string currency = "") =>
        BuildEvent(new MetaCapiEventInput(
            eventName, eventTimeUnix,
            new MetaCapiUserData(leadgenId, PhoneHash: hashedPhone, EmailHash: hashedEmail),
            value, currency));

    /// <summary>
    /// To'liq so'rov tanasi: <c>{"data":[…]}</c>.
    ///
    /// <para>⚠️ <c>access_token</c> tanaga QO'SHILMAYDI — u so'rov manzilida ketadi
    /// (<see cref="MetaCapiApi"/>). Sabab: payload log/bazaga tushishi mumkin, token esa
    /// hech qachon tushmasligi kerak.</para>
    /// </summary>
    /// <param name="testEventCode">⚠️ FAQAT sinovda (Events Manager → "Test Events").
    /// Produksiyada BO'SH qoldiriladi: kod berilgan hodisalar faqat sinov oynasida ko'rinadi
    /// va reklama optimizatsiyasiga UMUMAN qo'shilmaydi.</param>
    public static string BuildBody(IReadOnlyList<MetaCapiEventInput> events, string testEventCode = "")
    {
        var body = new Dictionary<string, object>
        {
            ["data"] = events.Select(EventNode).ToList(),
        };
        if (!string.IsNullOrWhiteSpace(testEventCode))
            body["test_event_code"] = testEventCode.Trim();

        return JsonSerializer.Serialize(body, JsonOpts);
    }

    /// <summary>
    /// Hodisalarni <see cref="MaxEventsPerRequest"/> talik bo'laklarga bo'ladi — Meta bir
    /// so'rovda 1000 tadan ko'pini qabul qilmaydi (chegaradan oshgan so'rov TO'LIQ rad etiladi).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(
        IReadOnlyList<T> items, int size = MaxEventsPerRequest)
    {
        if (size < 1) size = MaxEventsPerRequest;

        var result = new List<IReadOnlyList<T>>();
        for (var i = 0; i < items.Count; i += size)
            result.Add(items.Skip(i).Take(size).ToList());
        return result;
    }

    /// <summary>Bitta hodisaning tugun ko'rinishi (JSON'ga aylantirishdan oldin).</summary>
    private static Dictionary<string, object> EventNode(MetaCapiEventInput e)
    {
        var user = new Dictionary<string, object>();

        // 🔴 lead_id HASHLANMAYDI va RAQAM sifatida yuboriladi.
        // ⚠️ Raqamga aylanmasa maydon UMUMAN qo'shilmaydi: satr ko'rinishidagi lead_id
        //    Meta tomonidan rad etilib, BUTUN so'rovni yiqitardi.
        if (long.TryParse(e.User.LeadId, out var leadIdNum) && leadIdNum > 0)
            user["lead_id"] = leadIdNum;

        // Hashlangan maydonlar — MASSIV ko'rinishida (Meta bir nechta qiymatni qo'llaydi).
        if (e.User.PhoneHash.Length > 0) user["ph"] = new[] { e.User.PhoneHash };
        if (e.User.EmailHash.Length > 0) user["em"] = new[] { e.User.EmailHash };
        if (e.User.FirstNameHash.Length > 0) user["fn"] = new[] { e.User.FirstNameHash };
        if (e.User.LastNameHash.Length > 0) user["ln"] = new[] { e.User.LastNameHash };

        var custom = new Dictionary<string, object>
        {
            ["lead_event_source"] = LeadEventSource,
            ["event_source"] = EventSource,
        };
        if (e.Value.HasValue)
        {
            custom["value"] = e.Value.Value;
            custom["currency"] = string.IsNullOrWhiteSpace(e.Currency)
                ? DefaultCurrency
                : e.Currency.Trim().ToUpperInvariant();
        }

        return new Dictionary<string, object>
        {
            ["event_name"] = (e.EventName ?? "").Trim(),
            ["event_time"] = e.EventTimeUnix,
            ["action_source"] = ActionSource,
            ["event_id"] = EventId(e.User.LeadId, e.EventTimeUnix),
            ["user_data"] = user,
            ["custom_data"] = custom,
        };
    }
}
