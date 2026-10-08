using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Lid SINOV DARSI natijasi qoidalari (sof, testlangan). <c>LeadsController.SetTrialResult</c> va
/// konversiya (<see cref="LeadConversion.ResolvePendingTrialsAsync"/>) shu ta'rifga tayanadi.
/// </summary>
public static class LeadTrialRules
{
    /// <summary>"Sinovga KELGAN" natijalar — tashrifdan keyingi qaror (qoldi/ketdi) ham kelgan hisoblanadi.</summary>
    public static bool IsCame(string? result) => result is "came" or "stayed" or "left";

    /// <summary>
    /// Natijaga mos KELGAN SANA (<see cref="TrialLesson.AttendedAt"/>):
    /// <list type="bullet">
    ///   <item>"keldi" oilasi (came/stayed/left) — bo'sh bo'lsa BUGUN; oila ICHIDA tuzatishda
    ///     (keldi → qoldi) mavjud sana SAQLANADI (tashrif kuni o'zgarmagan);</item>
    ///   <item>kelmagan holat (no_show/pending) — sana TOZALANADI.</item>
    /// </list>
    /// <para>⚠️ Tozalash SHART: KPI <c>trialCame</c> (<c>KpiMetricsService</c>) <c>AttendedAt</c> to'la
    /// har qanday sinovni "keldi" deb sanaydi — xato "Keldi" bosilib keyin "Kelmadi" ga tuzatilsa,
    /// sana qolib ketib, KPI yolg'on chiqardi.</para>
    /// </summary>
    public static void ApplyAttendance(TrialLesson trial, string result, DateOnly today)
    {
        if (IsCame(result))
        {
            if (string.IsNullOrWhiteSpace(trial.AttendedAt)) trial.AttendedAt = today.ToString("yyyy-MM-dd");
        }
        else
        {
            trial.AttendedAt = null;
        }
    }
}
