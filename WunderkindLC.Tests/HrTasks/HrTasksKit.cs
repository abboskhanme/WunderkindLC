using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>A Telegram sender that records instead of sending — tests never touch the network.</summary>
public sealed class FakeHrSender : IHrTelegramSender
{
    public ConcurrentQueue<(long Chat, string Text, string? Url)> Messages { get; } = new();
    public ConcurrentQueue<(long Chat, string Name)> Voices { get; } = new();
    public bool IsConfigured => true;
    public string? BotUsername => "wk_test_bot";

    public Task<bool> SendHtmlAsync(long chatId, string html, string? openUrl = null, CancellationToken ct = default)
    {
        Messages.Enqueue((chatId, html, openUrl));
        return Task.FromResult(true);
    }

    public Task<bool> SendVoiceAsync(long chatId, byte[] bytes, string fileName, string contentType, string caption, CancellationToken ct = default)
    {
        Voices.Enqueue((chatId, fileName));
        return Task.FromResult(true);
    }

    /// <summary>LC: task messages with the «✅ Qabul qildim» button — also queued in <see cref="Messages"/>.</summary>
    public ConcurrentQueue<(long Chat, string TaskId)> TaskButtons { get; } = new();
    public ConcurrentQueue<(long Chat, string Name)> Photos { get; } = new();
    public ConcurrentQueue<(long Chat, string Name)> Documents { get; } = new();

    public Task<bool> SendTaskAsync(long chatId, string html, string? openUrl, string taskId, CancellationToken ct = default)
    {
        Messages.Enqueue((chatId, html, openUrl));
        TaskButtons.Enqueue((chatId, taskId));
        return Task.FromResult(true);
    }

    public Task<bool> SendPhotoAsync(long chatId, byte[] bytes, string fileName, string? caption, CancellationToken ct = default)
    {
        Photos.Enqueue((chatId, fileName));
        return Task.FromResult(true);
    }

    public Task<bool> SendDocumentAsync(long chatId, byte[] bytes, string fileName, string? caption, CancellationToken ct = default)
    {
        Documents.Enqueue((chatId, fileName));
        return Task.FromResult(true);
    }

    public List<string> To(long chat) => [.. Messages.Where(m => m.Chat == chat).Select(m => m.Text)];
}

/// <summary>In-memory file store for service-level tests.</summary>
public sealed class MemoryHrFileStore : IHrFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public Task<string> SaveAsync(string folder, byte[] content, string extension, DateTimeOffset now, CancellationToken ct = default)
    {
        var path = $"hr/{folder}/{Guid.NewGuid():N}{LocalHrFileStore.SafeExtension(extension)}";
        _files[path] = content;
        return Task.FromResult(path);
    }

    public string UrlFor(string relativePath) => "/uploads/" + relativePath;
    public Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default) => Task.FromResult(_files.GetValueOrDefault(relativePath));
    public bool Delete(string relativePath) => _files.TryRemove(relativePath, out _);
}

/// <summary>
/// LC port of the LMS <c>HrTasksKit</c>: the LMS tests run HTTP calls against Testcontainers PostgreSQL;
/// LC tests use the SQLite <see cref="TestDb"/> and call <see cref="HrTasksService"/> directly — the same
/// assertions, one layer down (the controllers are thin <c>Run(...)</c> wrappers, checked at source level in
/// <see cref="HrTasksRbacTests"/>).
/// </summary>
public sealed class HrTasksKit : IDisposable
{
    public static readonly TimeZoneInfo Tz = HrValues.Zone("Asia/Tashkent");

    private static int _phone = Random.Shared.Next(1_000_000, 8_000_000);
    private static long _telegram = 7_000_000_000 + Random.Shared.Next(1, 900_000_000);

    public static string NextPhone() => "+99899" + Interlocked.Increment(ref _phone).ToString("D7");

    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }
    public FakeHrSender Sender { get; } = new();
    public MemoryHrFileStore Files { get; } = new();

    public HrTasksKit()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        // the RESTRICT / CASCADE keys are part of the rules — enforce them like PostgreSQL does
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        Db = NewContext();
        Db.Database.EnsureCreated();
    }

    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>A service over its OWN context (a fresh "request") with the kit's fakes and an optional fixed clock.</summary>
    public HrTasksService Service(DateTimeOffset? now = null, AppDbContext? db = null)
    {
        var service = new HrTasksService(db ?? NewContext(), Sender, Files, NullLogger<HrTasksService>.Instance);
        if (now is { } n) service.Clock = () => n;
        return service;
    }

    public sealed record Person(AppUser User, HrEmployee? Employee, long? Chat);

    /// <summary>A user (+ optional HR row, + optional Telegram link by UserId).</summary>
    public async Task<Person> PersonAsync(
        string role = Roles.Staff, string? hrRole = HrRoles.Employee, string[]? permissions = null,
        bool active = true, bool telegram = false, string? name = null)
    {
        var user = new AppUser
        {
            FullName = name ?? "Xodim " + Guid.NewGuid().ToString("N")[..6],
            Role = role,
            Email = "u" + Guid.NewGuid().ToString("N"),
            PasswordHash = "x",
            Permissions = [.. permissions ?? []],
        };
        Db.Users.Add(user);
        HrEmployee? employee = null;
        if (hrRole is not null)
        {
            employee = new HrEmployee
            {
                UserId = user.Id, HrRole = hrRole, FullName = user.FullName, Phone = NextPhone(), IsActive = active,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            };
            Db.HrEmployees.Add(employee);
        }
        long? chat = null;
        if (telegram)
        {
            chat = Interlocked.Increment(ref _telegram);
            Db.TelegramRegistrations.Add(new TelegramRegistration { UserId = user.Id, ChatId = chat.Value, ParentName = user.FullName });
        }
        await Db.SaveChangesAsync();
        return new Person(user, employee, chat);
    }

    public async Task ReportsToAsync(string employeeId, string managerId)
    {
        Db.HrEmployeeManagers.Add(new HrEmployeeManager { EmployeeId = employeeId, ManagerId = managerId });
        await Db.SaveChangesAsync();
    }

    public async Task<HrTask> TaskAsync(
        string employeeId, DateTimeOffset dueAt, string status = HrTaskStatuses.Open, string source = HrTaskSources.Director,
        string title = "Test topshiriq", string kind = HrTaskKinds.Check, decimal? target = null,
        string policy = HrOverduePolicies.KeepOpen, DateTimeOffset? completedAt = null, string? comment = null,
        string? createdBy = null)
    {
        var task = new HrTask
        {
            EmployeeId = employeeId, Title = title, Source = source, Kind = kind, TargetAmount = target,
            DueAt = dueAt.ToUniversalTime(), PeriodStart = HrValues.LocalDate(dueAt, Tz), Status = status,
            OverduePolicy = policy, CompletedAt = completedAt?.ToUniversalTime(), EmployeeComment = comment,
            CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        Db.HrTasks.Add(task);
        await Db.SaveChangesAsync();
        return task;
    }

    /// <summary>The panel caller as the controller builds it from the JWT.</summary>
    public static HrActor Actor(AppUser user) => new(user.Id, user.Role, user.Permissions);

    public Task<HrPanel> PanelAsync(AppUser user, HrTasksService? service = null) => (service ?? Service()).PanelAsync(Actor(user));

    /// <summary>Local Tashkent wall time → instant.</summary>
    public static DateTimeOffset At(int y, int mo, int d, int h = 12, int mi = 0) =>
        HrValues.At(new DateOnly(y, mo, d), new TimeOnly(h, mi), Tz);

    /// <summary>A DI container like Program.cs (one SQLite database) — for <see cref="HrTasksScheduler"/>.</summary>
    public IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IHrTelegramSender>(Sender);
        services.AddSingleton<IHrFileStore>(Files);
        services.AddScoped<HrTasksService>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
