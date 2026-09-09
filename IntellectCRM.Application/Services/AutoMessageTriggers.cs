using System.Linq;

namespace IntellectCRM.Application.Services;

/// <summary>
/// YAGONA avto-xabar hodisalari katalogi. Har bir <see cref="IntellectCRM.Domain.AutoMessageRule"/>
/// shu ro'yxatdagi bitta kalitga (Trigger) bog'lanadi. Yangi hodisa qo'shilsa — shu yerga yozib qo'yish
/// kifoya: frontend forma va backend validatsiya shu katalogdan avtomatik ishlaydi.
///
/// Bu eski <c>AutoSmsService</c> (faqat SMS) + <c>ReminderTriggers</c> (faqat push/telegram) ro'yxatlarini
/// birlashtiradi va yangi hodisalarni (monthly_charge, attendance_absent, student_added) qo'shadi.
///
/// DIQQAT: <see cref="TriggerInfo.Tokens"/> — <c>MessageTokenizer</c>/dispatcher haqiqatan qo'llab-quvvatlaydigan
/// ANIQ token nomlari (o'ylab topilmagan). Kanallar (<see cref="TriggerInfo.Sms"/>/Push/Telegram) — shu hodisada
/// mavjud kanallar (frontend faqat shularni toggle qiladi). Lid hodisalarida faqat SMS ishlaydi (lidda push/telegram yo'q).
/// </summary>
public static class AutoMessageTriggers
{
    public const string PaymentReceived = "payment_received";
    public const string MonthlyCharge = "monthly_charge";
    public const string PaymentDebt = "payment_debt";
    public const string AttendanceAbsent = "attendance_absent";
    public const string GradeEntered = "grade_entered";
    public const string Birthday = "birthday";
    public const string StudentAdded = "student_added";
    public const string LeadNew = "lead_new";
    public const string TrialReminder = "trial_reminder";
    public const string TestLink = "test_link";
    public const string TestResult = "test_result";
    public const string LessonAttendance = "lesson_attendance";
    public const string CustomSchedule = "custom_schedule";

    // Hodisa toifalari (frontend guruhlab ko'rsatadi).
    public const string CategoryLeads = "Lidlar";
    public const string CategoryEducation = "O'quv jarayoni";
    public const string CategoryFinance = "Moliya";
    public const string CategoryOther = "Boshqa";

    /// <param name="Sms">SMS kanali mavjudmi (frontend toggle ko'rsatadimi).</param>
    /// <param name="Push">Push kanali mavjudmi.</param>
    /// <param name="Telegram">Telegram kanali mavjudmi.</param>
    /// <param name="Category">Toifa (frontend guruhlash uchun): "Lidlar" | "O'quv jarayoni" | "Moliya" | "Boshqa".</param>
    public record TriggerInfo(
        string Key,
        string Label,
        string Description,
        string[] Tokens,
        bool Sms,
        bool Push,
        bool Telegram,
        bool SupportsSchedule,
        bool SupportsSendScope,
        string[] Audiences,
        string DefaultAudience,
        string DefaultTemplate,
        string Category,
        /// <summary>Matn IXTIYORIY — bo'sh qoldirilsa tizim matnni o'zi tuzadi (qarzdorlik eslatmasi kabi).</summary>
        bool TemplateOptional = false);

    // Umumiy token to'plamlari (takrorni kamaytirish uchun).
    private static readonly string[] ParentsAll = { "{ism}", "{fish}", "{guruh}", "{qarzdorlik}", "{markaz}", "{telefon}" };

    public static readonly TriggerInfo[] All =
    {
        new(PaymentReceived,
            "To'lov qabul qilinganda",
            "O'quvchining oylik (tuition) to'lovi qabul qilinganda ota-onaga (yoki o'quvchiga) tasdiq xabari. {oy} — to'lov QAYSI OY uchun ekani (kiritilgan sanadan emas, tanlangan oydan). {kurs}/{guruh} — to'lov QAYSI guruh (kurs) uchun ekani: o'quvchi bir necha guruhda o'qisa har to'lov alohida yoziladi, demak har biriga o'z kursi nomi bilan alohida xabar boradi.",
            new[] { "{ism}", "{fish}", "{summa}", "{oy}", "{sana}", "{qarzdorlik}", "{kurs}", "{guruh}", "{telefon}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism}ning {kurs} kursi ({guruh}) uchun {oy} oyi to'lovi qabul qilindi. Summa: {summa}. Qarzdorlik: {qarzdorlik}. {markaz}",
            Category: CategoryFinance),

        new(MonthlyCharge,
            "Oylik hisob yaratilganda",
            "Har oy avtomatik oylik to'lov hisobi yaratilganda ota-onaga qancha hisoblangani va umumiy qarzdorlik haqida xabar.",
            new[] { "{ism}", "{fish}", "{oy}", "{summa}", "{qarzdorlik}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} uchun {oy} oyi to'lovi hisoblandi: {summa}. Umumiy qarzdorlik: {qarzdorlik}. {markaz}",
            Category: CategoryFinance),

        new(PaymentDebt,
            "Qarzdorlik eslatmasi",
            "Balansi manfiy o'quvchilarga har oyning 1-sanasida, keyin har 2 kunda eslatma. ⚠️ Xabar HAR FAN (kurs) uchun ALOHIDA yuboriladi: o'quvchi ikki fanda o'qisa ikkita alohida xabar (va ikkita SMS) ketadi — {qarzdorlik}, {kurs}, {guruh} va {oqituvchi} har birida SHU fanning qiymati bo'ladi. Matn IXTIYORIY: bo'sh qoldirsangiz tizim matnni o'zi tuzadi (\"F.I.Sh — Kurs bo'yicha qarzdorlik: summa\").",
            new[] { "{ism}", "{fish}", "{qarzdorlik}", "{kurs}", "{guruh}", "{oqituvchi}", "{telefon}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "",
            Category: CategoryFinance,
            TemplateOptional: true),

        new(AttendanceAbsent,
            "O'quvchi darsga kelmaganda",
            "Jurnalda o'quvchiga davomat sababi (kelmadi) belgilanganda ota-onaga xabar.",
            new[] { "{ism}", "{fish}", "{sana}", "{guruh}", "{sabab}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! Farzandingiz {ism} {sana} kuni {guruh} guruhida darsga kelmadi. Sabab: {sabab}. {markaz}",
            Category: CategoryEducation),

        new(GradeEntered,
            "Baho qo'yilganda",
            "Jurnalda o'quvchiga baho qo'yilganda ota-onaga (yoki o'quvchiga) xabar. Qoida mavjud bo'lmasa — eski standart push saqlanadi.",
            new[] { "{ism}", "{fish}", "{guruh}", "{baho}", "{sana}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} {sana} kuni {guruh} guruhida {baho} baho oldi. {markaz}",
            Category: CategoryEducation),

        new(Birthday,
            "Tug'ilgan kun",
            "O'quvchining tug'ilgan kunida (kunlik 09:00, Toshkent) tabrik xabari.",
            new[] { "{ism}", "{fish}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli {ism}! Tug'ilgan kuningiz muborak bo'lsin! {markaz}",
            Category: CategoryEducation),

        new(StudentAdded,
            "O'quvchi guruhga qo'shilganda",
            "O'quvchi guruhga qo'shilganda (yangi o'quvchi yoki mavjud o'quvchini guruhga biriktirish) ota-onaga xush kelibsiz xabari.",
            new[] { "{ism}", "{fish}", "{guruh}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} {guruh} guruhiga qabul qilindi. Xush kelibsiz! {markaz}",
            Category: CategoryEducation),

        new(LeadNew,
            "Yangi lid kelganda",
            "Yangi lid (potentsial mijoz) qo'shilganda lidning telefoniga avtomatik SMS. Faqat SMS kanali (lidda ilova/telegram yo'q).",
            new[] { "{fish}", "{telefon}", "{fan}", "{markaz}" },
            Sms: true, Push: false, Telegram: false,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: System.Array.Empty<string>(), DefaultAudience: "parents",
            DefaultTemplate: "Assalomu alaykum {fish}! {markaz}ga qiziqishingiz uchun rahmat. Tez orada siz bilan bog'lanamiz.",
            Category: CategoryLeads),

        new(TrialReminder,
            "Sinov darsi eslatmasi (ertaga)",
            "Ertaga sinov darsi bo'ladigan lidlarga (kunlik 09:00, Toshkent) eslatma SMS. Faqat SMS kanali.",
            new[] { "{fish}", "{dars_sana}", "{dars_vaqti}", "{dars_kunlari}", "{markaz}" },
            Sms: true, Push: false, Telegram: false,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: System.Array.Empty<string>(), DefaultAudience: "parents",
            DefaultTemplate: "Assalomu alaykum {fish}! Ertaga {dars_sana} kuni {dars_vaqti}da sinov darsingiz bor. {markaz}",
            Category: CategoryLeads),

        new(TestLink,
            "Daraja-test havolasi",
            "Lidga bir martalik daraja-test havolasini SMS qilib yuborish (Lidlar → Test yuborish). Faqat SMS kanali.",
            new[] { "{fish}", "{link}", "{markaz}" },
            Sms: true, Push: false, Telegram: false,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: System.Array.Empty<string>(), DefaultAudience: "parents",
            DefaultTemplate: "Assalomu alaykum {fish}! Daraja testini shu havola orqali topshiring: {link}",
            Category: CategoryLeads),

        new(TestResult,
            "Test natijasi tayyor",
            "Daraja testi topshirilib natija chiqqanda abituriyentga (lidga) SMS. Faqat SMS kanali.",
            new[] { "{fish}", "{natija}", "{daraja}", "{ball}", "{foiz}", "{markaz}" },
            Sms: true, Push: false, Telegram: false,
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: System.Array.Empty<string>(), DefaultAudience: "parents",
            DefaultTemplate: "Assalomu alaykum {fish}! Test natijangiz: {natija}, daraja: {daraja}, ball: {ball} ({foiz}). {markaz}",
            Category: CategoryLeads),

        new(LessonAttendance,
            "Davomat eslatmasi (o'qituvchi)",
            "O'qituvchilarga davomat (jurnal) eslatmasi. Rejimlar: dars boshlangach to'ldirmaganga; kunlik belgilangan vaqtda bugun darsi bo'lib to'ldirmaganlarga; yoki hammaga.",
            new[] { "{fish}", "{guruh}", "{kurs}", "{dars_vaqti}", "{telefon}", "{markaz}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: false, SupportsSendScope: true,
            Audiences: new[] { "teachers" }, DefaultAudience: "teachers",
            DefaultTemplate: "Hurmatli {fish}! {guruh} ({kurs}) guruhida davomat jurnalini to'ldirishni unutmang. {markaz}",
            Category: CategoryEducation),

        new(CustomSchedule,
            "Jadval bo'yicha erkin eslatma",
            "O'zingiz belgilagan matn — tanlangan auditoriyaga (o'qituvchilar yoki o'quvchilar/ota-onalar) belgilangan jadval bo'yicha (har kuni yoki oyning muayyan kunida, belgilangan soatda) avtomatik xabar.",
            new[] { "{fish}", "{telefon}", "{markaz}", "{sana}", "{oy}", "{yil}" },
            Sms: true, Push: true, Telegram: true,
            SupportsSchedule: true, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "students",
            DefaultTemplate: "Hurmatli {fish}! Eslatma. {markaz}",
            Category: CategoryOther),
    };

    public static bool IsKnown(string? key) => !string.IsNullOrWhiteSpace(key) && All.Any(t => t.Key == key);

    public static TriggerInfo? Get(string? key) => All.FirstOrDefault(t => t.Key == key);
}

/// <summary>"Davomat eslatmasi" (lesson_attendance) yuborish rejimi — <see cref="IntellectCRM.Domain.AutoMessageRule.SendScope"/> qiymatlari.</summary>
public static class ReminderSendScopes
{
    /// <summary>Dars boshlangach +N daqiqada, davomat kiritilmagan bo'lsa (default — eski xatti-harakat).</summary>
    public const string LessonStart = "lesson_start";
    /// <summary>Kunlik ScheduleTime'da — bugun darsi bo'lib (boshlangan) hali to'ldirmaganlarga.</summary>
    public const string NotFilled = "not_filled";
    /// <summary>Kunlik ScheduleTime'da — BARCHA faol o'qituvchilarga (to'ldirganlarga ham).</summary>
    public const string All = "all";

    public static bool IsKnown(string? key) => key is LessonStart or NotFilled or All;
}
