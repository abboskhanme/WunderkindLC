using System.Text;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KURS katalogi qoidalari (Kurslar sahifasi, <c>SubjectsController</c>) — sof funksiyalar va
/// SaveChanges QILMAYDIGAN bazaviy yordamchilar (testlangan: <c>AuditFixT10Tests</c>).
/// </summary>
public static class CourseCatalog
{
    /// <summary>Saqlash uchun nom: chetdagi va ichma-ich ortiqcha bo'shliqlar olib tashlanadi.</summary>
    public static string CleanName(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Solishtirish kaliti: <see cref="CleanName"/> + registrdan qat'i nazar + apostroflar BIR ko'rinishga
    /// (<c>'</c> <c>ʻ</c> <c>’</c> <c>`</c> <c>ʼ</c> <c>‘</c>) — "Oʻzbek tili" va "o'zbek  tili" BIR kurs.
    /// Matn turli klaviaturalardan kiritiladi (contacts.md §7.5 dagi bilan bir xil sabab).
    /// </summary>
    public static string NameKey(string? name)
    {
        var sb = new StringBuilder();
        foreach (var ch in CleanName(name).ToLowerInvariant())
            sb.Append(ch is 'ʻ' or '’' or '`' or 'ʼ' or '‘' or '´' ? '\'' : ch);
        return sb.ToString();
    }

    /// <summary>
    /// NOM BANDMI: boshqa kursda (<paramref name="exceptId"/> dan tashqari) AYNAN shu nom (kalit bo'yicha)
    /// bo'lsa — o'sha kursning nomi, aks holda <c>null</c>. Kurslar soni kichik — xotirada solishtiriladi
    /// (registr/apostrof qoidasini SQL'da provayderdan qat'i nazar ifodalab bo'lmaydi).
    /// </summary>
    public static async Task<string?> NameTakenByAsync(
        IAppDbContext db, string name, string? exceptId, CancellationToken ct = default)
    {
        var key = NameKey(name);
        var all = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        return all.FirstOrDefault(s => s.Id != exceptId && NameKey(s.Name) == key)?.Name;
    }

    /// <summary>Kursga HAVOLA qilayotgan yozuvlar soni — o'chirishdan oldingi darvoza.</summary>
    public sealed record References(
        int Groups, int ArchivedGroups, int Certificates, int CertificateTemplates,
        int BonusTracks, int BonusAwards, int LevelTests, int Extensions)
    {
        public int Total => Groups + ArchivedGroups + Certificates + CertificateTemplates
                            + BonusTracks + BonusAwards + LevelTests + Extensions;

        /// <summary>O'zbekcha rad etish sababi (sanoqlar bilan); havola yo'q bo'lsa <c>null</c>.</summary>
        public string? BlockMessage()
        {
            if (Total == 0) return null;
            var parts = new List<string>();
            if (Groups > 0) parts.Add($"{Groups} ta faol guruh");
            if (ArchivedGroups > 0) parts.Add($"{ArchivedGroups} ta arxivdagi guruh");
            if (Certificates > 0) parts.Add($"{Certificates} ta berilgan sertifikat");
            if (CertificateTemplates > 0) parts.Add($"{CertificateTemplates} ta sertifikat andozasi");
            if (BonusTracks + BonusAwards > 0) parts.Add($"{BonusTracks + BonusAwards} ta ushlab turish bonusi yozuvi");
            if (LevelTests > 0) parts.Add($"{LevelTests} ta daraja testi");
            if (Extensions > 0) parts.Add($"{Extensions} ta uzaytirish yozuvi");
            return "Kursni o'chirib bo'lmaydi — unga bog'langan: " + string.Join(", ", parts)
                   + ". Tarix va pul hisobi shu kursga tayanadi; avval guruhlarni boshqa kursga o'tkazing yoki kursni o'z holicha qoldiring.";
        }
    }

    /// <summary>
    /// Kursga havola qiluvchi yozuvlarni sanaydi. ⚠️ ARXIVDAGI guruhlar ham sanaladi — ular orqali kurs
    /// analitikasi (keldi/ketdi) va o'tgan oylarning hisobi o'qiladi (course-analytics.md §5).
    /// Jurnal yozuvlari (<c>JournalEntry.SubjectId</c>) alohida sanalmaydi: ular guruhga tegishli va
    /// guruh darvozasi ularni qoplaydi.
    /// </summary>
    public static async Task<References> CountReferencesAsync(IAppDbContext db, string courseId, CancellationToken ct = default)
    {
        var groups = await db.Classes.Where(g => g.CourseId == courseId)
            .GroupBy(g => g.IsArchived).Select(g => new { Archived = g.Key, Count = g.Count() }).ToListAsync(ct);
        return new References(
            Groups: groups.Where(g => !g.Archived).Sum(g => g.Count),
            ArchivedGroups: groups.Where(g => g.Archived).Sum(g => g.Count),
            Certificates: await db.StudentCertificates.CountAsync(c => c.CourseId == courseId, ct),
            CertificateTemplates: await db.CertificateTemplates.CountAsync(t => t.CourseId == courseId, ct),
            BonusTracks: await db.RetentionBonusTracks.CountAsync(t => t.CourseId == courseId, ct),
            BonusAwards: await db.RetentionBonusAwards.CountAsync(a => a.CourseId == courseId, ct),
            LevelTests: await db.LevelTests.CountAsync(t => t.CourseId == courseId, ct),
            Extensions: await db.StudentExtensions.CountAsync(e => e.CourseId == courseId, ct));
    }

    /// <summary>
    /// O'chirishga tayyorlash (SaveChanges CHAQIRUVCHIDA — hammasi BITTA tranzaksiyada saqlanadi):
    /// biriktirilgan o'quv dasturlari bog'lanishi va sinf narxlari olib tashlanadi, kurs o'qituvchilarning
    /// "o'tadigan fanlari" ro'yxatidan chiqariladi. Dasturlarning O'ZI (Curriculum) tegilmaydi.
    /// Ilgari dasturlar alohida <c>ExecuteDeleteAsync</c> bilan DARHOL o'chirilar, keyingi SaveChanges
    /// yiqilsa ham qaytmasdi.
    /// </summary>
    public static async Task<(int Curricula, int Teachers)> DetachForDeleteAsync(
        IAppDbContext db, string courseId, CancellationToken ct = default)
    {
        var links = await db.SubjectCurricula.Where(sc => sc.SubjectId == courseId).ToListAsync(ct);
        db.SubjectCurricula.RemoveRange(links);
        db.SubjectPriceBands.RemoveRange(await db.SubjectPriceBands.Where(b => b.SubjectId == courseId).ToListAsync(ct));
        // `List<string>` ustuni provayderga qarab (Npgsql massiv / SQLite JSON) SQL'da turlicha — o'qituvchilar
        // soni kichik, xotirada filtrlanadi.
        var teachers = (await db.Teachers.ToListAsync(ct)).Where(t => t.SubjectIds.Contains(courseId)).ToList();
        foreach (var t in teachers)
            t.SubjectIds = t.SubjectIds.Where(id => id != courseId).ToList();
        return (links.Count, teachers.Count);
    }

    /// <summary>
    /// Kurs nomi o'zgarganda lidlarning «Qiziqqan fani» (<c>Lead.InterestSubject</c> — NOM saqlaydi)
    /// ko'chiriladi. Kuzatiladigan (tracked) o'zgarish — chaqiruvchining SaveChanges'i bilan BIR
    /// tranzaksiyada saqlanadi va kesh (<c>Lead</c> turi) interceptor orqali avtomatik yangilanadi
    /// (ilgari commit'dan KEYIN <c>ExecuteUpdateAsync</c> bilan, kesh yangilanmasdan bajarilardi).
    ///
    /// <para>⚠️ Eski nom BOSHQA kursda ham qolgan bo'lsa (noyoblik qoidasidan oldingi takror nomlar)
    /// ko'chirilmaydi — aks holda o'sha kursning lidlari ham yangi nomga o'tib ketardi.</para>
    /// </summary>
    public static async Task<int> RelabelLeadsAsync(
        IAppDbContext db, string courseId, string oldName, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(oldName) || oldName == newName) return 0;
        if (await NameTakenByAsync(db, oldName, courseId, ct) is not null) return 0;
        var leads = await db.Leads.Where(l => l.InterestSubject == oldName).ToListAsync(ct);
        foreach (var l in leads) l.InterestSubject = newName;
        return leads.Count;
    }
}

/// <summary>Xonalar (<c>RoomsController</c>) qoidalari.</summary>
public static class RoomRules
{
    /// <summary>
    /// Tahrirdan keyingi faollik: <c>null</c> = TEGILMAYDI. Ilgari so'rovdagi <c>bool</c> majburiy edi,
    /// klient uni yubormas edi va u <c>false</c> bo'lib bog'lanardi — sig'imni tuzatish xonani
    /// ro'yxatdan, guruh formasidan va analitikadan yo'qotardi.
    /// </summary>
    public static bool ResolveIsActive(bool current, bool? requested) => requested ?? current;

    /// <summary>Xonaga biriktirilgan ARXIVLANMAGAN guruhlar soni — o'chirish (nofaol qilish) darvozasi.</summary>
    public static Task<int> LiveGroupsAsync(IAppDbContext db, string roomId, CancellationToken ct = default) =>
        db.Classes.CountAsync(g => g.RoomId == roomId && !g.IsArchived, ct);
}

/// <summary>
/// DAVOMAT SABABLARI (<c>SettingsController.SaveAbsenceReasons</c>) — ro'yxat almashtirilishidan
/// oldingi darvoza. Sabab <c>JournalEntry.ReasonId</c> orqali o'tgan davomatga bog'langan.
/// </summary>
public static class AbsenceReasonRules
{
    /// <summary>«Kech keldi» sababi bir vaqtda «sababli» bo'la olmaydi (SPEC P3.1).</summary>
    public const string LateExcusedMessage = "\"Kech keldi\" sababi bir vaqtda \"sababli\" bo'la olmaydi";

    /// <summary>
    /// Bitta sabab bayroqlari mosligi: <c>IsLate &amp;&amp; IsExcused</c> — xato (kech kelgan o'quvchi
    /// darsda bo'lgan, "uzrli yo'qlik" emas). <c>IsExcused</c> ishlatilgan sababda ham O'ZGARTIRILADI —
    /// u faqat analitikaga (ketma-ket kelmaslik seriyasi) ta'sir qiladi, foizga emas.
    /// </summary>
    public static string? ExcusedError(bool isLate, bool isExcused) =>
        isLate && isExcused ? LateExcusedMessage : null;

    /// <summary>
    /// Yangi bazalar uchun standart «sababli» bayrog'i va migratsiyadagi backfill qoidasi
    /// (<c>docs/migrations/Phase3.sql</c>): kech EMAS va nomida «sababsiz» yo'q → sababli.
    /// </summary>
    public static bool DefaultExcused(string name, bool isLate) =>
        !isLate && !(name ?? "").ToLowerInvariant().Contains("sababsiz");

    /// <summary>
    /// Yangi ro'yxatni tekshiradi: jurnalda ISHLATILGAN sababni olib tashlash ham, uning
    /// «kech keldi» (<c>IsLate</c>) belgisini almashtirish ham rad etiladi (sababi matnda, sanoq bilan).
    /// Xato yo'q bo'lsa <c>null</c>.
    ///
    /// <para>Nega rad etiladi (yumshoq o'chirish emas): olib tashlangan sababli yozuvlar "noma'lum sabab"
    /// bo'lib qolar, <c>IsLate</c> almashsa esa O'TGAN kechikishlar yo'qlikka (yoki teskarisi) aylanib,
    /// davomat foizi, reyting va hisobotlar orqaga qarab o'zgarardi. Nomini o'zgartirish — erkin.</para>
    /// </summary>
    public static async Task<string?> ValidateReplaceAsync(
        IAppDbContext db, IReadOnlyList<AbsenceReasonDto> incoming, CancellationToken ct = default)
    {
        var current = await db.AbsenceReasons.AsNoTracking().ToListAsync(ct);
        var incomingById = incoming.Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var removed = current.Where(c => !incomingById.ContainsKey(c.Id)).ToList();
        var flipped = current.Where(c => incomingById.TryGetValue(c.Id, out var n) && n.IsLate != c.IsLate).ToList();
        var watched = removed.Concat(flipped).Select(r => r.Id).ToList();
        if (watched.Count == 0) return null;

        var usage = (await db.JournalEntries.AsNoTracking()
                .Where(e => e.ReasonId != null && watched.Contains(e.ReasonId))
                .GroupBy(e => e.ReasonId!)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Count, StringComparer.Ordinal);

        var errors = new List<string>();
        foreach (var r in removed.Where(r => usage.ContainsKey(r.Id)))
            errors.Add($"«{r.Name}» jurnalda {usage[r.Id]} ta yozuvda ishlatilgan — o'chirib bo'lmaydi (nomini o'zgartirish mumkin)");
        foreach (var r in flipped.Where(r => usage.ContainsKey(r.Id)))
            errors.Add($"«{r.Name}» ning «kech keldi» belgisini o'zgartirib bo'lmaydi — u {usage[r.Id]} ta yozuvda ishlatilgan (o'tgan davomat qayta hisoblanib ketardi)");
        return errors.Count == 0 ? null : string.Join("; ", errors) + ".";
    }
}
