namespace WunderkindLC.Application.Dtos;

/// <summary>
/// Guruh sahifasi → "O'quvchilar" tabining bitta qatori (edutizim ko'rinishi): a'zolar ro'yxatidagi
/// (<see cref="GroupMemberDto"/>) HAMMA maydon + jadvalga kerak bo'lgan ko'rsatish maydonlari.
///
/// <para>⚠️ <see cref="GroupMemberDto"/> ATAYIN o'zgartirilmadi (u telefonsiz qolishi kerak —
/// a'zolar oynasi va boshqa joylar uni ishlatadi). Bu DTO faqat shu tab uchun.</para>
/// </summary>
/// <param name="Balance">SHU GURUH bo'yicha balans (<c>GroupBalanceService</c>) — umumiy balans emas.</param>
/// <param name="CourseCount">O'quvchi hozir o'qiyotgan (muzlatilmagan joriy a'zoliklardagi) turli
/// kurslar soni — 2 va undan ko'p bo'lsa ism ostida "N ta Kurs" chipi chiqadi.</param>
/// <param name="Price">Shu oy uchun o'quvchining narxi (guruh narxi − amaldagi chegirma,
/// <c>DiscountBook</c>). FAQAT moliya ruxsati borga (chegirma summasi nozik —
/// <c>discounts.md</c>, <c>permissions.md</c> §10); qolganlarga <c>null</c> — klient guruh
/// narxini ko'rsatadi.</param>
/// <param name="LastNote">Oxirgi izoh matni (qisqartirilgan). FAQAT <c>students.list</c> /
/// <c>students.notes</c> ruxsati borga (<c>student-notes.md</c> §4 — izohlar jamlanmasi nozik);
/// qolganlarga bo'sh.</param>
public record GroupRosterRowDto(
    string StudentId, string FullName, string JoinedAt, string? LeftAt, bool IsActive,
    string Status, string ActivatedAt, string FrozenAt, decimal Balance, bool YearFreeze,
    string Phone, int CourseCount, decimal? Price, string LastNote, string LastNoteAt);
