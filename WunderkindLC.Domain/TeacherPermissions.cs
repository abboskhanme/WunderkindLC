namespace WunderkindLC.Domain;

/// <summary>
/// O'qituvchi web panelidagi bo'limlar (ruxsatlar) kalitlari. Admin har o'qituvchiga qaysi
/// bo'limlardan foydalanishni belgilaydi (Teacher.Permissions). "Bosh sahifa" har doim ochiq.
/// </summary>
public static class TeacherPermissions
{
    public const string Journal = "journal";
    public const string Schedule = "schedule";
    public const string Salary = "salary";
    // ("messages" — guruh chati — 2026-10 da chat bilan birga olib tashlandi; eski o'qituvchilarning
    //  Permissions ro'yxatida qolgan "messages" qiymati hech narsani ochmaydi.)

    /// <summary>Barcha mavjud bo'lim kalitlari (yangi o'qituvchi uchun standart — hammasi ochiq).</summary>
    public static readonly string[] All = { Journal, Schedule, Salary };
}
