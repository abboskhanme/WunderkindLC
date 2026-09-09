namespace IntellectCRM.Application.Services;

/// <summary>
/// O'ZGARISHLAR TARIXINI BO'LIMLARGA AJRATISH — <b>yagona manba</b> (sof funksiyalar, testlangan:
/// <c>AuditSectionsTests</c>).
///
/// <para><c>AuditLog.EntityType</c> texnik nom (masalan <c>"StudentDiscount"</c>), foydalanuvchi
/// esa BO'LIM ko'radi ("O'quvchilar"). Shu ikkisi orasidagi xarita AYNAN shu yerda — Sozlamalardagi
/// "O'zgarishlar tarixi" sahifasi ham, `GET /api/admin/audit?section=` filtri ham shundan oziqlanadi.</para>
///
/// <para>⚠️ TARIXIY NOMLAR ALDAMASIN: `EntityType` qiymatlari yillar davomida qayta ishlatilgan va
/// nomi bilan mazmuni har doim mos EMAS. Masalan <c>"StudentDiscount"</c> nafaqat chegirma, balki
/// o'quvchini arxivlash, login bloklash va qo'lda hisob tahriri uchun ham yozilgan; <c>"ClassFee"</c>
/// guruhni arxivlashda ham ishlatilgan; <c>"TeacherSalary"</c> o'qituvchi yozuvining o'zi
/// o'zgarganda ham yoziladi. Shuning uchun xarita nom bo'yicha emas, <b>o'sha yozuv qaysi bo'lim
/// sahifasida ko'rinishi kerakligi</b> bo'yicha tuzilgan.</para>
///
/// <para>Yangi `audit.Record(...)` chaqiruvi qo'shsangiz — `EntityType`ni shu yerga ham qo'shing,
/// aks holda yozuv "Boshqa" bo'limiga tushib qoladi (yo'qolmaydi, lekin filtrda topilmaydi).</para>
/// </summary>
public static class AuditSections
{
    /// <summary>Xaritaga tushmagan `EntityType` uchun zaxira bo'lim.</summary>
    public const string Other = "other";

    /// <summary>Bo'lim: kalit (ruxsat kaliti bilan bir xil) + ko'rinadigan nom.</summary>
    /// <param name="Key">Ruxsat/nav kaliti — `adminPermissions` dagi bilan AYNAN bir xil.</param>
    public readonly record struct Section(string Key, string Label);

    /// <summary>Bo'limlar — UI'da SHU tartibda ko'rsatiladi.</summary>
    public static readonly IReadOnlyList<Section> All = new List<Section>
    {
        new("students",  "O'quvchilar"),
        new("contacts",  "Bog'lanish kerak"),
        new("classes",   "Guruhlar"),
        new("teachers",  "O'qituvchilar"),
        new("schedule",  "Kurslar"),
        new("finance",   "Moliya"),
        new("leads",     "Lidlar"),
        new("marketing", "Marketing"),
        new("books",     "Kitoblar sotuvi"),
        new("contracts", "Shartnomalar"),
        new("vacancies", "Vakansiyalar"),
        new("staff",     "Xodimlar"),
        new("settings",  "Sozlamalar"),
        new(Other,       "Boshqa"),
    };

    /// <summary>
    /// `EntityType` → bo'lim kaliti. Bir bo'limga bir nechta tur tushishi mumkin.
    /// </summary>
    private static readonly Dictionary<string, string> ByEntityType = new(StringComparer.Ordinal)
    {
        // --- O'quvchilar ---
        // "StudentDiscount" — chegirma, arxivlash/tiklash, login bloklash, qo'lda oylik tahriri.
        ["StudentDiscount"] = "students",
        ["Student"] = "students",
        // YUZ BILAN KIRISH — etalon (tasdiqlash/tozalash) va ishonchli qurilmani bekor qilish.
        // O'quvchilar bo'limida, chunki amal AYNAN bitta o'quvchining kirishiga tegishli.
        // ⚠️ Bu yozuvlarda selfi MANZILI hech qachon bo'lmaydi (biometrik ma'lumot tarixga
        // tushmasin — `.claude/rules/audit.md` "Maxfiy qiymat yozilmaydi" qoidasi).
        // "StudentBall" — reyting balini QO'LDA tuzatish (qo'shish / 0 ga tushirish).
        // O'quvchilar bo'limida: amal aynan bitta o'quvchining reytingdagi o'rniga tegishli.
        ["StudentBall"] = "students",
        ["StudentFaceProfile"] = "students",
        ["TrustedDevice"] = "students",

        // --- Bog'lanish kerak (follow-up navbati) ---
        ["ContactRequest"] = "contacts",

        // --- Guruhlar ---
        ["Group"] = "classes",
        // "Membership" — a'zolik hodisalari (qo'shish/aktivlashtirish/muzlatish/ko'chirish/chiqarish),
        // EntityId = "{groupId}:{studentId}".
        ["Membership"] = "classes",
        // "ClassFee" — guruh oyligi, guruh yaratish/tahrir/arxiv.
        ["ClassFee"] = "classes",
        // "StudentExtension" — UZAYTIRISH hodisasi: o'quvchi kursning bir bosqichini tugatib
        // KEYINGI bosqichga o'tdi (guruhni "Tugatish (sertifikat bilan)" yo'lida avtomatik yoki
        // qo'lda belgilanadi). "Guruhlar" bo'limida, chunki amal AYNAN guruh a'zoligining
        // ko'chishi; `EntityId` — `Membership` naqshidagidek "{fromGroupId}:{studentId}", ya'ni
        // yozuv TUGATILGAN guruhning "Tarix" tabida ko'rinadi (`EntityId.StartsWith(groupId + ":")`).
        ["StudentExtension"] = "classes",

        // --- Kurslar ---
        ["Course"] = "schedule",

        // --- O'qituvchilar ---
        // "TeacherSalary" — maosh to'lovi VA o'qituvchi yozuvining o'zi (yaratish/tahrir/arxiv).
        ["TeacherSalary"] = "teachers",
        ["TeacherReview"] = "teachers",
        // "substitute_teacher" — O'RINBOSAR biriktirish va uni bekor qilish. "Guruhlar" emas,
        // "O'qituvchilar" bo'limida: amal guruhning tarkibiga emas, KIM dars o'tishiga va kimning
        // MAOSHIGA (o'rinbosarga haq, asosiy o'qituvchidan ushlanma) tegishli.
        ["substitute_teacher"] = "teachers",

        // --- Moliya ---
        ["FinanceTransaction"] = "finance",

        // --- Qolgan bo'limlar ---
        ["Lead"] = "leads",
        // "LeadForm" — ijtimoiy tarmoq formalari (yaratish/tahrir/nusxa/o'chirish). Lidlar
        // bo'limida, chunki forma AYNAN lid ishlab chiqaradi.
        ["LeadForm"] = "leads",
        // --- Marketing (Instagram AI agenti) ---
        // "Instagram" — BUTUN modulning yagona turi: akkauntni ulash/uzish, sozlamalar, kalit so'z
        // qoidalari, bilim bazasi, operator javobi va suhbatni lidga aylantirish. Bo'lim kaliti
        // `marketing` ruxsat kaliti bilan bir xil (`adminPermissions` da allaqachon bor).
        // ⚠️ Bu yozuvlarda TOKEN/SECRET hech qachon bo'lmaydi ("Maxfiy qiymat yozilmaydi" qoidasi),
        // shuningdek mijozning DM matni ham tarixga ko'chirilmaydi — faqat "kimga javob berildi".
        ["Instagram"] = "marketing",

        ["Book"] = "books",
        ["BookOrder"] = "books",
        ["Contract"] = "contracts",
        ["Vacancy"] = "vacancies",
        ["JobApplication"] = "vacancies",
        ["Staff"] = "staff",
        // --- KPI (xodimlar samaradorligi) ---
        // ⚠️ ALOHIDA "kpi" bo'limi ATAYIN OCHILMADI: `All` ro'yxatiga yangi chip qo'shish
        // "O'zgarishlar tarixi" filtrini kengaytiradi, KPI yozuvlari esa mazmunan XODIM
        // haqidagi qarorlar (rol, oklad, jarima, tasdiqlangan oylik) — ya'ni «Xodimlar»
        // bo'limining o'zi. Kerak bo'lsa keyinchalik alohida bo'lim qo'shiladi; yozuvlar
        // hozir ham topiladi, "Boshqa" ga tushib qolmaydi.
        ["KpiProfile"] = "staff",           // rol biriktirish + OKLAD versiyalari
        ["KpiRuleSet"] = "staff",           // qoidalar versiyasi, seed, oy boshi snapshoti
        ["KpiTicket"] = "staff",            // sifat nazorati tiketi (pul ushlanmasi)
        ["KpiMonthResult"] = "staff",       // oyni tasdiqlash / qayta ochish
        ["ChecklistTemplateItem"] = "staff",// kunlik cheklist bandini tahrirlash
        ["CenterMeta"] = "settings",
        ["CertificateTemplate"] = "settings",
    };

    /// <summary>Yozuv qaysi bo'limga tegishli (noma'lum tur → <see cref="Other"/>).</summary>
    public static string SectionOf(string? entityType) =>
        entityType is not null && ByEntityType.TryGetValue(entityType, out var s) ? s : Other;

    /// <summary>
    /// Bo'lim kalitiga tegishli `EntityType`lar. Noma'lum/bo'sh kalit uchun BO'SH ro'yxat qaytadi —
    /// chaqiruvchi bunda filtr qo'ymasligi kerak (aks holda "hech narsa" chiqib qolardi).
    /// <see cref="Other"/> uchun ham bo'sh: uni ro'yxat bilan emas, "xaritada yo'q" shartida
    /// filtrlash kerak (<see cref="KnownEntityTypes"/>).
    /// </summary>
    public static IReadOnlyList<string> EntityTypesOf(string? section) =>
        string.IsNullOrEmpty(section) || section == Other
            ? Array.Empty<string>()
            : ByEntityType.Where(kv => kv.Value == section).Select(kv => kv.Key).ToList();

    /// <summary>Xaritada BOR barcha turlar — "Boshqa" bo'limini (bularning teskarisi) yasash uchun.</summary>
    public static IReadOnlyList<string> KnownEntityTypes { get; } = ByEntityType.Keys.ToList();

    /// <summary>Bo'lim kaliti haqiqiymi (noma'lum kalit filtrga qo'yilmaydi).</summary>
    public static bool IsKnownSection(string? section) =>
        !string.IsNullOrEmpty(section) && All.Any(s => s.Key == section);
}
