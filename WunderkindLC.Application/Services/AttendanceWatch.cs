using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «DAVOMAT OLINMAGAN GURUHLAR» — tanlangan KUNDA qaysi guruhda dars BOR va u hozir qaysi
/// bosqichda (hali boshlanmagan / ketyapti / tugagan). Manba — <c>GET /api/admin/dashboard/today-lessons</c>
/// ("Darslar monitoringi"), u «Nazorat → Davomat qilinmagan guruhlar» sahifasida ham,
/// «O'quvchilar davomati» sahifasidagi bo'limda ham ishlatiladi. Batafsil: <c>.claude/rules/retention-loss.md</c> §6.
///
/// <para>Qoidalar QAYTA YOZILMAGAN — o'qituvchi jadvali bilan BITTA manba
/// (<see cref="TeacherHomeService.PlanLessons"/>):</para>
/// <list type="bullet">
/// <item>dars sanalari — guruh kunlari + KO'CHIRILGAN darslar (<c>LessonReschedules</c>), guruh umri
///   ichida (<c>StartDate</c> dan oldin va <c>EndDate</c>/<c>ArchivedAt</c> dan keyin dars yo'q);</item>
/// <item>ko'chirilgan darsning VAQTI — <see cref="TeacherHomeService.LessonTime"/>;</item>
/// <item>o'rinbosar — <see cref="SubstituteTeacherService.CoversDate"/> (davomatni o'sha kuni u oladi).</item>
/// </list>
/// <para>Ilgari monitor faqat hafta kuniga qarardi: boshqa kunga ko'chirilgan dars asl kunida
/// "davomat olinmagan" bo'lib qizarib turar, yangi kunida esa umuman ko'rinmasdi.</para>
/// <para>⚠️ Loyihada BAYRAM/dam olish kunlari modeli YO'Q — bunday kunda ham dars rejada turadi
/// (o'qituvchi jadvali va maosh hisobi bilan bir xil).</para>
/// </summary>
public static class AttendanceWatch
{
    public const string PhaseUpcoming = "upcoming";
    public const string PhaseStarted = "started";
    public const string PhaseEnded = "ended";

    /// <summary><see cref="TeacherHomeService.PlanLessons"/> o'qituvchi id'siz ishlamaydi — o'qituvchisi
    /// biriktirilmagan guruh shu belgi bilan rejalashtiriladi (monitor uni "Biriktirilmagan" deb ko'rsatadi).</summary>
    private const string NoTeacherKey = "\u0000no-teacher";

    /// <summary>Kundagi bitta dars.</summary>
    /// <param name="SubstituteTeacherId">Shu kuni darsni o'tadigan o'rinbosar (bo'lmasa bo'sh).</param>
    public sealed record DayLesson(Group Group, string StartTime, string EndTime, bool Moved, string SubstituteTeacherId);

    /// <summary>
    /// <paramref name="day"/> kunidagi barcha darslar. <paramref name="moves"/>/<paramref name="assignments"/> —
    /// guruh id bo'yicha (bazadan bir marta yuklangan).
    /// </summary>
    public static List<DayLesson> LessonsOn(
        IEnumerable<Group> groups,
        ILookup<string, LessonReschedule> moves,
        ILookup<string, SubstituteTeacherAssignment> assignments,
        DateOnly day)
    {
        var result = new List<DayLesson>();
        foreach (var g in groups)
        {
            var owner = string.IsNullOrEmpty(g.TeacherId) ? NoTeacherKey : g.TeacherId;
            var source = new TeacherHomeService.PlanSource(g, true, moves[g.Id].ToList(), assignments[g.Id].ToList());
            foreach (var p in TeacherHomeService.PlanLessons(owner, [source], day, day))
                result.Add(new DayLesson(g, p.StartTime, p.EndTime, p.Moved, p.SubstituteTeacherId));
        }
        return result;
    }

    /// <summary>
    /// Dars bosqichi <paramref name="now"/> (markaz vaqti) ga nisbatan: o'tgan kun — <c>ended</c>,
    /// kelgusi kun — <c>upcoming</c>; bugun — boshlanish/tugash vaqtiga qarab.
    /// <para>⚠️ Bugun, VAQTI noma'lum dars — <c>started</c> (ya'ni davomati kutiladi): "davomat
    /// eslatmasi"ning <c>not_filled</c> rejimi bilan bir xil (<c>LessonAttendanceReminderService</c>),
    /// aks holda vaqtsiz guruh hech qachon "kechikkan" bo'lib ko'rinmasdi.</para>
    /// </summary>
    public static string Phase(string date, string startTime, string endTime, DateTime now)
    {
        var today = now.ToString("yyyy-MM-dd");
        var cmp = string.CompareOrdinal(date, today);
        if (cmp < 0) return PhaseEnded;
        if (cmp > 0) return PhaseUpcoming;

        var start = ScheduleRules.ParseTime(startTime);
        var end = ScheduleRules.ParseTime(endTime);
        var nowMin = now.Hour * 60 + now.Minute;
        if (start is null) return PhaseStarted;
        if (nowMin < start) return PhaseUpcoming;
        if (end is not null && end > start && nowMin >= end) return PhaseEnded;
        return PhaseStarted;
    }

    /// <summary>KECHIKKAN — dars boshlangan (yoki tugagan), lekin davomat hali olinmagan.</summary>
    public static bool Overdue(bool attendanceDone, string phase) => !attendanceDone && phase != PhaseUpcoming;
}
