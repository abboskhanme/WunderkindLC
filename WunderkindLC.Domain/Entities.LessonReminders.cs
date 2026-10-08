namespace WunderkindLC.Domain;

/// <summary>
/// "Davomat kiriting" eslatmasi YUBORILGANI belgisi (<c>LessonAttendanceReminderService</c>).
/// Kalit — <c>"{qoidaId}:{guruhId|daily}:{sana}"</c>, UNIKAL.
///
/// <para>Nega bazada: ilgari belgi faqat xotirada (<c>HashSet</c>) edi — server qayta yuklansa
/// (deploy) 10 daqiqalik oyna ichida xuddi shu eslatma o'qituvchilarga YANA ketardi ("all"
/// rejimida — barcha o'qituvchiga). Endi belgi yuborishdan OLDIN yoziladi; unikal indeks ikki
/// nusxa ishlab qolgan holatda ham takrorni to'xtatadi. Eski qatorlar bir haftadan keyin
/// tozalanadi.</para>
/// </summary>
public class LessonReminderMark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>"{qoidaId}:{guruhId|daily}:{yyyy-MM-dd}".</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Eslatma kuni ("yyyy-MM-dd") — eski belgilarni tozalash uchun.</summary>
    public string Date { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
