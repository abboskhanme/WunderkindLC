using IntellectCRM.Domain;

namespace IntellectCRM.Application.Services;

/// <summary>
/// "BOG'LANISH KERAK" moduli — bosqichlar, natijalar va o'tish qoidalarining <b>YAGONA MANBASI</b>
/// (sof funksiyalar, testlangan: <c>ContactServiceTests</c>).
///
/// <para>Backend, navbat sahifasi va hisobotlar AYNAN shu kalitlarni ishlatadi. Frontenddagi
/// yorliqlar server javobidan (<c>GET /api/admin/contacts/meta</c>) keladi — u yerdagi ro'yxat
/// server javob bermasa ishlatiladigan ZAXIRA, yagona haqiqat manbai emas
/// (karyera modulidagi <c>careerLabels.ts</c> bilan bir xil konvensiya).</para>
/// </summary>
public static class ContactService
{
    /// <summary>Sabablar katalogidagi kategoriya kaliti (Sozlamalar → Sabablar).</summary>
    public const string ReasonCategory = "contact";

    /// <summary>Bosqich: kalit + yorliq + yakuniymi + rang (UI chiplari uchun).</summary>
    /// <param name="IsOpen">Ochiq (navbatda turadi) — yakuniy bosqichlar `false`.</param>
    public readonly record struct Status(string Key, string Label, bool IsOpen, string Color);

    /// <summary>Bosqichlar — UI'da SHU tartibda.</summary>
    public static readonly IReadOnlyList<Status> Statuses = new List<Status>
    {
        new(ContactStatuses.New,      "Bog'lanish kerak",   true,  "amber"),
        new(ContactStatuses.Callback, "Qayta qo'ng'iroq",   true,  "sky"),
        new(ContactStatuses.Done,     "Hal bo'ldi",         false, "emerald"),
        new(ContactStatuses.Failed,   "Bog'lanib bo'lmadi", false, "rose"),
    };

    /// <summary>OCHIQ bosqichlar — navbatda turadigan, ya'ni "hali ish bor" degani.</summary>
    public static readonly IReadOnlyList<string> OpenStatuses =
        new[] { ContactStatuses.New, ContactStatuses.Callback };

    /// <summary>Bitta bog'lanish urinishining natijasi.</summary>
    public readonly record struct Result(string Key, string Label, bool Reached);

    /// <summary>
    /// Natijalar. <c>Reached</c> — odam bilan HAQIQATAN gaplashildimi: kunlik hisobotdagi
    /// "nechta odam bilan bog'lanildi" AYNAN shu bo'yicha sanaladi (ko'tarmagan qo'ng'iroq
    /// "bog'lanildi" emas — aks holda hisobot urinishlar soni bilan aralashib ketardi).
    /// </summary>
    public static readonly IReadOnlyList<Result> Results = new List<Result>
    {
        new("answered",     "Javob berdi (gaplashildi)", true),
        new("no_answer",    "Ko'tarmadi",                false),
        new("busy",         "Band — keyin deydi",        false),
        new("wrong_number", "Raqam ishlamadi",           false),
        new("other",        "Boshqa",                    true),
    };

    /// <summary>
    /// HODISA turlarining yorliqlari (<see cref="ContactAttemptTypes"/>) — kunlik jurnalda
    /// har qator nima ekani yozilib turadi ("Bog'lanildi", "Talab ochildi", ...).
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> AttemptTypes =
        new[]
        {
            (ContactAttemptTypes.Created, "Talab ochildi"),
            (ContactAttemptTypes.Contact, "Bog'lanildi"),
            (ContactAttemptTypes.Note,    "Izoh"),
            (ContactAttemptTypes.Reopen,  "Qayta ochildi"),
        };

    /// <inheritdoc cref="StatusLabel"/>
    public static string TypeLabel(string? key)
    {
        foreach (var t in AttemptTypes) if (t.Key == key) return t.Label;
        return key ?? "";
    }

    public static bool IsValidStatus(string? key) => Statuses.Any(s => s.Key == key);
    public static bool IsValidResult(string? key) => Results.Any(r => r.Key == key);
    public static bool IsOpen(string? status) => OpenStatuses.Contains(status ?? "");

    /// <summary>Yorliq; noma'lum kalit uchun kalitning O'ZI qaytadi (bo'sh qator emas — tarix
    /// yozuvida "nima bo'lgani" baribir ko'rinib tursin).</summary>
    public static string StatusLabel(string? key)
    {
        foreach (var s in Statuses) if (s.Key == key) return s.Label;
        return key ?? "";
    }

    /// <inheritdoc cref="StatusLabel"/>
    public static string ResultLabel(string? key)
    {
        foreach (var r in Results) if (r.Key == key) return r.Label;
        return key ?? "";
    }

    /// <summary>Shu natijada odam bilan haqiqatan gaplashildimi (noma'lum kalit — yo'q).</summary>
    public static bool Reached(string? resultKey) =>
        Results.FirstOrDefault(r => r.Key == resultKey).Reached;

    /// <summary>
    /// MUDDATI O'TGANMI — qayta qo'ng'iroq sanasi bugundan oldin. Navbatda qizil bo'lib turadi
    /// va hisobotda alohida sanaladi.
    /// </summary>
    /// <param name="today">Bugungi kun ("yyyy-MM-dd") — <see cref="AppClock"/> dan uzatiladi
    /// (sof funksiya bo'lishi va testlanishi uchun ichkarida O'QILMAYDI).</param>
    public static bool IsOverdue(string? status, string? dueDate, string today) =>
        status == ContactStatuses.Callback
        && !string.IsNullOrEmpty(dueDate)
        && string.CompareOrdinal(dueDate, today) < 0;

    /* =========================================================================================
     *  MUDDAT GURUHLARI — "bugun nechta odamga bog'lanish kerak?"
     * ====================================================================================== */

    /// <summary>
    /// Navbatning MUDDAT bo'yicha guruhlari. Operator savoli bosqich emas, VAQT:
    /// "bugun kimga qo'ng'iroq qilishim kerak?".
    /// </summary>
    public static class Due
    {
        /// <summary>BUGUN qilinishi kerak: muddati o'tgan + bugungi + sanasiz (jamlanma).</summary>
        public const string Todo = "todo";
        public const string Overdue = "overdue";
        public const string Today = "today";
        public const string Tomorrow = "tomorrow";
        /// <summary>Ertadan keyingi 6 kun (ya'ni bugundan +2..+7).</summary>
        public const string Week = "week";
        public const string Later = "later";
        /// <summary>Sana belgilanmagan — "Bog'lanish kerak" holatidagi talablar.</summary>
        public const string NoDate = "nodate";
    }

    /// <summary>
    /// Ochiq talab qaysi MUDDAT guruhiga tushadi. Yakuniy (done/failed) talab uchun bo'sh satr —
    /// u navbatda umuman yo'q.
    /// </summary>
    /// <param name="today">Bugungi kun ("yyyy-MM-dd") — sof funksiya bo'lishi uchun UZATILADI
    /// (<see cref="AppClock"/> ichkarida o'qilmaydi).</param>
    public static string BucketOf(string? status, string? dueDate, string today)
    {
        // "Bog'lanish kerak" — sana yo'q, ya'ni "hoziroq navbatda turibdi".
        if (status == ContactStatuses.New) return Due.NoDate;
        if (status != ContactStatuses.Callback) return "";

        var due = (dueDate ?? "").Trim();
        // Sanasiz "qayta qo'ng'iroq" bo'lmasligi kerak (server sanani talab qiladi), lekin eski
        // yoki qo'lda tuzatilgan yozuv shunday bo'lsa u YO'QOLIB ketmasin — sanasizlarga qo'shamiz.
        if (due.Length == 0) return Due.NoDate;

        var cmp = string.CompareOrdinal(due, today);
        if (cmp < 0) return Due.Overdue;
        if (cmp == 0) return Due.Today;

        if (!DateOnly.TryParse(today, out var t) || !DateOnly.TryParse(due, out var d))
            return Due.Later;
        var days = d.DayNumber - t.DayNumber;
        return days == 1 ? Due.Tomorrow : days <= 7 ? Due.Week : Due.Later;
    }

    /// <summary>
    /// Shu guruh "BUGUN QILISH KERAK" ga kiradimi.
    ///
    /// <para>Muddati o'tganlar ham kiradi (kechikkani ish yo'qolgani degani emas) va sanasizlar
    /// ham (ular ochilgan kunidan beri kutmoqda). Aks holda operator "bugun 5 ta" deb ko'rib,
    /// kechagi 12 tasini ko'rmay qolardi.</para>
    /// </summary>
    public static bool IsTodo(string? bucket) =>
        bucket is Due.Overdue or Due.Today or Due.NoDate;

    /// <summary>Guruh kaliti haqiqiymi (noma'lum kalit filtrga qo'yilmaydi).</summary>
    public static bool IsKnownDue(string? key) =>
        key is Due.Todo or Due.Overdue or Due.Today or Due.Tomorrow or Due.Week or Due.Later or Due.NoDate;

    /* =========================================================================================
     *  JAVOBLAR TAHLILI ("javobi nima dedi" matnlari)
     * ====================================================================================== */

    /// <summary>
    /// MA'NOSIZ so'zlar — chastota tahlilida chiqarib tashlanadi.
    ///
    /// <para>Ro'yxat ATAYIN qisqa: faqat bog'lovchi/olmosh/yordamchi so'zlar. "to'lov", "dars",
    /// "kasal", "kerak" kabi so'zlar QOLADI — aynan ular hisobotning ma'nosi.</para>
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "va", "bilan", "uchun", "ham", "lekin", "ammo", "yoki", "shu", "bu", "u", "o'sha",
        "men", "sen", "siz", "biz", "ular", "uni", "unga", "meni", "menga", "bizga", "sizga",
        "deb", "dedi", "deydi", "aytdi", "gapirdi", "javob", "berdi",
        "bo'ldi", "bo'lib", "bo'lgan", "bo'ladi", "edi", "emas", "yana", "endi",
        "qildi", "qilib", "qilish", "qilaman", "qiladi",
        "haqida", "ustida", "keyin", "oldin", "hozir", "juda", "faqat", "yaxshi",
        "bir", "ikki", "uch", "ha", "yo'q", "mumkin", "kelmadi", "kelaman",
        "the", "and", "for", "with",
    };

    /// <summary>Chastotaga kiradigan eng qisqa so'z (uzunligi shundan kichik so'z tashlab yuboriladi).</summary>
    private const int MinWordLength = 3;

    /// <summary>
    /// Javob matnlaridagi eng ko'p uchragan so'zlar.
    ///
    /// <para>Har matn ichida bir so'z bir necha marta kelsa ham BIR marta sanaladi — aks holda
    /// bitta uzun izoh butun hisobotni egallab olardi ("nechta javobda uchradi" degan savol
    /// "necha marta yozildi" dan foydaliroq).</para>
    ///
    /// <para>Apostroflar (' ʻ ’ `) BIR ko'rinishga keltiriladi: aks holda "to'lov" va "toʻlov"
    /// ikki xil so'z bo'lib sanalardi (matn turli klaviaturalardan kiritiladi).</para>
    ///
    /// <para><b>QO'SHIMCHALAR KESILMAYDI</b> — bu ATAYLAB qilingan qaror, kamchilik emas.
    /// Ya'ni "to'lov" va "to'lovni" ikki xil so'z bo'lib sanaladi. O'zak ajratish (stemming)
    /// hisobotni birmuncha yaxshilagan bo'lardi, lekin o'zbek tili uchun ishonchli qoida yozish
    /// oson emas: ehtiyotsiz qo'shimcha ro'yxati bog'liq bo'lmagan so'zlarni ham birlashtirib
    /// yuboradi va hisobot NOTO'G'RI ko'rsata boshlaydi — bu "biroz kamroq foydali" dan yomonroq.
    /// Qaror <c>ContactServiceTests.TopWords_QOSHIMCHALIshakl_ALOHIDAsozDebSanaladi</c> testi
    /// bilan qulflangan: o'zgartirmoqchi bo'lsangiz, avval o'sha testni ongli ravishda yangilang.</para>
    /// </summary>
    public static List<(string Word, int Count)> TopWords(IEnumerable<string> texts, int take = 25)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in Tokenize(text))
            {
                if (word.Length < MinWordLength || StopWords.Contains(word)) continue;
                if (!seen.Add(word)) continue;                      // bitta matnda bir marta
                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
        }

        return counts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, take))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>Matnni so'zlarga ajratadi: kichik harf, apostrof normallashtirilgan, tinish belgisisiz.</summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        var buf = new System.Text.StringBuilder();
        foreach (var raw in text)
        {
            var ch = raw switch
            {
                'ʻ' or 'ʼ' or '’' or '‘' or '`' or '´' => '\'',
                _ => char.ToLowerInvariant(raw),
            };
            if (char.IsLetterOrDigit(ch) || ch == '\'') buf.Append(ch);
            else if (buf.Length > 0) { yield return Trim(buf); buf.Clear(); }
        }
        if (buf.Length > 0) yield return Trim(buf);
    }

    /// <summary>Chetidagi apostroflarni olib tashlaydi ("'kasal'" → "kasal").</summary>
    private static string Trim(System.Text.StringBuilder b) => b.ToString().Trim('\'');

    /// <summary>
    /// Bog'lanish urinishidan keyingi holat QABUL QILINADIMI.
    ///
    /// <para>Qoida: keyingi qadam sifatida faqat <c>callback</c> (qayta qo'ng'iroq),
    /// <c>done</c> (hal bo'ldi) yoki <c>failed</c> (bog'lanib bo'lmadi) tanlanadi.
    /// <c>new</c> ATAYIN taqiqlangan: bog'langandan keyin "hech narsa bo'lmagandek" boshiga
    /// qaytish navbatni cheksiz aylantirardi va hisobotda bosqich ko'rinmasdi — kerak bo'lsa
    /// bugungi sana bilan <c>callback</c> tanlanadi.</para>
    /// </summary>
    public static bool CanTransitionTo(string? next) =>
        next is ContactStatuses.Callback or ContactStatuses.Done or ContactStatuses.Failed;

    /* ======================================================================================
     *  KANBAN USTUNLARI (ContactStage) — foydalanuvchi qo'shadigan bosqichlar
     * ====================================================================================== */

    /// <summary>Ustun rangi kalitlari — klientdagi <c>config/stageColors.ts</c> bilan AYNAN bir xil.</summary>
    public static readonly IReadOnlyList<string> StageColorKeys = new List<string>
    {
        "slate", "blue", "emerald", "amber", "violet", "rose", "cyan", "orange",
    };

    /// <summary>Rang kaliti ma'lummi. Noma'lum rang RAD ETILMAYDI — chaqiruvchi uni
    /// "slate" ga tushiradi (klient ham shunday himoyalangan).</summary>
    public static bool IsValidColor(string? color) =>
        !string.IsNullOrWhiteSpace(color) && StageColorKeys.Contains(color.Trim());

    /// <summary>Rangni xavfsiz qiymatga keltiradi.</summary>
    public static string SafeColor(string? color) =>
        IsValidColor(color) ? color!.Trim() : "slate";

    /// <summary>TIZIM ustuni ta'rifi (seed uchun).</summary>
    /// <param name="Key">Ustun Id'si VA bazaviy holat — ikkisi ATAYIN bir xil, shuning uchun
    /// <c>StageId</c> bo'sh talab o'z holatining ustunida qo'shimcha izlashsiz ko'rinadi.</param>
    public readonly record struct SystemStage(string Key, string Title, string Color);

    /// <summary>
    /// Har bazaviy holat uchun BITTA tizim ustuni — taxtaning boshlang'ich ko'rinishi
    /// (bugungi to'rt bosqich bilan AYNAN bir xil, ya'ni seed'dan keyin hech narsa o'zgarmaydi).
    /// </summary>
    public static readonly IReadOnlyList<SystemStage> SystemStages = new List<SystemStage>
    {
        new(ContactStatuses.New, "Bog'lanish kerak", "amber"),
        new(ContactStatuses.Callback, "Qayta qo'ng'iroq", "blue"),
        new(ContactStatuses.Done, "Hal bo'ldi", "emerald"),
        new(ContactStatuses.Failed, "Bog'lanib bo'lmadi", "rose"),
    };

    /// <summary>Ustun shu bazaviy holatga BOG'LANA oladimi (to'rt holatdan biri bo'lishi shart).</summary>
    public static bool CanAnchorTo(string? baseStatus) => IsValidStatus(baseStatus);

    /// <summary>
    /// Ustun shu talabga MOS keladimi — ustunning bazaviy holati talab holati bilan bir xilmi.
    ///
    /// <para>⚠️ Taxtaning asosiy qoidasi: ustun HOLATNI almashtirmaydi, unga bog'lanadi. Mos
    /// kelmasa karta o'z holatiga ZID ustunda ko'rinib, hisobot bilan ziddiyat yuzaga kelardi.</para>
    /// </summary>
    public static bool StageMatches(string? stageBaseStatus, string? requestStatus) =>
        !string.IsNullOrEmpty(stageBaseStatus) && stageBaseStatus == requestStatus;
}
