using System.Globalization;
using System.Text;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// SINOV DARSI TOPSHIRIQLARI — sof qoidalar (LC qo'shimchasi, `.claude/rules/hr-tasks.md` §8). Testlar: <c>HrTrialTasksTests</c>.
/// IO — <see cref="HrTasksService"/> (<c>HrTasksService.Trials.cs</c>).
/// </summary>
public static class HrTrialRules
{
    /// <summary>Ro'yxatlar shu soatdan keyin yaratiladi (egasining qarori, 2026-10-27).</summary>
    public static readonly TimeOnly SendAt = new(9, 0);
    /// <summary>Muddat — o'sha kuni shu soatgacha (bog'lanish ish kuni ichida).</summary>
    public static readonly TimeOnly DueAt = new(20, 0);
    /// <summary>Kech yaratilgan ro'yxatga (muddat o'tib ketgan bo'lsa) shuncha vaqt beriladi.</summary>
    public static readonly TimeSpan LateGrace = TimeSpan.FromHours(2);
    public const string GiverName = "Tizim (sinov darsi)";
    /// <summary>Avtomatik bajarilganda topshiriq izohi (isbot o'rnida — panelda ko'rinadi).</summary>
    public const string AutoDoneComment = "Avtomatik: ro'yxatdagi barcha lidlar hal bo'ldi (qayta yozildi, o'quvchiga aylantirildi yoki rad etildi).";

    /// <summary>Lid yopilgan — o'quvchiga aylantirilgan, shartnoma yopilgan yoki yo'qotilgan (rad etilgan).</summary>
    public static bool LeadClosed(string? convertedStudentId, string? closedAt, string? lostAt) =>
        !string.IsNullOrWhiteSpace(convertedStudentId) || !string.IsNullOrWhiteSpace(closedAt) || !string.IsNullOrWhiteSpace(lostAt);

    /// <summary>Sinov KUNI ("yyyy-MM-ddTHH:mm" → sana); buzuq qiymat — null (ro'yxatga tushmaydi).</summary>
    public static DateOnly? DayOf(string? scheduledAt) =>
        scheduledAt is { Length: >= 10 }
        && DateOnly.TryParseExact(scheduledAt[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    /// <summary>Qaysi kunning sinovlari: <c>before</c> — ertangi, <c>after</c> — kechagi.</summary>
    public static DateOnly LessonDay(string kind, DateOnly today) =>
        kind == HrTrialTaskKinds.Before ? today.AddDays(1) : today.AddDays(-1);

    /// <summary>
    /// Ro'yxatga TUSHADIMI (yaratish/qo'shish paytida). before — faqat natijasi hali belgilanmagan sinov;
    /// after — natijasidan qat'i nazar (kelmaganlar bilan ham bog'lanish kerak). Ikkalasida ham lid ochiq.
    /// </summary>
    public static bool Eligible(string kind, string result, bool leadClosed) =>
        !leadClosed && (kind == HrTrialTaskKinds.After || result == "pending");

    /// <summary>
    /// Ro'yxatdagi bitta sinov HAL BO'LDIMI (hammasi hal bo'lsa topshiriq O'ZI bajariladi).
    /// <list type="bullet">
    /// <item>lid o'chirilgan / o'quvchiga aylantirilgan / rad etilgan — hal;</item>
    /// <item><c>before</c>: sinov o'chirilgan, natijasi belgilangan yoki BOSHQA kunga ko'chirilgan — hal;</item>
    /// <item><c>after</c>: lid shu kundan KEYINGI sanaga qayta sinovga yozilgan — hal (yangi sinov o'z sikliga tushadi).</item>
    /// </list>
    /// </summary>
    public static bool Resolved(string kind, DateOnly lessonDay, bool leadMissing, bool leadClosed,
        TrialState? trial, IEnumerable<DateOnly> otherTrialDays)
    {
        if (leadMissing || leadClosed) return true;
        if (kind == HrTrialTaskKinds.Before)
            return trial is not { } t || t.Result != "pending" || t.Day != lessonDay;
        return otherTrialDays.Any(d => d > lessonDay);
    }

    /// <summary>Sinovning holati (resolve uchun).</summary>
    public readonly record struct TrialState(string Result, DateOnly? Day);

    /// <summary>
    /// Kimga: lid MAS'ULI → (bo'lmasa) sinovga YOZGAN xodim → (u ham bo'lmasa) direktor(lar).
    /// Faqat FAOL HR xodimi bo'lgan akkaunt nishon bo'la oladi (aks holda keyingisiga o'tiladi).
    /// </summary>
    public static IReadOnlyList<string> Recipients(string? assigneeUserId, string? bookedByUserId,
        Func<string, string?> employeeOfUser, IReadOnlyList<string> directorEmployeeIds)
    {
        foreach (var userId in new[] { assigneeUserId, bookedByUserId })
            if (!string.IsNullOrWhiteSpace(userId) && employeeOfUser(userId) is { } employeeId)
                return [employeeId];
        return directorEmployeeIds;
    }

    /// <summary>Ro'yxatdagi bitta o'quvchi (ekranda ko'rinadigan hamma ma'lumot).</summary>
    public sealed record Item(
        string TrialId, string FullName, string Phone, string FatherPhone, string MotherPhone,
        string ScheduledAt, string GroupName, string TeacherName, string CourseName, string Level,
        string AssigneeName, string Result);

    public static string Title(string kind, DateOnly lessonDay, int count)
    {
        var day = lessonDay.ToString("dd.MM", CultureInfo.InvariantCulture);
        return kind == HrTrialTaskKinds.Before
            ? $"Ertaga ({day}) sinov darsiga keladiganlar — {count} ta"
            : $"Kecha ({day}) sinov darsi bo'lganlar bilan bog'laning — {count} ta";
    }

    /// <summary>Topshiriq TAVSIFI (oddiy matn — Telegram'da escape qilinadi). <see cref="HrLimits.DescriptionMax"/> ga sig'adi.</summary>
    public static string Description(string kind, IReadOnlyList<Item> items)
    {
        var head = kind == HrTrialTaskKinds.Before
            ? "Ertaga sinov darsiga keladigan o'quvchilar. Ota-onasi bilan bog'lanib, kelishini tasdiqlang."
            : "Kecha sinov darsi bo'lgan o'quvchilar. Bog'laning: qayta sinovga yozing, o'quvchiga aylantiring yoki lidni rad eting — "
              + "hammasi hal bo'lganda topshiriq O'ZI bajariladi.";
        var sb = new StringBuilder(head).Append("\n\n");
        const string moreTail = "\n…va yana {0} ta (lidlar bo'limida)";
        for (var i = 0; i < items.Count; i++)
        {
            var block = ItemText(i + 1, kind, items[i]);
            if (sb.Length + block.Length + 60 > HrLimits.DescriptionMax)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, moreTail, items.Count - i);
                break;
            }
            sb.Append(block);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Bitta o'quvchi bloki: ism, telefonlar, vaqt, guruh/ustoz, kurs/daraja, mas'ul (+ natija — after).</summary>
    public static string ItemText(int number, string kind, Item it)
    {
        var sb = new StringBuilder();
        sb.Append(number).Append(". ").Append(Or(it.FullName, "Ismsiz lid")).Append('\n');
        var phones = new List<string>();
        if (!string.IsNullOrWhiteSpace(it.Phone)) phones.Add(Phone(it.Phone));
        if (!string.IsNullOrWhiteSpace(it.FatherPhone)) phones.Add("ota: " + Phone(it.FatherPhone));
        if (!string.IsNullOrWhiteSpace(it.MotherPhone)) phones.Add("ona: " + Phone(it.MotherPhone));
        sb.Append("   📞 ").Append(phones.Count > 0 ? string.Join(" · ", phones) : "telefon yo'q").Append('\n');
        sb.Append("   🕐 ").Append(When(it.ScheduledAt)).Append(" · ").Append(Or(it.GroupName, "guruh?"));
        if (!string.IsNullOrWhiteSpace(it.TeacherName)) sb.Append(" (ustoz: ").Append(it.TeacherName).Append(')');
        sb.Append('\n');
        sb.Append("   📚 ").Append(Or(it.CourseName, "kurs ko'rsatilmagan")).Append(" · daraja: ").Append(Or(it.Level, "—")).Append('\n');
        if (!string.IsNullOrWhiteSpace(it.AssigneeName)) sb.Append("   👤 Mas'ul: ").Append(it.AssigneeName).Append('\n');
        if (kind == HrTrialTaskKinds.After) sb.Append("   ").Append(ResultLabel(it.Result)).Append('\n');
        return sb.Append('\n').ToString();
    }

    public static string ResultLabel(string result) => result switch
    {
        "came" => "✅ Sinovga keldi",
        "stayed" => "✅ Keldi va qoldi",
        "left" => "⚠️ Keldi, lekin ketdi",
        "no_show" => "❌ Kelmadi",
        _ => "⏳ Natija belgilanmagan",
    };

    /// <summary>"2026-10-28T15:00" → "28.10 15:00".</summary>
    public static string When(string scheduledAt) =>
        DateTime.TryParse(scheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM HH:mm", CultureInfo.InvariantCulture)
            : scheduledAt;

    /// <summary>O'zbek raqami — "+998 90 123 45 67"; boshqasi o'zgarishsiz (soxta "998" qo'shilmaydi).</summary>
    public static string Phone(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 9) digits = "998" + digits;
        return digits.Length == 12 && digits.StartsWith("998", StringComparison.Ordinal)
            ? $"+998 {digits[3..5]} {digits[5..8]} {digits[8..10]} {digits[10..12]}"
            : raw.Trim();
    }

    /// <summary>Muddat: o'sha kuni 20:00; u o'tib ketgan (yoki juda yaqin) bo'lsa — hozirdan +2 soat.</summary>
    public static DateTime DueLocal(DateTime localNow)
    {
        var due = localNow.Date + DueAt.ToTimeSpan();
        return due - localNow < LateGrace ? localNow + LateGrace : due;
    }

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
