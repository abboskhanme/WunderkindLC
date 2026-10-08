using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// STANDART LOCAL SMS AGENTI (<see cref="CenterMeta.LocalSmsDefaultAgentId"/>) holati — ogohlantirishlar
/// uchun yagona joy (sof qism testlangan: <c>AuditFixFx4Tests</c>).
///
/// <para><b>NEGA:</b> barcha avtomatik/fon Local SMS'lar (avto-xabar qoidalari, eslatmalar) FAQAT
/// standart agent orqali ketadi va <see cref="CtiSmsService"/> o'chirilgan agentni ishlatmaydi. Ilgari
/// standart agentni o'chirish (Local Call → Agentlar) yoki uning xodimini o'chirish (agent ham o'chadi)
/// HAMMA avtomatik SMS'ni JIMGINA to'xtatardi — hech qayerda aytilmasdi. Endi: o'chirish javobida
/// ogohlantirish, xodim o'chirilganda audit matnida eslatma, SMS sozlamalarida esa doimiy banner.</para>
///
/// <para>Local SMS o'chiq bo'lsa (<see cref="CenterMeta.LocalSmsEnabled"/>) hech narsa to'xtamaydi —
/// ogohlantirish ham yo'q (yoqishda <c>SaveLocalSms</c> o'chirilgan agentni baribir qabul qilmaydi).</para>
/// </summary>
public static class LocalSmsDefaultAgent
{
    /// <summary>SMS sozlamalaridagi banner matni; <c>null</c> — muammo yo'q (yoki Local SMS o'chiq).
    /// <paramref name="agent"/> — <paramref name="defaultAgentId"/> bo'yicha topilgan agent (yo'q bo'lsa null).</summary>
    public static string? Problem(bool enabled, string? defaultAgentId, CtiAgent? agent)
    {
        if (!enabled) return null;
        if (string.IsNullOrWhiteSpace(defaultAgentId))
            return "Standart agent tanlanmagan — Local SMS tanlangan avto-xabarlar va eslatmalar YUBORILMAYDI. Agentni tanlang.";
        if (agent is null)
            return "Standart agent topilmadi — Local SMS tanlangan avto-xabarlar va eslatmalar YUBORILMAYAPTI. Boshqa agentni tanlang.";
        if (!agent.IsActive)
            return $"Standart agent \"{NameOf(agent)}\" o'chirilgan — Local SMS tanlangan avto-xabarlar va eslatmalar " +
                   "YUBORILMAYAPTI. Agentni Call Center → Local Call → Agentlar da faollashtiring yoki boshqasini tanlang.";
        return null;
    }

    /// <summary>Standart agent o'chirilgandan KEYINgi ogohlantirish (agentni tahrirlash / xodimni o'chirish javobi).</summary>
    public static string StoppedWarning(string agentName) =>
        $"Diqqat: \"{agentName}\" standart Local SMS agenti edi — endi barcha avtomatik Local SMS'lar " +
        "(avto-xabarlar, eslatmalar) YUBORILMAYDI. Sozlamalar → Xabar kanallari → SMS da boshqa agentni tanlang.";

    /// <summary>Berilgan agentlardan biri hozir standart Local SMS agentimi (va Local SMS yoqilganmi).
    /// Mos kelganining id'si, aks holda <c>null</c>.</summary>
    public static async Task<string?> DefaultAmongAsync(
        IAppDbContext db, IReadOnlyCollection<string> agentIds, CancellationToken ct = default)
    {
        if (agentIds.Count == 0) return null;
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        return meta is { LocalSmsEnabled: true, LocalSmsDefaultAgentId: { Length: > 0 } id } && agentIds.Contains(id)
            ? id : null;
    }

    public static string NameOf(CtiAgent a) => a.DisplayName.Length > 0 ? a.DisplayName : a.Login;
}
