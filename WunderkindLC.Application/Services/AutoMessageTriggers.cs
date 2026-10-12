using System.Linq;

namespace WunderkindLC.Application.Services;

/// <summary>
/// YAGONA avto-xabar hodisalari katalogi. Har bir <see cref="WunderkindLC.Domain.AutoMessageRule"/>
/// shu ro'yxatdagi bitta kalitga (Trigger) bog'lanadi. Yangi hodisa qo'shilsa — shu yerga yozib qo'yish
/// kifoya: frontend forma va backend validatsiya shu katalogdan avtomatik ishlaydi.
///
/// Bu eski <c>AutoSmsService</c> (faqat SMS) + <c>ReminderTriggers</c> (faqat push/telegram) ro'yxatlarini
/// birlashtiradi va yangi hodisalarni (monthly_charge, attendance_absent, student_added) qo'shadi.
///
/// DIQQAT: <see cref="TriggerInfo.Tokens"/> — <c>MessageTokenizer</c>/dispatcher haqiqatan qo'llab-quvvatlaydigan
/// ANIQ token nomlari (o'ylab topilmagan). Yetkazish: kabinet bildirishnomasi + Telegram
/// (<see cref="AutoMessageChannels"/>). 2026-10: SMS/push kanallari va faqat-SMS lid hodisalari
/// (yangi lid, sinov darsi eslatmasi, daraja testi havolasi/natijasi) katalogdan olib tashlandi.
/// </summary>
public static class AutoMessageTriggers
{
    public const string PaymentReceived = "payment_received";
    public const string MonthlyCharge = "monthly_charge";
    public const string PaymentDebt = "payment_debt";
    public const string AttendanceAbsent = "attendance_absent";
    public const string AttendancePresent = "attendance_present";
    public const string GradeEntered = "grade_entered";
    public const string Birthday = "birthday";
    public const string StudentAdded = "student_added";
    public const string LessonAttendance = "lesson_attendance";
    public const string CustomSchedule = "custom_schedule";
    // CRM upgrade — Phase 2 (docs/SPEC-crm-upgrade.md P2.5). test_score / rating_* are dispatched by Phase 4.
    public const string BotConnected = "bot_connected";
    public const string Achievement = "achievement";
    public const string TestScore = "test_score";
    public const string RatingTop = "rating_top";
    public const string RatingBottom = "rating_bottom";

    // Hodisa toifalari (frontend guruhlab ko'rsatadi).
    public const string CategoryLeads = "Lidlar";
    public const string CategoryEducation = "O'quv jarayoni";
    public const string CategoryFinance = "Moliya";
    public const string CategoryOther = "Boshqa";

    /// <param name="Category">Toifa (frontend guruhlash uchun): "Lidlar" | "O'quv jarayoni" | "Moliya" | "Boshqa".</param>
    public record TriggerInfo(
        string Key,
        string Label,
        string Description,
        string[] Tokens,
        bool SupportsSchedule,
        bool SupportsSendScope,
        string[] Audiences,
        string DefaultAudience,
        string DefaultTemplate,
        string Category,
        /// <summary>Matn IXTIYORIY — bo'sh qoldirilsa tizim matnni o'zi tuzadi (qarzdorlik eslatmasi kabi).</summary>
        bool TemplateOptional = false,
        /// <summary>«✅ Qabul qildim» tugmasi YANGI qoidada oldindan yoqilganmi (CRM upgrade P2.5).</summary>
        bool DefaultAck = false,
        /// <summary>Yangi qoidaning oyning kuni / vaqti (<c>monthly_day_time</c> va <c>daily_time</c> rejimlari).</summary>
        int DefaultDay = 1,
        string DefaultTime = "09:00")
    {
        /// <summary>Jadval rejimi — <see cref="Messaging.AutoMessageSchedule.ModeOf"/>.</summary>
        public string ScheduleMode => Messaging.AutoMessageSchedule.ModeOf(Key);
    }

    // Umumiy token to'plamlari (takrorni kamaytirish uchun).
    private static readonly string[] ParentsAll = { "{ism}", "{fish}", "{guruh}", "{qarzdorlik}", "{markaz}", "{telefon}" };

    public static readonly TriggerInfo[] All =
    {
        new(PaymentReceived,
            "To'lov qabul qilinganda",
            "O'quvchining oylik (tuition) to'lovi qabul qilinganda ota-onaga (yoki o'quvchiga) tasdiq xabari. {oy} — to'lov QAYSI OY uchun ekani (kiritilgan sanadan emas, tanlangan oydan). {kurs}/{guruh} — to'lov QAYSI guruh (kurs) uchun ekani: o'quvchi bir necha guruhda o'qisa har to'lov alohida yoziladi, demak har biriga o'z kursi nomi bilan alohida xabar boradi.",
            new[] { "{ism}", "{fish}", "{summa}", "{oy}", "{sana}", "{qarzdorlik}", "{kurs}", "{guruh}", "{telefon}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism}ning {kurs} kursi ({guruh}) uchun {oy} oyi to'lovi qabul qilindi. Summa: {summa}. Qarzdorlik: {qarzdorlik}. {markaz}",
            Category: CategoryFinance),

        new(MonthlyCharge,
            "Oylik hisob yaratilganda",
            "Har oy avtomatik oylik to'lov hisobi yaratilganda ota-onaga qancha hisoblangani va umumiy qarzdorlik haqida xabar.",
            new[] { "{ism}", "{fish}", "{oy}", "{summa}", "{qarzdorlik}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} uchun {oy} oyi to'lovi hisoblandi: {summa}. Umumiy qarzdorlik: {qarzdorlik}. {markaz}",
            Category: CategoryFinance),

        new(PaymentDebt,
            "Qarzdorlik eslatmasi",
            "Qarzi bor o'quvchilarga har oyning tanlangan kunida, belgilangan soatda eslatma. Xabar HAR FAN uchun alohida yuboriladi: o'quvchi ikki fanda o'qisa ikkita alohida xabar ketadi — {qarzdorlik}, {kurs}, {guruh} va {oqituvchi} har birida SHU fanning qiymati bo'ladi. Matn IXTIYORIY: bo'sh qoldirsangiz tizim matnni o'zi tuzadi (\"F.I.Sh — Kurs bo'yicha qarzdorlik: summa\").",
            new[] { "{ism}", "{fish}", "{qarzdorlik}", "{kurs}", "{guruh}", "{oqituvchi}", "{telefon}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "",
            Category: CategoryFinance,
            TemplateOptional: true,
            DefaultAck: true),

        new(AttendanceAbsent,
            "O'quvchi darsga kelmaganda",
            "Jurnalda o'quvchiga davomat sababi (kelmadi) belgilanganda ota-onaga xabar.",
            new[] { "{ism}", "{fish}", "{sana}", "{guruh}", "{sabab}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! Farzandingiz {ism} {sana} kuni {guruh} guruhida darsga kelmadi. Sabab: {sabab}. {markaz}",
            Category: CategoryEducation,
            DefaultAck: true),

        new(AttendancePresent,
            "O'quvchi darsga kelganda",
            "Davomatda o'quvchi «keldi» deb belgilanganda (yakka katak yoki «hammasi keldi») ota-onaga xabar. Har dars uchun BIR MARTA — qayta saqlansa yoki qayta «hammasi keldi» bosilsa takror ketmaydi. ⚠️ Har kuni har o'quvchiga ketadi.",
            new[] { "{ism}", "{fish}", "{sana}", "{guruh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! Farzandingiz {ism} {sana} kuni {guruh} guruhida darsga keldi. {markaz}",
            Category: CategoryEducation),

        new(GradeEntered,
            "Baho qo'yilganda",
            "Jurnalda o'quvchiga baho qo'yilganda ota-onaga (yoki o'quvchiga) xabar. Qoida mavjud bo'lmasa — o'quvchi kabinetiga standart bildirishnoma yoziladi.",
            new[] { "{ism}", "{fish}", "{guruh}", "{baho}", "{sana}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} {sana} kuni {guruh} guruhida {baho} baho oldi. {markaz}",
            Category: CategoryEducation),

        new(Birthday,
            "Tug'ilgan kun",
            "O'quvchining tug'ilgan kunida, belgilangan soatda tabrik.",
            new[] { "{ism}", "{fish}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli {ism}! Tug'ilgan kuningiz muborak bo'lsin! {markaz}",
            Category: CategoryEducation,
            DefaultTime: "09:00"),

        new(StudentAdded,
            "O'quvchi guruhga qo'shilganda",
            "O'quvchi guruhga qo'shilganda (yangi o'quvchi yoki mavjud o'quvchini guruhga biriktirish) ota-onaga xush kelibsiz xabari.",
            new[] { "{ism}", "{fish}", "{guruh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {ism} {guruh} guruhiga qabul qilindi. Xush kelibsiz! {markaz}",
            Category: CategoryEducation),

        new(LessonAttendance,
            "Davomat eslatmasi (o'qituvchi)",
            "O'qituvchilarga davomat (jurnal) eslatmasi. Rejimlar: darsdan OLDIN yoki dars boshlangach (to'ldirmaganga); kunlik belgilangan vaqtda bugun darsi bo'lib to'ldirmaganlarga; yoki hammaga.",
            new[] { "{fish}", "{guruh}", "{kurs}", "{dars_vaqti}", "{telefon}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: true,
            Audiences: new[] { "teachers" }, DefaultAudience: "teachers",
            DefaultTemplate: "Hurmatli {fish}! {guruh} ({kurs}) guruhida davomat jurnalini to'ldirishni unutmang. {markaz}",
            Category: CategoryEducation),

        new(CustomSchedule,
            "Jadval bo'yicha erkin eslatma",
            "O'zingiz belgilagan matn — tanlangan auditoriyaga (o'qituvchilar yoki o'quvchilar/ota-onalar) belgilangan jadval bo'yicha (har kuni yoki oyning muayyan kunida, belgilangan soatda) avtomatik xabar.",
            new[] { "{fish}", "{telefon}", "{markaz}", "{sana}", "{oy}", "{yil}" },
            SupportsSchedule: true, SupportsSendScope: false,
            Audiences: new[] { "parents", "students", "teachers" }, DefaultAudience: "students",
            DefaultTemplate: "Hurmatli {fish}! Eslatma. {markaz}",
            Category: CategoryOther),

        // ---------- CRM upgrade — Phase 2 (Telegram) ----------
        new(BotConnected,
            "Botga ulanganda (01-xabar)",
            "Ota-ona (yoki o'quvchi) botga telefon raqamini yuborib ulanganda — FAQAT o'sha ulangan chatga bir martalik minnatdorchilik xabari. {qarindosh} — kim ulangani (Otasi / Onasi / O'zi).",
            new[] { "{ism}", "{fish}", "{qarindosh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents" }, DefaultAudience: "parents",
            DefaultTemplate: "Assalomu alaykum! {markaz} botiga ulanganingiz uchun rahmat 🙏\nEndi {ism}ning davomati, to'lovlari va natijalari haqidagi xabarlarni shu yerda olasiz.",
            Category: CategoryOther),

        new(Achievement,
            "Yutuq (sertifikat / IELTS / CEFR)",
            "O'quvchi profiliga yutuq (IELTS, CEFR, sertifikat, olimpiada) qo'shilganda ota-onaga tabrik. Yutuqda rasm bo'lsa xabar bilan birga yuboriladi.",
            new[] { "{ism}", "{fish}", "{yutuq}", "{yutuq_natija}", "{sana}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "🎉 Tabriklaymiz! {fish} — {yutuq} {yutuq_natija}. Farzandingiz bilan faxrlanamiz! {markaz}",
            Category: CategoryEducation),

        new(TestScore,
            "Test natijasi (ota-onaga)",
            "Test sahifasidagi «Ota-onalarga yuborish» tugmasi bosilganda har o'quvchining natijasi ota-onasiga yuboriladi (o'zgarmagan natija qayta yuborilmaydi).",
            new[] { "{ism}", "{fish}", "{test}", "{turi}", "{sana}", "{ball}", "{foiz}", "{orin}", "{guruh_orin}", "{reyting_orin}", "{guruh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "📝 {test} ({turi}, {sana})\n{fish}: {ball} — {foiz}\nTestdagi o'rni: {orin}\nMarkaz reytingi: {reyting_orin}\n{markaz}",
            Category: CategoryEducation),

        new(RatingTop,
            "Oylik TOP — tashakkurnoma",
            "Har oyning tanlangan kunida o'tgan oy test natijalari bo'yicha eng yaxshi o'quvchilarning ota-onalariga tashakkurnoma.",
            new[] { "{ism}", "{fish}", "{oy}", "{orin}", "{foiz}", "{guruh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "🏆 Tabriklaymiz! {fish} {oy} oyida test natijalari bo'yicha eng yaxshilar qatorida: o'rni {orin}, o'rtacha natija {foiz}. Rahmat! {markaz}",
            Category: CategoryEducation,
            DefaultDay: 3, DefaultTime: "10:00"),

        new(RatingBottom,
            "Oylik quyi natija — e'tibor",
            "Har oyning tanlangan kunida o'tgan oy test natijalari guruhda eng past bo'lgan o'quvchilarning ota-onalariga e'tibor xabari.",
            new[] { "{ism}", "{fish}", "{oy}", "{orin}", "{foiz}", "{guruh}", "{markaz}" },
            SupportsSchedule: false, SupportsSendScope: false,
            Audiences: new[] { "parents", "students" }, DefaultAudience: "parents",
            DefaultTemplate: "Hurmatli ota-ona! {fish}ning {oy} oyidagi test natijalari ({foiz}) guruhda pastroq (o'rni {orin}). Iltimos, tayyorgarligiga e'tibor qarating; savollar bo'lsa o'qituvchi bilan bog'laning. {markaz}",
            Category: CategoryEducation,
            DefaultDay: 3, DefaultTime: "10:00"),
    };

    public static bool IsKnown(string? key) => !string.IsNullOrWhiteSpace(key) && All.Any(t => t.Key == key);

    /// <summary>
    /// ARXIVDAGI o'quvchiga ham yuboriladigan hodisa — FAQAT to'lov kvitansiyasi
    /// (<see cref="PaymentReceived"/>): eski qarzini to'lagan ota-ona tasdiqni olishi kerak.
    /// Qolgan hammasi (davomat, baho, guruhga qo'shilish, oylik hisob, eslatmalar) arxivdagi
    /// o'quvchiga ketmaydi — arxiv = o'quvchi markazdan ketdi.
    /// </summary>
    public static bool ReachesArchived(string? trigger) => trigger == PaymentReceived;

    public static TriggerInfo? Get(string? key) => All.FirstOrDefault(t => t.Key == key);
}

/// <summary>"Davomat eslatmasi" (lesson_attendance) yuborish rejimi — <see cref="WunderkindLC.Domain.AutoMessageRule.SendScope"/> qiymatlari.</summary>
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
