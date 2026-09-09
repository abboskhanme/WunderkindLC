namespace IntellectCRM.Application.Services.Kpi;

/// <summary>
/// KUNLIK CHEKLIST SHABLONLARI — Excel «Kunlik cheklist *.xlsx» dagi ikkala ro'yxatning
/// KODDAGI nusxasi (sof ma'lumot, bazaga tegmaydi).
///
/// <para>⚠️ Bu yerdagi matn — YAGONA MANBA EMAS, faqat SEED. Bandlar bir marta
/// <c>ChecklistTemplateItem</c> qatorlariga ko'chiriladi va keyin «Qoidalar» sahifasidan
/// tahrirlanadi. <see cref="KpiSeedService"/> mavjud shablonni QAYTA YOZMAYDI — u faqat
/// <c>No</c> bo'yicha YETISHMAYDIGAN bandni qo'shadi, ya'ni rahbarning tahriri saqlanadi.</para>
///
/// <para>⚠️ <c>AutoCheckKey</c> to'liq yozilgan, lekin ularning HAMMASI qo'llanmagan:
/// <see cref="ChecklistAutoCheck.Supported"/> da yo'q kalit JIMGINA e'tiborsiz qoladi va band
/// oddiy QO'LDA belgilanadi. Bu ATAYIN — seed to'liq, avtomatlashtirish bosqichma-bosqich.</para>
///
/// <para>⚠️ <c>call_operator</c> uchun cheklist YO'Q (Excel'da ham yo'q): operator kiruvchi
/// adminning ish oqimida ishlaydi. Rol qo'shilganda shu yerga blok qo'shiladi.</para>
/// </summary>
public static class KpiChecklistSeed
{
    /// <summary>Cheklist bandi (vaqt bloki ICHIDA).</summary>
    /// <param name="No">Ekranda ko'rinadigan tartib raqami — rol ichida UNIKAL va shablonni
    /// keyinchalik to'ldirishning KALITI (mavjud band qayta qo'shilmasin).</param>
    /// <param name="KpiTag">Chiquvchi admin uchun: U (uzaytirish) | K (ketish) | Q (qarz) | T (texnik).</param>
    /// <param name="CriterionNo">Yashirin mijoz auditining mezoni (1..13) yoki null.</param>
    /// <param name="AutoCheckKey">Avtomatik tekshiruv kaliti yoki null (faqat qo'lda).</param>
    public sealed record Item(int No, string Text, string Norm, string? KpiTag, int? CriterionNo,
                              string? AutoCheckKey);

    /// <summary>Vaqt bloki: sarlavha + shu blokning bandlari.</summary>
    public sealed record Block(string TimeBlock, IReadOnlyList<Item> Items);

    /// <summary>Bazaga yoziladigan TEKIS qator (blok sarlavhasi va tartib qo'shilgan holda).</summary>
    public sealed record Row(int No, string TimeBlock, string Text, string Norm, string? KpiTag,
                             int? CriterionNo, string? AutoCheckKey, int Order);

    /// <summary>Cheklisti BOR rollar (shu tartibda seed qilinadi).</summary>
    public static IReadOnlyList<string> Roles { get; } = new[] { KpiConst.Intake, KpiConst.Retention };

    /// <summary>Shablon nomi (rolda bitta faol shablon bo'ladi).</summary>
    public static string TemplateName(string roleCode) => roleCode switch
    {
        KpiConst.Intake => "Kiruvchi admin — kunlik cheklist",
        KpiConst.Retention => "Chiquvchi admin — kunlik cheklist",
        _ => $"{KpiRuleSeed.RoleLabel(roleCode)} — kunlik cheklist",
    };

    /// <summary>Rol bo'yicha vaqt bloklari. Noma'lum rol — BO'SH ro'yxat (cheklist yo'q).</summary>
    public static IReadOnlyList<Block> Blocks(string roleCode) => roleCode switch
    {
        KpiConst.Intake => IntakeBlocks,
        KpiConst.Retention => RetentionBlocks,
        _ => Array.Empty<Block>(),
    };

    /// <summary>
    /// Rol bo'yicha TEKIS bandlar ro'yxati — <c>TimeBlock</c> va <c>Order</c> to'ldirilgan holda.
    /// </summary>
    /// <remarks>⚠️ <c>Order</c> — ro'yxatdagi O'RIN (0..N), <c>No</c> emas: bandlar keyin
    /// qo'shilsa (masalan 32-band 5-blokka) tartib baribir seed'dagi ketma-ketlik bo'lib
    /// qoladi. Ekranda esa bandlar AYNAN shu tartibda, bloklar bo'yicha guruhlanib chiqadi.</remarks>
    public static IReadOnlyList<Row> Items(string roleCode)
    {
        var rows = new List<Row>();
        foreach (var block in Blocks(roleCode))
            foreach (var item in block.Items)
                rows.Add(new Row(item.No, block.TimeBlock, item.Text, item.Norm,
                                 item.KpiTag, item.CriterionNo, item.AutoCheckKey, rows.Count));
        return rows;
    }

    /* =====================================================================================
     *  KIRUVCHI ADMIN — 31 band, 9 vaqt bloki
     * ===================================================================================== */

    private static readonly IReadOnlyList<Block> IntakeBlocks =
    [
        new("08:50 – 09:15  KUN BOSHI",
        [
            new( 1, "Yo'riqnoma va Telegram Updates kanali o'qildi", "Ishga chiqishdan oldin", null, null, null),
            new( 2, "Bugungi raqam olindi (lid / sinov / shartnoma)", "Har kuni", null, null, "auto:kpi.today_numbers"),
            new( 3, "CRM ulangani tekshirildi, MoiZvonki sozlandi", "Ish boshlashdan oldin", null, null, null),
        ]),
        new("09:15 – 09:45  TUNGI OQIM",
        [
            new( 4, "Kechqurun/tunda kelgan DM va lead formlarga javob berildi", "09:45 gacha 100%", null, 3, "auto:dm.night_answered"),
            new( 5, "Kechagi javobsiz qo'ng'iroqlar qaytarildi", "09:45 gacha 100%", null, 1, "auto:calls.missed_returned"),
            new( 6, "Barcha lidlar CRMga avtomatik tushgani tekshirildi", "Qo'lda kiritish = 0", null, 7, null),
        ]),
        new("09:45 – 11:30  FAOL OBZVON #1",
        [
            new( 7, "Kiruvchi qo'ng'iroqlar 3-gudokgacha olindi", "3 gudok", null, 1, "auto:calls.answer_speed"),
            new( 8, "Yangi lid 15 daqiqa ichida ishga olindi", "15 daqiqa", null, 1, null),
            new( 9, "Ism + kim uchun + maqsad + muddat so'raldi va CRMga yozildi", "Lidlarning 95%", null, 2, "auto:leads.answers_filled"),
            new(10, "Har suhbatda kamida 1 ta RAQAMLI natija aytildi", "100%", null, 5, null),
            new(11, "Kvalifikatsiyadan keyin sinov darsiga taklif qilindi", "100%", null, 6, null),
            new(12, "E'tirozga skript bo'yicha javob berildi", "Har e'tirozda", null, 9, null),
        ]),
        new("11:30 – 12:00  INSTAGRAM / TELEGRAM",
        [
            new(13, "Javobsiz qolgan barcha dialoglar yopildi", "15 daqiqa SLA", null, 3, "auto:dm.open_dialogs"),
            new(14, "DM lidlari CRMda sdelka bo'lib ochildi", "100%", null, 7, "auto:dm.leads_created"),
        ]),
        new("13:00 – 15:00  FAOL OBZVON #2",
        [
            new(15, "Bugunga rejalashtirilgan qayta qo'ng'iroqlar bajarildi", "100%", null, 8, "auto:tasks.due_done"),
            new(16, "«Javob bermadi» ssenariysi bo'yicha keyingi qadam belgilandi", "Har lidda", null, 8, null),
            new(17, "4-urinishda boshqa raqamdan qo'ng'iroq qilindi", "4-urinish", null, 8, null),
        ]),
        new("15:00 – 16:30  OFFLAYN OQIM",
        [
            new(18, "Mehmon «Kutib olish eslatmasi» o'qilgach, ISM bilan kutib olindi", "Har mehmon", null, 13, null),
            new(19, "Kelgan har bir mehmon CRMga kiritildi (manba = offlayn)", "100%", null, 7, null),
            new(20, "Sinov darsidan keyin natija va tavsiya guruh CRMga yozildi", "Shu kuni", null, null, null),
            new(21, "Shartnoma imzolangach o'quvchi LMSga o'tkazildi", "Shu kuni", null, null, "auto:leads.converted"),
        ]),
        new("16:30 – 17:15  ERTANGI SINOV DARSLARI",
        [
            new(22, "«Ertaga sinov darsi» ro'yxati ochildi", "Har kuni", null, null, null),
            new(23, "Ertangi mehmonlar qo'ng'iroq/xabar bilan tasdiqlandi", "100%", null, null, null),
            new(24, "Har biriga «Kutib olish eslatmasi» to'ldirildi", "100%", null, 2, null),
            new(25, "O'qituvchi va xona band qilindi", "Har sinov darsi", null, null, null),
        ]),
        new("17:15 – 17:45  CRM GIGIYENASI",
        [
            new(26, "Kechikkan vazifa", "0 ta", null, null, "auto:tasks.overdue_zero"),
            new(27, "Vazifasiz sdelka", "0 ta", null, null, "auto:leads.without_task_zero"),
            new(28, "Etap majburiy maydonlari to'liq to'ldirilgan", "100%", null, null, null),
            new(29, "Yopilgan sdelkalarda rad etish sababi ko'rsatilgan", "100%", null, null, null),
        ]),
        new("17:45 – 18:00  KUN YAKUNI",
        [
            new(30, "Kunlik raqamlar Telegramga yuborildi", "18:00 gacha", null, null, "auto:kpi.digest_sent"),
            new(31, "Ertangi ustuvor vazifalar belgilandi", "Har kuni", null, null, null),
        ]),
    ];

    /* =====================================================================================
     *  CHIQUVCHI ADMIN — 35 band, 7 vaqt bloki (KPI teglari: U / K / Q / T)
     *
     *  ⚠️ Spetsifikatsiya sarlavhasida «8 vaqt bloki» deyilgan, lekin ro'yxatning O'ZIDA 7 ta
     *  sarlavha bor: 29–32-bandlar (ketish sababi) alohida blok emas, «13:30 – 16:50
     *  UZAYTIRISH» bloki ichida. Seed ro'yxatga AYNAN amal qiladi — sarlavhani "tuzatib"
     *  qo'shsak, band matni Excel bilan mos kelmay qolardi.
     * ===================================================================================== */

    private static readonly IReadOnlyList<Block> RetentionBlocks =
    [
        new("07:50 – 08:00  KUN BOSHI",
        [
            new( 1, "Yo'riqnoma va Telegram Updates kanali o'qildi", "Ishga chiqishdan oldin", null, null, null),
            new( 2, "Kechagi ketish/qarzdorlik raqami olindi", "Har kuni", null, null, "auto:kpi.today_numbers"),
        ]),
        new("08:00 – 08:40  BINO VA XONA NAZORATI",
        [
            new( 3, "Barcha xonalar aylanib chiqildi: toza, havo almashtirilgan", "Har xona", "T", null, null),
            new( 4, "Texnika tekshirildi: proyektor, kolonka, konditsioner, yorug'lik", "Har xona", "T", null, null),
            new( 5, "Doska, marker, o'chirg'ich, stul soni joyida", "Har xona", "T", null, null),
            new( 6, "Kutish zonasi: tartibli, suv bor, o'tirish joylari toza", "Har kuni", "T", null, null),
            new( 7, "Hojatxona va rakvina joyi tekshirildi", "Kuniga 2 marta", "T", null, null),
            new( 8, "Devordagi natijalar taxtasi va e'lonlar dolzarb", "Haftada yangilanadi", "T", null, null),
            new( 9, "Aniqlangan nosozliklar ro'yxatga olindi va rahbarga yuborildi", "Shu kuni", "T", null, null),
        ]),
        new("08:40 – 09:00  DAVOMAD",
        [
            new(10, "Kechagi barcha guruhlar davomadi tekshirildi", "100% guruh", "K", null, "auto:journal.yesterday_filled"),
            new(11, "Kelmagan har bir o'quvchiga xabar yuborildi", "24 soat ichida", "K", null, "auto:sms.absence_sent"),
            new(12, "Ketma-ket 2 dars kelmaganlar ro'yxati tuzildi", "Har kuni", "K", null, "auto:journal.streak2"),
            new(13, "Ketma-ket 3 dars kelmaganlarga QO'NG'IROQ qilindi (xabar emas)", "Shu kuni", "K", null, "auto:journal.streak3_called"),
            new(14, "Sabab CRMga yozildi", "Har holat", "K", null, null),
        ]),
        new("09:00 – 10:30  QARZDORLAR BILAN ISHLASH",
        [
            new(15, "Bugungi to'lov muddati kelganlar ro'yxati ochildi", "Har kuni", "Q", null, null),
            new(16, "Muddati kelganlarga eslatma xabar yuborildi", "100%", "Q", null, "auto:sms.payment_reminder"),
            new(17, "5 kundan ortiq qarzi borlarga qo'ng'iroq qilindi", "100%", "Q", null, "auto:contacts.debtor_called"),
            new(18, "To'lov va'da sanasi CRMga yozildi", "Har holat", "Q", null, null),
            new(19, "15 kundan ortiq qarz — rahbarga eskalatsiya qilindi", "Darhol", "Q", null, null),
        ]),
        new("11:00 – 12:30  O'QITUVCHILAR VA MATERIALLAR",
        [
            new(20, "O'qituvchilarning material/jihoz so'rovlari yig'ildi", "Har kuni", "T", null, null),
            new(21, "Kechagi so'rovlar bajarildi yoki muddat aytildi", "2 kun ichida", "T", null, "auto:support.sla2"),
            new(22, "Darslik, kopiya", "Har kuni", "T", null, null),
            new(23, "Yetishmayotgan narsalar xaridi ro'yxatga qo'shildi", "Haftalik", "T", null, null),
        ]),
        new("13:30 – 16:50  UZAYTIRISH",
        [
            new(24, "Kursi 3 hafta ichida tugaydigan guruhlar ro'yxati ochildi", "Har kuni", "U", null, "auto:groups.ending_soon"),
            new(25, "Har bir o'quvchi bilan natija suhbati o'tkazildi (o'qituvchi bahosi bilan)", "Kursdan 3 hafta oldin", "U", null, null),
            new(26, "Keyingi bosqich va guruh taklif qilindi, narx aytildi", "Har o'quvchi", "U", null, null),
            new(27, "Ota-onaga natija haqida xabar/qo'ng'iroq qilindi", "Har o'quvchi", "U", null, null),
            new(28, "Uzaytirish javobi CRMga yozildi (ha / yo'q / o'ylayapti + sana)", "100%", "U", null, null),
            new(29, "Bugun kursni tashlab ketganlar aniqlandi", "Har kuni", "K", null, "auto:students.left_today"),
            new(30, "Har biriga qo'ng'iroq qilinib, KETISH SABABI aniqlandi", "100% — majburiy", "K", null, null),
            new(31, "Sabab CRMga aniq variant bilan yozildi («noma'lum» taqiqlanadi)", "100%", "K", null, "auto:students.reason_filled"),
            new(32, "Qaytarish imkoni bor holatlar rahbarga aytildi", "Shu kuni", "K", null, null),
        ]),
        new("16:50 – 17:20  KUN YAKUNI",
        [
            new(33, "Kechikkan vazifa = 0, vazifasiz sdelka = 0", "Har kuni", null, null, "auto:tasks.overdue_zero"),
            new(34, "Kunlik raqamlar Telegramga yuborildi", "17:00 gacha", null, null, "auto:kpi.digest_sent"),
            new(35, "Ertangi ustuvor ish belgilandi", "Har kuni", null, null, null),
        ]),
    ];
}
