using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// QABUL CHEKLISTI — retseptsiya/administrator yangi o'quvchini qabul qilganda bajaradigan
/// 5 ta ish: ma'lumotlarni to'ldirish, daraja testi, shartnoma, Telegramga ulash, to'lovni
/// qabul qilish. YAGONA manba: profil kartasi ham, "Qabul cheklisti" ro'yxati ham, "Yangi
/// o'quvchilar" dagi ustun ham AYNAN shu yerdan oladi.
///
/// <para><b>AVTOMATIK + QO'LDA.</b> Har band avval MAVJUD yozuvlardan aniqlanadi (pastdagi
/// jadval). Aniqlab bo'lmaydigan ish (qog'ozda olingan test, qo'lda imzolangan shartnoma)
/// uchun xodim QO'LDA "bajarildi" qo'yadi — bazada FAQAT shu belgilar saqlanadi
/// (<see cref="StudentIntakeMark"/>). Band bajarilgan = avtomatik YOKI qo'lda.</para>
///
/// <list type="table">
/// <item><term><c>profile</c></term><description>telefon (o'quvchi/ota-ona/ota/ona) · tug'ilgan sana ·
///   maktab sinfi · kamida bitta TIRIK a'zolik (arxivlanmagan guruhda)</description></item>
/// <item><term><c>levelTest</c></term><description>o'quvchiga aylantirilgan LID orqali daraja testi
///   topshiruvi (<see cref="LevelTestSubmission"/>) yoki lid kartasidagi daraja (<see cref="Lead.Level"/>)</description></item>
/// <item><term><c>contract</c></term><description>ota-onaga tuzilgan shartnoma
///   (<c>Target="parent"</c>, <c>RecipientKey = StudentId</c> — <c>StudentListView</c> bilan bir xil)</description></item>
/// <item><term><c>telegram</c></term><description><see cref="TelegramRegistration"/> shu o'quvchiga bog'langan</description></item>
/// <item><term><c>payment</c></term><description>kamida bitta O'QUV to'lovi (kirim/tuition —
///   <c>DashboardSummary</c> konvensiyasi; bekor qilinganlar global filtr bilan chiqib ketadi)</description></item>
/// </list>
///
/// <para>⚠️ Guruh testlari (<see cref="TestScore"/>) daraja testi deb SANALMAYDI: ular oylik/bo'lim
/// imtihonlari — sinovdagi o'quvchi guruh imtihonini yozsa band yolg'on "bajarildi" bo'lardi.</para>
///
/// <para>⚠️ Javobda fayl MANZILI yo'q (shartnoma PDF/DOCX) — faqat raqam va sana
/// (<c>uploads-security.md</c>). Qoidalar: <c>.claude/rules/student-intake.md</c>.</para>
/// </summary>
public static class StudentIntake
{
    public const string Profile = "profile";
    public const string LevelTest = "levelTest";
    public const string ContractItem = "contract";
    public const string Telegram = "telegram";
    public const string Payment = "payment";

    public sealed record ItemDef(string Key, string Label);

    /// <summary>Bandlar katalogi — TARTIB ham shu (qabul jarayonining tabiiy ketma-ketligi).</summary>
    public static readonly IReadOnlyList<ItemDef> Items =
    [
        new(Profile, "Ma'lumotlar to'ldirilgan"),
        new(LevelTest, "Daraja testi olingan"),
        new(ContractItem, "Shartnoma tuzilgan"),
        new(Telegram, "Telegramga ulangan"),
        new(Payment, "To'lov qabul qilingan"),
    ];

    public static bool IsKnownItem(string? key) => key is not null && Items.Any(i => i.Key == key);

    public static string LabelOf(string key) => Items.FirstOrDefault(i => i.Key == key)?.Label ?? key;

    // ---- "Ma'lumotlar to'ldirilgan" — majburiy maydonlar ----
    public const string FieldPhone = "phone";
    public const string FieldBirthDate = "birthDate";
    public const string FieldSchoolGrade = "schoolGrade";
    public const string FieldGroup = "group";

    /// <summary>Majburiy profil maydonlari (tartib — formadagidek).</summary>
    public static readonly IReadOnlyList<StudentIntakeFieldDto> ProfileFields =
    [
        new(FieldPhone, "Telefon raqami"),
        new(FieldBirthDate, "Tug'ilgan sana"),
        new(FieldSchoolGrade, "Maktab sinfi"),
        new(FieldGroup, "Guruh"),
    ];

    /// <summary>Telefon "bor" hisoblanishi uchun kamida shuncha raqam (mahalliy qism — <see cref="PhoneUtil.Key"/>).</summary>
    public const int MinPhoneDigits = 9;

    // ---- Ro'yxat qamrovi ----
    public const int DefaultDays = 60;
    public const int MinDays = 7;
    public const int MaxDays = 365;
    /// <summary>Bir so'rovda ko'pi bilan shuncha o'quvchi (eng yangilari) — qirqilgani javobda ochiq aytiladi.</summary>
    public const int MaxRows = 1000;
    /// <summary>Qo'lda belgi izohining chegarasi.</summary>
    public const int MaxNoteLength = 500;

    public static int ClampDays(int? days) => Math.Clamp(days ?? DefaultDays, MinDays, MaxDays);

    // =========================================================================================
    //  SOF QOIDALAR
    // =========================================================================================

    /// <summary>Kamida bitta telefon (o'quvchi, asosiy ota-ona, ota, ona) to'liq kiritilganmi.</summary>
    public static bool HasPhone(Student s) =>
        new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone }
            .Any(p => PhoneUtil.Key(p).Length >= MinPhoneDigits);

    /// <summary>Tug'ilgan sana haqiqiy sanami (bo'sh yoki buzuq — yo'q).</summary>
    public static bool HasBirthDate(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Profilda YETISHMAYOTGAN majburiy maydonlar (bo'sh ro'yxat — hammasi bor).</summary>
    public static List<StudentIntakeFieldDto> MissingProfileFields(Student s, bool hasGroup, DateOnly today)
    {
        var missing = new List<StudentIntakeFieldDto>();
        if (!HasPhone(s)) missing.Add(ProfileFields[0]);
        if (!HasBirthDate(s.BirthDate)) missing.Add(ProfileFields[1]);
        if (SchoolGrades.Current(s.SchoolGrade, s.SchoolGradeYear, today) is null) missing.Add(ProfileFields[2]);
        if (!hasGroup) missing.Add(ProfileFields[3]);
        return missing;
    }

    /// <summary>
    /// "Qabul sanasi" — <see cref="Student.EnrollmentDate"/>, bo'sh bo'lsa tizimga kiritilgan kun
    /// (<see cref="Student.CreatedAt"/>). ⚠️ AYNAN shu tartibda: ommaviy import (edutizim) hamma
    /// o'quvchini BUGUNGI <c>CreatedAt</c> bilan yaratadi — u birinchi o'rinda tursa, import
    /// qilingan butun markaz "yangi o'quvchi" bo'lib ko'rinardi.
    /// </summary>
    public static string SinceOf(string? enrollmentDate, string? createdAt)
    {
        var e = (enrollmentDate ?? "").Trim();
        if (e.Length >= 10) return e[..10];
        var c = (createdAt ?? "").Trim();
        return c.Length >= 10 ? c[..10] : "";
    }

    /// <summary>
    /// Ro'yxat QAMROVI: arxivlanmagan VA (sinovdagi a'zoligi bor YOKI qabul sanasi oyna ichida).
    /// Sinovdagilar oynadan qat'i nazar kiradi — "Yangi o'quvchilar" ro'yxati (<c>StudentListView</c>
    /// "trial") bilan bir xil to'plam ustunga tushsin.
    /// </summary>
    public static bool InCohort(bool isArchived, bool hasTrial, string since, string from) =>
        !isArchived && (hasTrial || (since.Length == 10 && string.CompareOrdinal(since, from) >= 0));

    /// <summary>"yyyy-MM-dd…" → "dd.MM.yyyy" (buzuq qiymat o'zgarishsiz qaytadi).</summary>
    public static string Fmt(string? iso)
    {
        var v = (iso ?? "").Trim();
        if (v.Length < 10) return v;
        var d = v[..10];
        return DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x)
            ? x.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : v;
    }

    // ---- Aniqlangan dalillar (bitta o'quvchi uchun) ----

    /// <summary>Daraja: <paramref name="FromSubmission"/>=true — test topshiruvi, false — lid kartasidagi daraja.</summary>
    public sealed record LevelFact(string Level, int? Percent, string Date, bool FromSubmission);
    public sealed record ContractFact(int Number, string Date);
    public sealed record PaymentFact(string FirstDate, int Count);

    public sealed record Facts(
        bool HasGroup,
        LevelFact? Level,
        ContractFact? Contract,
        int TelegramChats,
        PaymentFact? Payment,
        IReadOnlyDictionary<string, StudentIntakeMark> Marks);

    private static StudentIntakeItemDto Item(
        string key, bool auto, string detail, IReadOnlyDictionary<string, StudentIntakeMark> marks,
        List<StudentIntakeFieldDto>? missing = null)
    {
        marks.TryGetValue(key, out var m);
        return new StudentIntakeItemDto(
            key, LabelOf(key), auto || m is not null, auto, detail, missing ?? [],
            m is null ? null : new StudentIntakeMarkDto(m.Note, m.MarkedByName, m.MarkedAt));
    }

    /// <summary>5 ta bandni dalillardan hisoblaydi (sof — bazaga tegmaydi).</summary>
    public static List<StudentIntakeItemDto> EvaluateItems(Student s, Facts f, DateOnly today)
    {
        var missing = MissingProfileFields(s, f.HasGroup, today);
        var profileDetail = missing.Count == 0
            ? "Hammasi to'ldirilgan"
            : "Yetishmaydi: " + string.Join(", ", missing.Select(m => m.Label));

        string levelDetail;
        if (f.Level is null) levelDetail = "Test natijasi topilmadi";
        else if (f.Level.FromSubmission)
        {
            var parts = new List<string> { f.Level.Level.Length > 0 ? $"Daraja: {f.Level.Level}" : "Test topshirilgan" };
            if (f.Level.Percent is int p) parts.Add($"{p}%");
            if (f.Level.Date.Length > 0) parts.Add(Fmt(f.Level.Date));
            levelDetail = string.Join(" · ", parts);
        }
        else levelDetail = $"Daraja: {f.Level.Level} (lid kartasida)";

        var contractDetail = f.Contract is null
            ? "Shartnoma tuzilmagan"
            : $"№ {f.Contract.Number}" + (f.Contract.Date.Length > 0 ? $" · {Fmt(f.Contract.Date)}" : "");

        var telegramDetail = f.TelegramChats switch
        {
            <= 0 => "Botga ulanmagan",
            1 => "Bot orqali ulangan",
            var n => $"Bot orqali ulangan ({n} ta chat)",
        };

        var paymentDetail = f.Payment is null
            ? "To'lov qabul qilinmagan"
            : $"Birinchi to'lov: {Fmt(f.Payment.FirstDate)}" + (f.Payment.Count > 1 ? $" · jami {f.Payment.Count} ta" : "");

        return
        [
            Item(Profile, missing.Count == 0, profileDetail, f.Marks, missing),
            Item(LevelTest, f.Level is not null, levelDetail, f.Marks),
            Item(ContractItem, f.Contract is not null, contractDetail, f.Marks),
            Item(Telegram, f.TelegramChats > 0, telegramDetail, f.Marks),
            Item(Payment, f.Payment is not null, paymentDetail, f.Marks),
        ];
    }

    /// <summary>To'liq cheklist DTO (sof).</summary>
    public static StudentIntakeDto Evaluate(
        Student s, Facts f, DateOnly today, List<string> groups, string memberState, string? leadId)
    {
        var items = EvaluateItems(s, f, today);
        var done = items.Count(i => i.Done);
        var phone = new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone }
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
        return new StudentIntakeDto(
            s.Id, s.FullName, phone, groups, memberState, SinceOf(s.EnrollmentDate, s.CreatedAt),
            done, items.Count, done == items.Count, items, leadId);
    }

    // =========================================================================================
    //  BAZADAN YIG'ISH — ommaviy so'rovlar (N+1 YO'Q: o'quvchilar soni qancha bo'lsa ham 8 ta so'rov)
    // =========================================================================================

    /// <summary>
    /// Berilgan o'quvchilar uchun cheklistlar (kirish tartibida). Har jadvalga BITTA so'rov.
    /// </summary>
    public static async Task<List<StudentIntakeDto>> BuildAsync(
        IAppDbContext db, IReadOnlyCollection<Student> students, DateOnly today, CancellationToken ct = default)
    {
        if (students.Count == 0) return [];
        var ids = students.Select(s => s.Id).Distinct().ToList();

        // A'zoliklar — TIRIK (IsActive); guruhi o'chirilgani (yetim) hisobga olinmaydi.
        var memberships = await db.StudentGroups.AsNoTracking()
            .Where(sg => sg.IsActive && ids.Contains(sg.StudentId)).ToListAsync(ct);
        var groupIds = memberships.Select(m => m.GroupId).Distinct().ToList();
        var groups = groupIds.Count == 0
            ? new Dictionary<string, (string Name, bool IsArchived)>()
            : (await db.Classes.AsNoTracking().Where(c => groupIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name, c.IsArchived }).ToListAsync(ct))
                .ToDictionary(c => c.Id, c => (c.Name, c.IsArchived));
        var groupNames = groups.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        var memberOf = memberships.Where(m => groups.ContainsKey(m.GroupId))
            .GroupBy(m => m.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        // Lid → o'quvchi; daraja testi faqat SHU bog'lanish orqali (telefon bo'yicha moslash
        // ATAYIN yo'q: aka-uka bir xil ota-ona raqamida — begona natija "bajarildi" bo'lardi).
        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.ConvertedStudentId != null && ids.Contains(l.ConvertedStudentId))
            .Select(l => new { l.Id, StudentId = l.ConvertedStudentId!, l.Level, l.CreatedAt })
            .ToListAsync(ct);
        var leadIds = leads.Select(l => l.Id).ToList();
        var submissions = leadIds.Count == 0
            ? []
            : await db.LevelTestSubmissions.AsNoTracking()
                .Where(x => leadIds.Contains(x.LeadId))
                .Select(x => new { x.LeadId, x.Level, x.Percent, x.CreatedAt })
                .ToListAsync(ct);
        var studentOfLead = leads.ToDictionary(l => l.Id, l => l.StudentId);
        var lastSubmission = submissions
            .Where(x => studentOfLead.ContainsKey(x.LeadId))
            .GroupBy(x => studentOfLead[x.LeadId])
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt ?? "", StringComparer.Ordinal).First());
        var leadsOf = leads.GroupBy(l => l.StudentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt ?? "", StringComparer.Ordinal).ToList());

        // Shartnoma — FAQAT raqam va sana (fayl manzillari SELECT'ga ham tushmaydi).
        var contracts = (await db.Contracts.AsNoTracking()
                .Where(c => c.Target == "parent" && ids.Contains(c.RecipientKey))
                .Select(c => new { c.RecipientKey, c.Number, c.SentAt })
                .ToListAsync(ct))
            .GroupBy(c => c.RecipientKey)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Number).First());

        var telegram = (await db.TelegramRegistrations.AsNoTracking()
                .Where(r => ids.Contains(r.StudentId))
                .Select(r => new { r.StudentId, r.ChatId })
                .ToListAsync(ct))
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChatId).Distinct().Count());

        var payments = (await db.FinanceTransactions.AsNoTracking()
                .Where(t => t.StudentId != null && ids.Contains(t.StudentId)
                            && t.Direction == "income" && t.Category == "tuition")
                .Select(t => new { StudentId = t.StudentId!, t.Date })
                .ToListAsync(ct))
            .GroupBy(t => t.StudentId)
            .ToDictionary(g => g.Key, g => new PaymentFact(
                g.Select(x => x.Date ?? "").Where(d => d.Length > 0).DefaultIfEmpty("")
                    .Min(StringComparer.Ordinal) ?? "",
                g.Count()));

        var marks = (await db.StudentIntakeMarks.AsNoTracking()
                .Where(m => ids.Contains(m.StudentId)).ToListAsync(ct))
            .GroupBy(m => m.StudentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, StudentIntakeMark>)
                g.GroupBy(m => m.Item).ToDictionary(x => x.Key, x => x.First()));
        var noMarks = new Dictionary<string, StudentIntakeMark>();

        var result = new List<StudentIntakeDto>(students.Count);
        foreach (var s in students)
        {
            var mine = memberOf.GetValueOrDefault(s.Id) ?? [];
            var hasGroup = mine.Any(m => !groups[m.GroupId].IsArchived);

            LevelFact? level = null;
            if (lastSubmission.TryGetValue(s.Id, out var sub))
                level = new LevelFact(sub.Level ?? "", sub.Percent, sub.CreatedAt ?? "", true);
            else if (leadsOf.TryGetValue(s.Id, out var ls) && ls.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Level)) is { } withLevel)
                level = new LevelFact(withLevel.Level.Trim(), null, "", false);

            ContractFact? contract = contracts.TryGetValue(s.Id, out var c)
                ? new ContractFact(c.Number, c.SentAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : null;

            var facts = new Facts(
                hasGroup, level, contract, telegram.GetValueOrDefault(s.Id, 0),
                payments.GetValueOrDefault(s.Id), marks.GetValueOrDefault(s.Id) ?? noMarks);

            var display = StudentMembershipView.DisplayGroupNames(mine, groupNames);
            result.Add(Evaluate(
                s, facts, today, display, MembershipLifecycle.MemberState(mine),
                leadsOf.TryGetValue(s.Id, out var own) ? own[0].Id : null));
        }
        return result;
    }

    /// <summary>Bitta o'quvchining cheklisti (topilmasa <c>null</c>).</summary>
    public static async Task<StudentIntakeDto?> ForStudentAsync(
        IAppDbContext db, string studentId, DateOnly today, CancellationToken ct = default)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (s is null) return null;
        return (await BuildAsync(db, [s], today, ct))[0];
    }

    /// <summary>
    /// "Qabul cheklisti" ro'yxati: QAMROV (<see cref="InCohort"/>) → cheklistlar → saralash
    /// (to'liq bo'lmaganlar tepada, keyin eng yangi qabul qilingan).
    /// </summary>
    public static async Task<StudentIntakeListDto> ListAsync(
        IAppDbContext db, int? days, DateOnly today, CancellationToken ct = default)
    {
        var d = ClampDays(days);
        var from = today.AddDays(-d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Sinovdagilar — "Yangi o'quvchilar" ta'rifi (tirik sinov a'zoligi, guruhi mavjud).
        var trialIds = (await db.StudentGroups.AsNoTracking()
                .Where(sg => sg.IsActive && sg.Status == "trial" && db.Classes.Any(c => c.Id == sg.GroupId))
                .Select(sg => sg.StudentId).Distinct().ToListAsync(ct))
            .ToHashSet();

        // Yengil proyeksiya — qamrov xotirada aniqlanadi (sana satrlari turli formatda bo'lishi mumkin).
        var light = await db.Students.AsNoTracking()
            .Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.EnrollmentDate, s.CreatedAt, s.IsArchived })
            .ToListAsync(ct);
        var cohort = light
            .Select(s => new { s.Id, Since = SinceOf(s.EnrollmentDate, s.CreatedAt), s.IsArchived })
            .Where(s => InCohort(s.IsArchived, trialIds.Contains(s.Id), s.Since, from))
            .OrderByDescending(s => s.Since, StringComparer.Ordinal)
            .ToList();

        var total = cohort.Count;
        var ids = cohort.Take(MaxRows).Select(s => s.Id).ToList();
        var students = ids.Count == 0
            ? []
            : await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);

        var rows = await BuildAsync(db, students, today, ct);
        rows = rows
            .OrderBy(r => r.Complete)
            .ThenByDescending(r => r.Since, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pending = Items.ToDictionary(
            i => i.Key, i => rows.Count(r => r.Items.Any(x => x.Key == i.Key && !x.Done)));

        return new StudentIntakeListDto(
            rows, d, from, total, rows.Count(r => !r.Complete), pending, total > ids.Count);
    }

    // =========================================================================================
    //  QO'LDA BELGI
    // =========================================================================================

    /// <summary>Belgi o'zgarishi — audit uchun (<c>null</c> — hech narsa o'zgarmadi).</summary>
    public sealed record MarkChange(string Action, string Summary);

    /// <summary>
    /// Qo'lda belgini qo'yadi/yangilaydi/olib tashlaydi. <b>SaveChanges QILMAYDI</b> — chaqiruvchi
    /// audit yozuvini qo'shib, BITTA saqlash bilan yakunlaydi. Idempotent: yo'q belgini olib
    /// tashlash yoki bir xil izohni qayta yozish — <c>null</c> (tarixga ortiqcha qator tushmaydi).
    /// </summary>
    /// <exception cref="ArgumentException">Noma'lum band kaliti.</exception>
    public static async Task<MarkChange?> ApplyMarkAsync(
        IAppDbContext db, string studentId, string item, bool done, string? note,
        string actorId, string actorName, CancellationToken ct = default)
    {
        if (!IsKnownItem(item)) throw new ArgumentException($"Noma'lum band: {item}", nameof(item));
        var label = LabelOf(item);
        var text = (note ?? "").Trim();
        if (text.Length > MaxNoteLength) text = text[..MaxNoteLength];

        var existing = await db.StudentIntakeMarks
            .FirstOrDefaultAsync(m => m.StudentId == studentId && m.Item == item, ct);

        if (!done)
        {
            if (existing is null) return null;
            db.StudentIntakeMarks.Remove(existing);
            return new MarkChange("delete", $"Qabul cheklisti: «{label}» qo'lda belgisi olib tashlandi");
        }

        var suffix = text.Length > 0 ? $" (izoh: {text})" : "";
        if (existing is not null)
        {
            if (existing.Note == text) return null;
            existing.Note = text;
            return new MarkChange("update", $"Qabul cheklisti: «{label}» belgisining izohi o'zgartirildi{suffix}");
        }

        db.StudentIntakeMarks.Add(new StudentIntakeMark
        {
            StudentId = studentId,
            Item = item,
            Note = text,
            MarkedById = actorId,
            MarkedByName = actorName,
            MarkedAt = AppClock.Iso(),
        });
        return new MarkChange("create", $"Qabul cheklisti: «{label}» qo'lda bajarildi deb belgilandi{suffix}");
    }
}
