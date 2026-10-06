namespace WunderkindLC.Application.Dtos;

// O'qituvchi portali — bosh sahifa, dars jadvali va "O'quvchilarim" (api/teacher/overview|schedule|students).
// Hammasi TeacherHomeService da quriladi; controller faqat darvoza (rol + ruxsat).

/// <summary>
/// O'qituvchining bitta DARSI (aniq sanada). Guruh kunlari + bir martalik ko'chirishlar +
/// o'rinbosar tayinlovlaridan hisoblanadi.
/// <para><c>IsSubstitute</c> — bu darsni o'qituvchi O'RINBOSAR sifatida o'tadi (guruh boshqa odamniki).
/// <c>SubstitutedBy</c> — guruh o'qituvchiniki, lekin shu kuni darsni boshqa odam (o'rinbosar) o'tadi.
/// <c>Moved</c> — dars boshqa kundan shu kunga ko'chirilgan (vaqti <c>StartTime</c> da).
/// <c>Conducted</c> — jurnalda shu dars "o'tildi" (davomat/baho kiritilgan).</para>
/// </summary>
public record TeacherLessonDto(
    string GroupId, string GroupName, string CourseName, string Date,
    string StartTime, string EndTime, string Room, int StudentCount,
    bool IsSubstitute, string SubstitutedBy, bool Moved, bool Conducted);

/// <summary>O'qituvchining guruh kartasi — jadval (kunlar/vaqt/xona) va o'quvchilar soni bilan.
/// Sanoqlar o'qituvchi jurnalida KO'RINADIGAN o'quvchilar bo'yicha (to'lov darvozasi bilan
/// yashirilganlar kirmaydi — jurnal bilan bir xil son chiqsin).</summary>
public record TeacherGroupCardDto(
    string Id, string Name, string CourseId, string CourseName, List<int> Days,
    string StartTime, string EndTime, string Room, string StartDate,
    int Active, int Trial, int Frozen, bool IsSubstitute, string NextLessonDate);

/// <summary>Bosh sahifa: guruhlar + bugungi darslar + jamlanma.
/// <c>CanSchedule=false</c> bo'lsa (ruxsat "Dars jadvali" berilmagan) <c>TodayLessons</c> bo'sh.</summary>
public record TeacherOverviewDto(
    string Today, List<TeacherGroupCardDto> Groups, List<TeacherLessonDto> TodayLessons,
    int StudentCount, bool CanSchedule,
    // O'z guruhlaridagi QARZDORLAR soni (distinct) va jami qarz — uchrashuv talabi (2026-10-07).
    // ⚠️ Faqat JAMI: o'quvchi kesimidagi summa o'qituvchiga ko'rsatilmaydi (`teacher-portal.md`).
    int DebtorCount = 0, decimal DebtTotal = 0);

/// <summary>O'qituvchining o'z guruhlari bo'yicha qarzdorlik jamlanmasi.</summary>
public record TeacherDebtSummaryDto(int DebtorCount, decimal DebtTotal);

/// <summary>"O'quvchilarim" qatori — bitta a'zolik (o'quvchi bir nechta guruhda bo'lsa bir nechta qator).
/// Davomat sanoqlari tanlangan OY bo'yicha, <c>StudentJournalBuilder</c> qoidasida
/// (o'quvchi portali va admin profilidagi jurnal bilan bir xil).</summary>
public record TeacherStudentRowDto(
    string StudentId, string FullName, string PhotoUrl, string GroupId, string GroupName,
    string CourseName, string Status, string MemberStart, int DebtMonths,
    int Held, int Attended, int Absent, int Late, int? AttendancePct);

public record TeacherStudentsDto(string Month, List<TeacherStudentRowDto> Rows);
