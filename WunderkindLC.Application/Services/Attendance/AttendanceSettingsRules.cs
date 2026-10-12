using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>Attendance discipline + absent-notice settings (SPEC P3.2). <c>Warnings</c> is response-only.</summary>
public record AttendanceSettingsDto(
    bool DisciplineEnabled,
    int DeadlineMinutes,
    List<int> ReminderMinutes,
    bool EscalateEnabled,
    bool FineEnabled,
    decimal FineAmount,
    int AbsentNoticeDelayMinutes,
    string AbsentNoticeApproval,
    List<string>? Warnings = null);

/// <summary>
/// Pure validation / parsing of the attendance settings (<see cref="CenterMeta"/> columns, SPEC P3.2).
/// The controller only orchestrates; every bound lives here (tested by <c>AttendanceSettingsTests</c>).
/// </summary>
public static class AttendanceSettingsRules
{
    public const int MinDeadline = 5;
    public const int MaxDeadline = 120;
    public const int MaxReminders = 3;
    public const decimal MaxFineAmount = 10_000_000m;
    public const int MaxNoticeDelay = 180;

    public const string ApprovalAuto = "auto";
    public const string ApprovalManual = "manual";
    public const string ApprovalLateOnly = "late_only";
    public static readonly IReadOnlyList<string> Approvals = [ApprovalAuto, ApprovalManual, ApprovalLateOnly];

    /// <summary>Double-reminder warning (legacy «Davomat eslatmasi» auto-message rule still enabled).</summary>
    public const string DoubleReminderWarning =
        "Avto xabarlarda «Davomat eslatmasi» qoidasi ham yoqilgan — o'qituvchi ikki marta eslatma olishi mumkin";

    /// <summary>"5,10" → [5, 10]. Junk and non-positive items are dropped (stored value is validated on save).</summary>
    public static List<int> ParseReminders(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var v) ? v : 0)
            .Where(v => v > 0)
            .ToList();

    public static string FormatReminders(IEnumerable<int> minutes) => string.Join(",", minutes);

    /// <summary>First error (Uzbek, shown as is) or <c>null</c>.</summary>
    public static string? Validate(AttendanceSettingsDto s)
    {
        if (s.DeadlineMinutes is < MinDeadline or > MaxDeadline)
            return $"Davomat muddati {MinDeadline}..{MaxDeadline} daqiqa bo'lishi kerak";
        var rem = s.ReminderMinutes ?? [];
        if (rem.Count > MaxReminders)
            return $"Eslatmalar ko'pi bilan {MaxReminders} ta bo'lishi mumkin";
        for (var i = 0; i < rem.Count; i++)
        {
            if (rem[i] < 1 || rem[i] > s.DeadlineMinutes)
                return $"Eslatma vaqti 1..{s.DeadlineMinutes} daqiqa oralig'ida bo'lishi kerak (davomat muddatidan oshmasin)";
            if (i > 0 && rem[i] <= rem[i - 1])
                return "Eslatma vaqtlari o'sib boruvchi tartibda bo'lishi kerak (masalan 5, 10)";
        }
        if (s.FineAmount < 0 || s.FineAmount > MaxFineAmount)
            return "Jarima summasi 0 dan 10 000 000 so'mgacha bo'lishi kerak";
        if (s.FineAmount != decimal.Truncate(s.FineAmount))
            return "Jarima summasi butun son bo'lishi kerak";
        if (s.FineEnabled && s.FineAmount <= 0)
            return "Jarima yoqilgan — bir dars uchun jarima summasini kiriting";
        if (s.AbsentNoticeDelayMinutes is < 0 or > MaxNoticeDelay)
            return $"«Kelmadi» xabari kechikishi 0..{MaxNoticeDelay} daqiqa bo'lishi kerak";
        if (!Approvals.Contains(s.AbsentNoticeApproval ?? ""))
            return "Tasdiqlash rejimi noto'g'ri";
        return null;
    }

    /// <summary>Current settings of the centre (null meta → defaults).</summary>
    public static AttendanceSettingsDto FromMeta(CenterMeta? m)
    {
        m ??= new CenterMeta();
        return new AttendanceSettingsDto(
            m.AttendanceDisciplineEnabled,
            m.AttendanceDeadlineMinutes,
            ParseReminders(m.AttendanceReminderMinutes),
            m.AttendanceEscalateEnabled,
            m.AttendanceFineEnabled,
            m.AttendanceFineAmount,
            m.AbsentNoticeDelayMinutes,
            Approvals.Contains(m.AbsentNoticeApproval) ? m.AbsentNoticeApproval : ApprovalLateOnly);
    }

    /// <summary>
    /// Writes a VALIDATED dto into the meta row. <paramref name="now"/> stamps the «since» moments: a switch flipped
    /// false → true gets <c>now</c>, a switch turned off is cleared, an unchanged switch keeps its moment — fines,
    /// reminders and escalations are never retroactive (<see cref="AttendanceDiscipline.Decide"/>).
    /// </summary>
    public static void Apply(CenterMeta m, AttendanceSettingsDto s, DateTime now)
    {
        m.AttendanceDisciplineSince = Since(m.AttendanceDisciplineEnabled, s.DisciplineEnabled, m.AttendanceDisciplineSince, now);
        m.AttendanceFineSince = Since(m.AttendanceFineEnabled, s.FineEnabled, m.AttendanceFineSince, now);
        m.AttendanceDisciplineEnabled = s.DisciplineEnabled;
        m.AttendanceDeadlineMinutes = s.DeadlineMinutes;
        m.AttendanceReminderMinutes = FormatReminders(s.ReminderMinutes ?? []);
        m.AttendanceEscalateEnabled = s.EscalateEnabled;
        m.AttendanceFineEnabled = s.FineEnabled;
        m.AttendanceFineAmount = s.FineAmount;
        m.AbsentNoticeDelayMinutes = s.AbsentNoticeDelayMinutes;
        m.AbsentNoticeApproval = s.AbsentNoticeApproval;
    }

    /// <summary>
    /// The «since» moment after a save: off → null; just turned on (or on without a moment — legacy row) → now;
    /// still on → unchanged.
    /// </summary>
    public static DateTime? Since(bool wasOn, bool isOn, DateTime? current, DateTime now) =>
        !isOn ? null
        : !wasOn || current is null ? now
        : current;

    /// <summary>Uzbek labels for the settings audit row (<c>SettingsAudit.Record</c>, withValues).</summary>
    public static readonly IReadOnlyDictionary<string, string> AuditLabels = new Dictionary<string, string>
    {
        [nameof(CenterMeta.AttendanceDisciplineEnabled)] = "Davomat intizomi",
        [nameof(CenterMeta.AttendanceDeadlineMinutes)] = "Davomat muddati (daqiqa)",
        [nameof(CenterMeta.AttendanceReminderMinutes)] = "O'qituvchiga eslatma (daqiqa)",
        [nameof(CenterMeta.AttendanceEscalateEnabled)] = "Administratorga xabar",
        [nameof(CenterMeta.AttendanceFineEnabled)] = "Jarima yozilsin",
        [nameof(CenterMeta.AttendanceFineAmount)] = "Bir dars uchun jarima",
        [nameof(CenterMeta.AbsentNoticeDelayMinutes)] = "«Kelmadi» xabari kechikishi (daqiqa)",
        [nameof(CenterMeta.AbsentNoticeApproval)] = "«Kelmadi» xabarini tasdiqlash",
        [nameof(CenterMeta.AttendanceDisciplineSince)] = "Davomat intizomi yoqilgan vaqt",
        [nameof(CenterMeta.AttendanceFineSince)] = "Jarima yoqilgan vaqt",
    };
}
