namespace WunderkindLC.Domain;

// ============================ QABUL CHEKLISTI (yangi o'quvchi) ============================
// Retseptsiya/administrator yangi o'quvchini qabul qilganda 5 ta ishni bajaradi: ma'lumotlarni
// to'ldirish, daraja testi, shartnoma, Telegramga ulash, to'lovni qabul qilish. Ularning
// aksariyati MAVJUD yozuvlardan AVTOMATIK aniqlanadi (`StudentIntake` — Application/Services),
// shuning uchun bazada FAQAT QO'LDA qo'yilgan belgilar saqlanadi. Batafsil:
// `.claude/rules/student-intake.md`.

/// <summary>
/// Qabul cheklistining QO'LDA qo'yilgan belgisi — "bajarildi" (masalan daraja testi qog'ozda
/// olingan, shartnoma qo'lda imzolangan va tizimda izi yo'q).
///
/// <para>⚠️ AVTOMATIK holat bu yerda SAQLANMAYDI — u har so'rovda mavjud jadvallardan
/// hisoblanadi (lid → daraja testi, <see cref="Contract"/>, <see cref="TelegramRegistration"/>,
/// <see cref="FinanceTransaction"/>). Ya'ni eski o'quvchilar uchun backfill KERAK EMAS.</para>
///
/// <para>Har <c>(StudentId, Item)</c> juftligi uchun ko'pi bilan BITTA qator (unikal indeks):
/// belgini olib tashlash — qatorni o'chirish (tarix «O'zgarishlar tarixi»da qoladi).</para>
/// </summary>
public class StudentIntakeMark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Band kaliti: <c>profile</c> | <c>levelTest</c> | <c>contract</c> | <c>telegram</c> |
    /// <c>payment</c> (katalog — <c>StudentIntake.Items</c>).</summary>
    public string Item { get; set; } = string.Empty;
    /// <summary>Ixtiyoriy izoh ("qog'ozda test olindi, natija: B1").</summary>
    public string Note { get; set; } = string.Empty;
    /// <summary>Belgilagan xodim (<see cref="AppUser"/>.Id) va ismi — SNAPSHOT (xodim o'chsa ham qoladi).</summary>
    public string MarkedById { get; set; } = string.Empty;
    public string MarkedByName { get; set; } = string.Empty;
    /// <summary>Belgilangan vaqt (markaz vaqti, "yyyy-MM-ddTHH:mm:ss").</summary>
    public string MarkedAt { get; set; } = string.Empty;
}
