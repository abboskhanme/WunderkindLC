using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;
using Group = WunderkindLC.Domain.Group;

namespace WunderkindLC.Tests;

/// <summary>
/// «DAVOMAT OLINMAGAN GURUHLAR» — <see cref="AttendanceWatch"/>: kunning darslari (ko'chirish,
/// guruh umri, o'rinbosar — o'qituvchi jadvali bilan BITTA manba) va dars bosqichi
/// (kechikkanmi). Manba endpoint — <c>GET api/admin/dashboard/today-lessons</c> (ruxsat darvozasi
/// <see cref="HomeEndpointsRbacTests"/> da qulflangan sinf darajasidagi rol tekshiruvi).
/// </summary>
public class AttendanceWatchTests
{
    // 2026-09-07 — dushanba (hafta kuni indeksi 0).
    private static readonly DateOnly Mon = new(2026, 9, 7);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static Group G(string id, string teacher = "t1", string? startDate = null, params int[] days) => new()
    {
        Id = id, Name = id.ToUpperInvariant(), TeacherId = teacher, CourseId = "c1",
        Days = days.ToList(), StartTime = "14:00", EndTime = "15:30", StartDate = startDate,
    };

    private static List<AttendanceWatch.DayLesson> On(
        DateOnly day, IEnumerable<Group> groups,
        IEnumerable<LessonReschedule>? moves = null, IEnumerable<SubstituteTeacherAssignment>? subs = null) =>
        AttendanceWatch.LessonsOn(groups,
            (moves ?? []).ToLookup(m => m.ClassId, StringComparer.Ordinal),
            (subs ?? []).ToLookup(a => a.GroupId, StringComparer.Ordinal),
            day);

    [Fact]
    public void Dars_kuni_bolgan_guruhlar_chiqadi_boshqalari_yoq()
    {
        var lessons = On(Mon, [G("g1", days: 0), G("g2", days: 2)]);
        Assert.Equal("g1", Assert.Single(lessons).Group.Id);
    }

    [Fact]
    public void Kochirilgan_dars_asl_kunida_YOQ_yangi_kunida_yangi_vaqt_bilan_BOR()
    {
        var move = new LessonReschedule { ClassId = "g1", FromDate = D(Mon), ToDate = D(Mon.AddDays(1)), Time = "16:00" };
        Assert.Empty(On(Mon, [G("g1", days: 0)], [move]));

        var moved = Assert.Single(On(Mon.AddDays(1), [G("g1", days: 0)], [move]));
        Assert.True(moved.Moved);
        Assert.Equal(("16:00", "17:30"), (moved.StartTime, moved.EndTime));
    }

    [Fact]
    public void Hali_boshlanmagan_guruhda_dars_yoq()
        => Assert.Empty(On(Mon, [G("g1", startDate: D(Mon.AddDays(1)), days: 0)]));

    [Fact]
    public void Orinbosar_shu_kunga_tayinlangan_bolsa_korsatiladi()
    {
        var sub = new SubstituteTeacherAssignment
        {
            GroupId = "g1", OriginalTeacherId = "t1", SubstituteTeacherId = "t9",
            Date = D(Mon), EndDate = D(Mon), SelectedDates = [D(Mon)], IsActive = true,
        };
        var lesson = Assert.Single(On(Mon, [G("g1", days: 0)], subs: [sub]));
        Assert.Equal("t9", lesson.SubstituteTeacherId);
    }

    [Fact]
    public void Oqituvchisi_biriktirilmagan_guruh_ham_royxatda()
        => Assert.Single(On(Mon, [G("g1", teacher: "", days: 0)]));

    [Theory]
    [InlineData("2026-09-06", "14:00", "15:30", "10:00", AttendanceWatch.PhaseEnded)]     // o'tgan kun
    [InlineData("2026-09-08", "14:00", "15:30", "10:00", AttendanceWatch.PhaseUpcoming)]  // ertangi kun
    [InlineData("2026-09-07", "14:00", "15:30", "13:59", AttendanceWatch.PhaseUpcoming)]
    [InlineData("2026-09-07", "14:00", "15:30", "14:00", AttendanceWatch.PhaseStarted)]
    [InlineData("2026-09-07", "14:00", "15:30", "15:30", AttendanceWatch.PhaseEnded)]
    [InlineData("2026-09-07", "", "", "08:00", AttendanceWatch.PhaseStarted)]             // vaqti noma'lum
    [InlineData("2026-09-07", "14:00", "", "18:00", AttendanceWatch.PhaseStarted)]        // tugashi noma'lum
    public void Dars_bosqichi(string date, string start, string end, string now, string expected)
    {
        var at = DateTime.Parse($"2026-09-07T{now}:00");
        Assert.Equal(expected, AttendanceWatch.Phase(date, start, end, at));
    }

    [Fact]
    public void Kechikkan_faqat_boshlangan_va_davomati_olinmagan_dars()
    {
        Assert.True(AttendanceWatch.Overdue(false, AttendanceWatch.PhaseStarted));
        Assert.True(AttendanceWatch.Overdue(false, AttendanceWatch.PhaseEnded));
        Assert.False(AttendanceWatch.Overdue(false, AttendanceWatch.PhaseUpcoming));
        Assert.False(AttendanceWatch.Overdue(true, AttendanceWatch.PhaseEnded));
    }
}
