namespace WunderkindLC.Domain;

/* =====================================================================================
 *  MOSLASHUV (onboarding) — yangi xodimning 30 kunlik adaptatsiyasi.
 *  "Boshqaruv → Moslashuv" (`/admin/boshqaruv/moslashuv`, ruxsat `vacancies.onboarding`).
 *  Qoidalar: `.claude/rules/onboarding.md`.
 *
 *  Uch qatlam:
 *    OnboardingStep          — QADAMLAR KATALOGI (tahrirlanadigan andoza, 11 ta standart qadam);
 *    EmployeeOnboarding      — bitta xodimning moslashuvi (boshlanish sanasi, mentor, holat);
 *    EmployeeOnboardingTask  — o'sha xodimning har bir qadami (bajarildimi, kim, qachon, izoh).
 *
 *  ⚠️ Moslashuv boshlanganda katalog qadamlari NUSXA qilinadi (task'larga snapshot) — katalog
 *  keyin tahrirlansa yoki qadam o'chirilsa ham davom etayotgan moslashuvlar BUZILMAYDI.
 * ===================================================================================== */

/// <summary>
/// Moslashuv cheklistining KATALOG qadami (andoza). Yangi moslashuv boshlanganda FAOL qadamlar
/// <see cref="EmployeeOnboardingTask"/> ga nusxa qilinadi. Bo'sh bazada 11 ta standart qadam
/// bilan to'ldiriladi (faqat jadval BO'SH bo'lsa — admin o'chirgani qayta tug'ilmaydi).
/// </summary>
public class OnboardingStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Qadam nomi ("Mehnat shartnomasini imzolash").</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Qisqa izoh — nima qilinadi, kim mas'ul (ixtiyoriy).</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Boshlanishdan necha kun ichida bajarilishi kerak (0..30). Muddat =
    /// boshlanish sanasi + shu kun; o'tib ketsa va bajarilmagan bo'lsa — KECHIKKAN.</summary>
    public int DayOffset { get; set; }
    /// <summary>Cheklistdagi tartibi (kichigi tepada).</summary>
    public int Order { get; set; }
    /// <summary>O'chirilgan qadam YANGI moslashuvlarga nusxa qilinmaydi (eskilari tegilmaydi).</summary>
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Bitta yangi xodimning MOSLASHUVI. Ikki yo'l bilan boshlanadi: ariza "Ishga qabul qilindi"
/// bosqichiga o'tganda AVTOMATIK (<see cref="JobApplicationId"/> bilan, idempotent) yoki qo'lda
/// ("Yangi xodim moslashuvi"). Ism/telefon/lavozim — SNAPSHOT (ariza o'chirilsa ham o'qiladi).
/// </summary>
public class EmployeeOnboarding
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    /// <summary>Lavozim (avtomatik boshlanganda — vakansiya nomi).</summary>
    public string Position { get; set; } = string.Empty;

    /* ---------- Bog'lanishlar (hammasi ixtiyoriy, "" = yo'q) ---------- */
    /// <summary>Qaysi arizadan boshlangan (avtomatik yo'l). Bitta arizaga bitta moslashuv.</summary>
    public string JobApplicationId { get; set; } = string.Empty;
    /// <summary>Ariza raqami nusxasi (#12) — ro'yxatda ko'rsatish uchun.</summary>
    public int ApplicationNumber { get; set; }
    /// <summary>Tizimdagi o'qituvchi yozuvi (agar xodim o'qituvchi bo'lib qo'shilgan bo'lsa).</summary>
    public string TeacherId { get; set; } = string.Empty;
    /// <summary>Tizimdagi xodim (staff) akkaunti.</summary>
    public string StaffUserId { get; set; } = string.Empty;

    /// <summary>Boshlanish sanasi ("yyyy-MM-dd") — qadam muddatlari shundan hisoblanadi.</summary>
    public string StartDate { get; set; } = string.Empty;
    /// <summary>Mentor (o'qituvchi yoki xodim) id'si va ismi (snapshot).</summary>
    public string MentorId { get; set; } = string.Empty;
    public string MentorName { get; set; } = string.Empty;

    /// <summary>"active" | "completed" (hamma qadam bajarildi) | "cancelled" (to'xtatildi).</summary>
    public string Status { get; set; } = "active";
    /// <summary>"hired" (ariza qabul qilinganda avtomatik) | "manual" (qo'lda).</summary>
    public string Source { get; set; } = "manual";
    public string Note { get; set; } = string.Empty;

    public string CompletedAt { get; set; } = string.Empty;
    public string CancelledAt { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Moslashuvning bitta QADAMI — katalogdan nusxa (<see cref="StepId"/> faqat kelib chiqishi uchun).
/// Bajarilgan = <see cref="DoneAt"/> bo'sh emas.
/// </summary>
public class EmployeeOnboardingTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OnboardingId { get; set; } = string.Empty;
    /// <summary>Katalogdagi asl qadam (o'chirilgan bo'lishi mumkin — nusxa baribir ishlaydi).</summary>
    public string StepId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DayOffset { get; set; }
    public int Order { get; set; }

    /// <summary>Bajarilgan vaqt (ISO, markaz vaqti) — bo'sh bo'lsa bajarilmagan.</summary>
    public string DoneAt { get; set; } = string.Empty;
    public string DoneBy { get; set; } = string.Empty;
    /// <summary>Izoh ("shartnoma №45", "kanalga qo'shildi").</summary>
    public string Note { get; set; } = string.Empty;
}
