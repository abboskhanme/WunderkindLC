using WunderkindLC.Application.Abstractions;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Fon xizmati: startupda va har 12 soatda joriy oygacha hisoblanmagan
/// oylik to'lovlarni avtomatik hisoblaydi (yangi oyga o'tilganda ishlaydi).
/// </summary>
public class TuitionAccrualService(IServiceProvider services, ILogger<TuitionAccrualService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ⚠️ Import rejimida oylik hisob YOZILMAYDI: yarim ko'chirilgan a'zoliklar ustida
                // hisob yozilsa, ota-onalarga yolg'on qarz xabari ketardi (`ImportMode`).
                if (ImportMode.Enabled)
                {
                    logger.LogWarning("Import rejimi — oylik to'lov hisobi o'tkazib yuborildi");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    continue;
                }
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                // KESH bump'lari TO'PLANADI (`DataCache.BeginBatch`): accrual har o'quvchini ALOHIDA saqlaydi,
                // ya'ni usiz har o'quvchida MonthlyCharge/Student'ga bog'liq og'ir keshlar (bosh sahifa,
                // moliya, reyting) qayta-qayta eskirardi. Bump blok oxirida BIR marta bo'ladi.
                List<string> accrued;
                List<(string StudentId, string Month, decimal Amount)> created;
                using (scope.ServiceProvider.GetService<DataCache>()?.BeginBatch())
                    (accrued, created) = await TuitionService.AccrueDue(db, logger);
                if (accrued.Count > 0)
                    logger.LogInformation("Oylik to'lov hisoblandi: {Months}", string.Join(", ", accrued));
                // Avto xabar — har YANGI hisob uchun ota-onaga ("Oylik hisob yaratilganda" hodisasi).
                var autoMsg = scope.ServiceProvider.GetRequiredService<AutoMessageService>();
                await autoMsg.DispatchMonthlyChargesAsync(db, created);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Oylik to'lovni hisoblashda xatolik");
            }

            try { await Task.Delay(TimeSpan.FromHours(12), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
