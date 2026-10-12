using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Pre-release review nits (2026-10-12): cleanup of the CRM Phase 3 tables on group deletion / student purge, the
/// removed legacy template writers, the nightly tar backup without <c>uploads/messaging</c>, «Telegram xabar» buttons
/// only for <c>messages.broadcast</c> and the error-visibility §4 comments.
/// </summary>
public class ReviewFixNitsTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Repo root (WunderkindLC.slnx) not found");
            return dir!.FullName;
        }
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public async Task Group_deletion_removes_the_groups_fines_and_streak_alerts_only()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Name = "Xato guruh" };
        var other = new Group { Name = "Boshqa" };
        ctx.Classes.AddRange(g, other);
        ctx.TeacherAttendanceFines.AddRange(
            new TeacherAttendanceFine { TeacherId = "t", GroupId = g.Id, LessonDate = "2026-10-05", Month = "2026-10", Amount = 1 },
            new TeacherAttendanceFine { TeacherId = "t", GroupId = other.Id, LessonDate = "2026-10-05", Month = "2026-10", Amount = 1 });
        ctx.AbsenceStreakAlerts.AddRange(
            new AbsenceStreakAlert { StudentId = "s", GroupId = g.Id, StreakStart = "2026-10-01", Level = 3 },
            new AbsenceStreakAlert { StudentId = "s", GroupId = other.Id, StreakStart = "2026-10-01", Level = 3 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var res = await GroupDeletion.PurgeLearningDataAsync(ctx, await ctx.Classes.FirstAsync(x => x.Id == g.Id), "Admin");
        await ctx.SaveChangesAsync();

        Assert.True(res.Other >= 2);
        Assert.Equal(other.Id, (await ctx.TeacherAttendanceFines.SingleAsync()).GroupId);
        Assert.Equal(other.Id, (await ctx.AbsenceStreakAlerts.SingleAsync()).GroupId);
    }

    [Fact]
    public async Task Student_purge_removes_the_students_streak_alerts_only()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var s = new Student { FullName = "O'chiriladi" };
        var keep = new Student { FullName = "Qoladi" };
        ctx.Students.AddRange(s, keep);
        ctx.AbsenceStreakAlerts.AddRange(
            new AbsenceStreakAlert { StudentId = s.Id, GroupId = "g", StreakStart = "2026-10-01", Level = 3 },
            new AbsenceStreakAlert { StudentId = keep.Id, GroupId = "g", StreakStart = "2026-10-01", Level = 3 });
        await ctx.SaveChangesAsync();

        Assert.NotNull(await StudentPurge.PurgeAsync(ctx, s.Id));
        await ctx.SaveChangesAsync();

        Assert.Equal(keep.Id, (await ctx.AbsenceStreakAlerts.SingleAsync()).StudentId);
    }

    [Fact]
    public void Legacy_template_writers_are_gone__the_validated_messaging_endpoints_stay()
    {
        var legacy = Src("WunderkindLC.Server", "Controllers", "MessagesController.cs");
        Assert.DoesNotContain("[HttpPost(\"templates\")]", legacy);
        Assert.DoesNotContain("[HttpPut(\"templates/{id}\")]", legacy);
        Assert.DoesNotContain("[HttpDelete(\"templates/{id}\")]", legacy);
        Assert.Contains("[HttpGet(\"templates\")]", legacy);   // read stays

        var current = Src("WunderkindLC.Server", "Controllers", "MessagingController.cs");
        Assert.Contains("CreateTemplate(", current);
        Assert.Contains("UpdateTemplate(", current);
        Assert.Contains("DeleteTemplate(", current);
    }

    [Fact]
    public void Nightly_tar_backup_excludes_messaging_attachments_like_face_selfies()
    {
        var compose = Src("docker-compose.yml");
        Assert.Contains("--exclude='uploads/messaging'", compose);
        Assert.Contains("--exclude='uploads/face'", compose);
    }

    [Fact]
    public void Telegram_message_buttons_are_shown_only_with_the_broadcast_permission()
    {
        foreach (var file in new[]
                 {
                     new[] { "pages", "admin", "classes", "ClassDetailPage.tsx" },
                     new[] { "pages", "admin", "students", "StudentDetailPage.tsx" },
                     new[] { "pages", "admin", "students", "StudentAbsencePage.tsx" },
                     new[] { "pages", "admin", "students", "StudentsPage.tsx" },
                 })
            Assert.Contains("can('messages.broadcast', 'create')",
                Src(new[] { "WunderkindLC.Client", "src" }.Concat(file).ToArray()));

        // The profile card renders the button only when the page passes a handler.
        var card = Src("WunderkindLC.Client", "src", "pages", "admin", "students", "profile", "ProfileCard.tsx");
        Assert.Contains("onMessage?: () => void", card);
        Assert.Contains("{onMessage && (", card);
    }

    [Fact]
    public void Swallowed_background_reads_carry_the_error_visibility_comment()
    {
        Assert.Contains("error-visibility.md §4",
            Src("WunderkindLC.Client", "src", "components", "messaging", "useTelegramDraft.ts"));
        Assert.Contains("error-visibility.md §4",
            Src("WunderkindLC.Client", "src", "pages", "admin", "messages", "AutoMessagesTab.tsx"));
    }
}
