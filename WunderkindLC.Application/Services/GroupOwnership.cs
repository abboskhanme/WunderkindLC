using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// GURUH EGALIGI — "shu (guruh, sana/oy) kimniki?" savoliga TARIX bo'yicha javob beradigan yuklangan
/// kontekst (guruhlar + <see cref="GroupTeacherAssignment"/> tarixi + dars ko'chirishlari).
///
/// <para><b>Muammo (audit C3):</b> maosh va o'qituvchi hisobotlari guruhni HOZIRGI
/// <see cref="Group.TeacherId"/> bo'yicha tanlardi — o'qituvchi almashsa guruhning BUTUN o'tmishi
/// (to'langan oylar ham) yangi o'qituvchiga ko'chib ketardi: eskisi "ortiqcha olgan", yangisi
/// "olmagan" bo'lib ko'rinardi va ikki marta to'lashga undardi.</para>
///
/// <para><b>Qoida:</b> sana egasi — <see cref="GroupTeacherHistory.TeacherOnDate"/>; oylik ulush —
/// <see cref="GroupTeacherHistory.SharesInMonth"/> (rejadagi dars sanalari nisbatida). Dars sanalari
/// YAGONA manbadan — <see cref="SubstituteTeacherService.LessonDatesInMonth"/> (ko'chirishlar + guruh
/// umri), ya'ni o'rinbosarlik narxining maxraji bilan AYNAN bir xil to'plam.</para>
///
/// <para>Ishlatadiganlar: <see cref="SalaryLedger"/>, <see cref="CourseFinanceReport"/> (o'qituvchi
/// kesimi), <see cref="TeacherActivityReport"/>, <see cref="TeacherSnapshotBuilder"/>,
/// <see cref="SubstituteTeacherService.PerLessonBatchAsync"/>.</para>
/// </summary>
public sealed class GroupOwnership
{
    private readonly Dictionary<string, Group> _groups;
    private readonly Dictionary<string, List<GroupTeacherAssignment>> _history;
    private readonly Dictionary<string, IReadOnlyList<JournalService.LessonMove>> _moves;
    private readonly Dictionary<(string GroupId, string Month), List<string>> _dates = new();
    private readonly Dictionary<(string GroupId, string Month), Dictionary<string, decimal>> _shares = new();

    public GroupOwnership(
        IEnumerable<Group> groups,
        IReadOnlyDictionary<string, List<GroupTeacherAssignment>>? history = null,
        IReadOnlyDictionary<string, IReadOnlyList<JournalService.LessonMove>>? moves = null)
    {
        _groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var g in groups) _groups[g.Id] = g;
        _history = history is null
            ? new Dictionary<string, List<GroupTeacherAssignment>>(StringComparer.Ordinal)
            : new Dictionary<string, List<GroupTeacherAssignment>>(history, StringComparer.Ordinal);
        _moves = moves is null
            ? new Dictionary<string, IReadOnlyList<JournalService.LessonMove>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<JournalService.LessonMove>>(moves, StringComparer.Ordinal);
    }

    /// <summary>Guruhlar uchun tarix va ko'chirishlarni bitta-bittadan so'rov bilan yuklaydi.</summary>
    public static async Task<GroupOwnership> LoadAsync(
        IAppDbContext db, IReadOnlyCollection<Group> groups, CancellationToken ct = default)
    {
        var ids = groups.Select(g => g.Id).Distinct().ToList();
        var history = await GroupTeacherHistory.LoadAsync(db, ids, ct);
        var moves = await SubstituteTeacherService.MovesByGroupAsync(db, ids, ct);
        return new GroupOwnership(groups, history, moves);
    }

    /// <summary>
    /// O'qituvchi HOZIR yoki ILGARI (tarixda) o'qitgan guruhlar id'lari. Maosh/hisobot shu ro'yxatdan
    /// boshlanadi — faqat hozirgi guruhlar olinsa, o'qituvchi almashgan guruhning o'tgan oylari eski
    /// o'qituvchining varaqasidan yo'qolardi.
    /// </summary>
    public static async Task<List<string>> GroupIdsOfTeacherAsync(
        IAppDbContext db, string teacherId, CancellationToken ct = default)
    {
        var current = await db.Classes.AsNoTracking()
            .Where(c => c.TeacherId == teacherId).Select(c => c.Id).ToListAsync(ct);
        var past = await db.GroupTeacherAssignments.AsNoTracking()
            .Where(a => a.TeacherId == teacherId).Select(a => a.GroupId).ToListAsync(ct);
        return current.Concat(past).Distinct().ToList();
    }

    public Group? Group(string groupId) => _groups.GetValueOrDefault(groupId);

    public IReadOnlyList<JournalService.LessonMove>? Moves(string groupId) => _moves.GetValueOrDefault(groupId);

    /// <summary>Tarixdagi ENG ERTA biriktirish sanasi ("yyyy-MM-dd") yoki <c>null</c> — <see cref="Group.StartDate"/>
    /// bo'sh guruhning "qachondan beri bor" zaxirasi (yangi guruhda qator yaratilgan kun).</summary>
    public string? FirstAssignedDate(string groupId) =>
        _history.TryGetValue(groupId, out var h) && h.Count > 0
            ? h.Select(a => a.FromDate).Where(d => d is { Length: >= 10 }).DefaultIfEmpty(null).Min(StringComparer.Ordinal)
            : null;

    /// <summary>Tarixi bormi (yo'q bo'lsa hamma narsa <see cref="Group.TeacherId"/> ga yoziladi).</summary>
    public bool HasHistory(string groupId) => _history.TryGetValue(groupId, out var h) && h.Count > 0;

    /// <summary>Guruhning shu oydagi rejadagi dars sanalari (keshlangan).</summary>
    public IReadOnlyList<string> LessonDates(string groupId, string month)
    {
        var key = (groupId, month.Length >= 7 ? month[..7] : month);
        if (!_dates.TryGetValue(key, out var list))
            _dates[key] = list = SubstituteTeacherService.LessonDatesInMonth(
                _groups.GetValueOrDefault(groupId), key.Item2, _moves.GetValueOrDefault(groupId));
        return list;
    }

    /// <summary>Guruhning shu SANADAGI egasi (tarix → hozirgi o'qituvchi).</summary>
    public string? OwnerOn(string groupId, string? date) =>
        GroupTeacherHistory.TeacherOnDate(
            _history.GetValueOrDefault(groupId), date, _groups.GetValueOrDefault(groupId)?.TeacherId);

    /// <summary>Guruhning shu OYDAGI asosiy egasi (oyni qamragan oxirgi biriktirish → hozirgi o'qituvchi).
    /// Kun aniqligi kerak bo'lmagan joylar uchun (masalan "shu oyda aktivlashdi" hodisasi).</summary>
    public string? OwnerAtMonth(string groupId, string month)
    {
        var shares = Shares(groupId, month);
        if (shares.Count == 0) return null;
        // Eng katta ulush; teng bo'lsa oyni qamragan oxirgi biriktirish (TeacherAtMonth bilan bir xil).
        var max = shares.Values.Max();
        var top = shares.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
        if (top.Count == 1) return top[0];
        var last = GroupTeacherHistory.TeacherAtMonth(_history.GetValueOrDefault(groupId), month);
        return last is not null && top.Contains(last) ? last : top[0];
    }

    /// <summary>(guruh, oy) → o'qituvchilar ulushi (keshlangan).</summary>
    public IReadOnlyDictionary<string, decimal> Shares(string groupId, string month)
    {
        var key = (groupId, month.Length >= 7 ? month[..7] : month);
        if (!_shares.TryGetValue(key, out var s))
            _shares[key] = s = GroupTeacherHistory.SharesInMonth(
                _history.GetValueOrDefault(groupId), LessonDates(groupId, key.Item2).ToList(), key.Item2,
                _groups.GetValueOrDefault(groupId)?.TeacherId);
        return s;
    }

    /// <summary>O'qituvchining (guruh, oy) dagi ulushi (0..1).</summary>
    public decimal Share(string groupId, string month, string teacherId) =>
        Shares(groupId, month).GetValueOrDefault(teacherId, 0m);

    /// <summary>Shu oyning O'QITUVCHIGA TEGISHLI BO'LMAGAN dars sanalari (jurnal jarimasi rejasidan
    /// chiqariladi — boshqa o'qituvchining darsi uchun jarima yozilmasin).</summary>
    public IEnumerable<string> NotOwnedDates(string groupId, string month, string teacherId)
    {
        if (!HasHistory(groupId)) yield break;   // tarixsiz — hammasi hozirgi o'qituvchiniki
        foreach (var d in LessonDates(groupId, month))
            if (OwnerOn(groupId, d) != teacherId) yield return d;
    }
}
