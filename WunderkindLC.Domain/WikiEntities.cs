namespace WunderkindLC.Domain;

/* =====================================================================================
 *  BILIMLAR BAZASI (wiki) — xodimlarning SHAXSIY eslatmalari (insaytlar, ish rejasi, qaydlar).
 *  "Bilimlar bazasi" (`/admin/bilimlar`): «Eslatmalarim» va (rahbariyat/menejerda) «Xodimlar».
 *  Qoidalar: `.claude/rules/wiki.md`.
 *
 *  ⚠️ FAQAT MATN: rasm/video/audio/fayl bloklari YO'Q (klient sxemasida ham, serverda ham —
 *  `WikiRules.ParseContent` boshqa blok turini rad etadi). Shuning uchun `/uploads` ga hech narsa
 *  tushmaydi va `uploads-security.md` qoidalari bu modulga tegmaydi.
 * ===================================================================================== */

/// <summary>
/// Bitta ESLATMA (sahifa). Egasining eslatmalari daraxt bo'lib turadi (<see cref="ParentId"/>).
/// Matn BlockNote JSON ko'rinishida (<see cref="ContentJson"/>), qidiruv uchun esa oddiy matn
/// nusxasi (<see cref="ContentText"/>) — ikkalasi har saqlashda BIRGA yoziladi.
/// </summary>
public class WikiPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>
    /// EGASI (`AppUser.Id`). Eslatmani egasi yozadi; rahbariyat ham (xodimga sahifa qo'shadi, tahrirlaydi, qulflaydi).
    /// Kim o'qiy olishi — `WikiViewer.CanSeeNotesOf` (o'zi · rahbariyat · HR daraxti bo'yicha menejer).
    /// </summary>
    public string OwnerUserId { get; set; } = string.Empty;
    /// <summary>Ota eslatma (shu egada). "" = yuqori daraja.</summary>
    public string ParentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>Bitta emoji — daraxtda sarlavha oldida. Bo'sh bo'lishi mumkin.</summary>
    public string Icon { get; set; } = string.Empty;
    /// <summary>BlockNote bloklari (JSON massiv). "" = bo'sh sahifa.</summary>
    public string ContentJson { get; set; } = string.Empty;
    /// <summary>Bloklardan olingan oddiy matn — FAQAT qidiruv uchun (ekranda ko'rsatilmaydi).</summary>
    public string ContentText { get; set; } = string.Empty;
    /// <summary>Bir ota ichidagi tartib (kichigi tepada).</summary>
    public int Order { get; set; }

    /// <summary>
    /// BO'LIM — chap panelda «Mening eslatmalarim» ostidagi sarlavha (papka): matni yo'q, faqat ichida sahifalar turadi.
    /// Har doim YUQORI darajada (<see cref="ParentId"/> = ""), boshqa sahifa ichiga ko'chmaydi. Qulflansa — ichidagi
    /// hamma sahifa egasiga faqat o'qish (meros odatdagidek).
    /// </summary>
    public bool IsSection { get; set; }

    /// <summary>Har saqlashda oshadi. Klient eski versiya ustidan yozmoqchi bo'lsa — 409
    /// (ikki kishi bir sahifani bir vaqtda tahrirlasa, biri ikkinchisinikini jimgina o'chirmasin).</summary>
    public int Version { get; set; } = 1;

    /// <summary>QULF (faqat rahbariyat qo'yadi): shu eslatma VA ichidagilari EGASINING O'ZIGA ham faqat o'qish uchun.</summary>
    public bool Locked { get; set; }
    public string LockedBy { get; set; } = string.Empty;
    public string LockedAt { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Bilimlar bazasi SOZLAMASI — BITTA qator (Id = <see cref="SingletonId"/>). Faqat rahbariyat o'zgartiradi.
/// </summary>
public class WikiSettings
{
    public const string SingletonId = "wiki";
    public string Id { get; set; } = SingletonId;

    /// <summary>HR menejeri o'ziga bo'ysunuvchilarning (HR daraxti) eslatmalarini ko'radimi.
    /// O'chiq bo'lsa — eslatmalarni faqat egasi va rahbariyat ko'radi.</summary>
    public bool ManagersSeeTeamNotes { get; set; } = true;

    /// <summary>Menejer qaysi LAVOZIMDAGI (`HrPosition.Id`) bo'ysunuvchilarning eslatmalarini ko'radi.
    /// BO'SH ro'yxat = barcha lavozimlar.</summary>
    public List<string> ManagerVisiblePositionIds { get; set; } = new();

    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}
