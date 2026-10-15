using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// 2026-10-15 (egasi: "akkauntga kirgan bo'lsa bo'ldi"): ro'yxatlardagi «Telegram» holati kabinetga KIRGAN akkauntni
/// ham «Ulangan» deb sanaydi — <see cref="TelegramLinkInfo.Cabinet"/>. Telegram chatlari soni (<c>Linked</c>) o'zgarmaydi.
/// </summary>
public class TelegramLinkCabinetTests
{
    [Fact]
    public async Task Kabinetga_kirgan_akkaunt_Cabinet__chati_yoq_bo_lsa_ham_ro_yxatda()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.Add(new AppUser { Id = "u1", FullName = "Kirgan", Role = Roles.Student, Email = "u1", FirstLoginAt = "2026-10-10T09:00:00" });
        ctx.Users.Add(new AppUser { Id = "u2", FullName = "Kirmagan", Role = Roles.Student, Email = "u2" });
        ctx.Students.Add(new Student { Id = "s1", FullName = "Kirgan", UserId = "u1" });
        ctx.Students.Add(new Student { Id = "s2", FullName = "Kirmagan", UserId = "u2" });
        ctx.Students.Add(new Student { Id = "s3", FullName = "Chatli" });
        ctx.TelegramRegistrations.Add(new TelegramRegistration { StudentId = "s3", ChatId = 777, Relation = "father" });
        await ctx.SaveChangesAsync();

        var map = await TelegramLinkSummary.ForStudentsAsync(ctx, ["s1", "s2", "s3"]);

        Assert.True(map["s1"].Cabinet);
        Assert.Equal(0, map["s1"].Linked);          // Telegram'ga xabar ketmaydi — chat yo'q
        Assert.False(map.ContainsKey("s2"));        // kirmagan, chati yo'q — «Ulanmagan»
        Assert.False(map["s3"].Cabinet);
        Assert.Equal(1, map["s3"].Linked);
    }
}
