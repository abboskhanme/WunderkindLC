using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// ADMIN paneli guruh chatining XAVFSIZLIK darvozasi — <see cref="ChatService.CanUseAdminChat"/>.
///
/// Fon: <c>AdminPermAttribute</c> xodim (staff) uchun BARCHA GET so'rovlarini ruxsat tekshirmasdan
/// o'tkazadi — bu ataylab shunday (bo'limlararo o'qish: Moliya → o'quvchilar ro'yxati va h.k.).
/// Lekin <c>GET /api/admin/messages/chat/{className}</c> bo'limlararo ma'lumot emas: u guruh
/// chatidagi yozishmalarni va <c>__xodimlar__</c> kanalini qaytaradi. Darvozasiz qolganda tor
/// ruxsatli xodim (masalan faqat "Kitoblar") ISTALGAN guruh chatini o'qiy olardi.
///
/// Qoida: admin/superadmin — cheklovsiz (mavjud xatti-harakat o'zgarmaydi), xodim — faqat
/// "messages" bo'lim ruxsati bilan (frontend'dagi <c>RequirePerm perm="messages"</c> bilan aynan
/// bir xil), boshqa rollar — umuman yo'q (ular o'z portallaridan a'zolik tekshiruvi bilan kiradi).
/// </summary>
public class ChatAccessTests
{
    [Fact]
    public void Admin_RuxsatTokenisizHam_ChatgaKiradi()
    {
        // Admin/superadminda "perm" claim'lari umuman bo'lmaydi — ular cheklovsiz.
        Assert.True(ChatService.CanUseAdminChat(Roles.Admin, Array.Empty<string>()));
        Assert.True(ChatService.CanUseAdminChat(Roles.Admin, null));
        Assert.True(ChatService.CanUseAdminChat(Roles.SuperAdmin, null));
    }

    [Fact]
    public void Xodim_MessagesToliqRuxsati_ChatgaKiradi()
    {
        // Yalang "messages" = bo'limda TO'LIQ ruxsat (eski/ixcham ko'rinish).
        Assert.True(ChatService.CanUseAdminChat(Roles.Staff, new[] { "students:edit", "messages" }));
    }

    [Theory]
    [InlineData("messages:view")]
    [InlineData("messages:create")]
    [InlineData("messages:edit")]
    [InlineData("messages:delete")]
    public void Xodim_MessagesBolimidagiBirorAmal_ChatgaKiradi(string token)
    {
        // Bo'limda biror amal ruxsati bo'lsa — "Xabarlar" sahifasi UI'da ham ochiladi (can(...,'view')),
        // demak server ham o'qishga ruxsat berishi kerak.
        Assert.True(ChatService.CanUseAdminChat(Roles.Staff, new[] { token }));
    }

    [Fact]
    public void Xodim_MessagesRuxsatiYoq_ChatgaKiraOlmaydi()
    {
        // ASOSIY ZAIFLIK: tor ruxsatli xodim (kitoblar/moliya) chatni o'qiy olmasligi shart.
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "books", "finance:view", "students:edit" }));
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, Array.Empty<string>()));
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, null));
    }

    [Fact]
    public void Xodim_OxshashNomliRuxsat_ChatniOchmaydi()
    {
        // Prefiks bo'yicha aldash bo'lmasin: faqat "messages" yoki "messages:<amal>" o'tadi.
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "messages2" }));
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "messagestemplates" }));
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "supportmessages" }));
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "auto-messages:view" }));
    }

    [Fact]
    public void BoshqaRollar_AdminChatEndpointidan_KiraOlmaydi()
    {
        // O'qituvchi/o'quvchi admin endpointiga AdminPerm sabab yetib kelmaydi, lekin darvoza
        // o'zi ham "ochiq" qolmasligi kerak — ular o'z portalidan, A'ZOLIK tekshiruvi bilan kiradi.
        Assert.False(ChatService.CanUseAdminChat(Roles.Teacher, new[] { "messages" }));
        Assert.False(ChatService.CanUseAdminChat(Roles.Student, new[] { "messages" }));
        Assert.False(ChatService.CanUseAdminChat("", new[] { "messages" }));
        Assert.False(ChatService.CanUseAdminChat(null, new[] { "messages" }));
    }

    [Fact]
    public void XodimlarKanali_AlohidaNomga_Bogliq_Emas()
    {
        // Darvoza kanal nomiga qaramaydi: "messages" ruxsati bo'lsa — barcha kanallar (shu jumladan
        // xodimlar kanali), bo'lmasa — hech biri. Ya'ni __xodimlar__ ni "topib olish" yo'li yo'q.
        Assert.Equal("__xodimlar__", ChatService.StaffChannel);
        Assert.False(ChatService.CanUseAdminChat(Roles.Staff, new[] { "books" }));
        Assert.True(ChatService.CanUseAdminChat(Roles.Staff, new[] { "messages:view" }));
    }
}
