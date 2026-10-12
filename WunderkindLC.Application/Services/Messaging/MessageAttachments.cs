using System.Text.RegularExpressions;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// ATTACHMENT RULES (SPEC P2.7 «Attachment limits», pure, <c>MessageAttachmentTests</c>): one file per message —
/// photo <c>.jpg/.jpeg/.png</c> ≤ 10 MB, video <c>.mp4</c> ≤ 50 MB, document <c>.pdf/.docx/.xlsx/.pptx/.doc/.xls/.ppt</c>
/// ≤ 50 MB. Extension, <c>Content-Type</c> and the file's magic bytes must ALL agree — trusting the extension alone
/// would let an HTML file named <c>.jpg</c> through.
/// </summary>
public static class MessageAttachmentRules
{
    public const long MaxPhotoBytes = 10L * 1024 * 1024;
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public const string ErrType = "Fayl turi qo'llab-quvvatlanmaydi (jpg, png, mp4, pdf, docx, xlsx, pptx, doc, xls, ppt)";
    public const string ErrPhotoSize = "Rasm 10 MB dan oshmasin";
    public const string ErrFileSize = "Video va fayl 50 MB dan oshmasin";
    public const string ErrMagic = "Fayl mazmuni kengaytmasiga mos emas";
    public const string ErrEmpty = "Fayl bo'sh";

    private sealed record Spec(string Kind, string[] ContentTypes, Func<byte[], bool> Magic);

    private static bool Starts(byte[] h, params byte[] sig) => h.Length >= sig.Length && sig.Select((b, i) => h[i] == b).All(x => x);
    private static bool Ftyp(byte[] h) => h.Length >= 8 && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p';

    private static readonly Spec Jpeg = new("photo", new[] { "image/jpeg", "image/jpg", "image/pjpeg" }, h => Starts(h, 0xFF, 0xD8, 0xFF));
    private static readonly Spec Png = new("photo", new[] { "image/png" }, h => Starts(h, 0x89, 0x50, 0x4E, 0x47));
    private static readonly Func<byte[], bool> Zip = h => Starts(h, 0x50, 0x4B, 0x03, 0x04);
    private static readonly Func<byte[], bool> Ole = h => Starts(h, 0xD0, 0xCF, 0x11, 0xE0);

    private static readonly Dictionary<string, Spec> ByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = Png,
        [".mp4"] = new("video", new[] { "video/mp4" }, Ftyp),
        [".pdf"] = new("document", new[] { "application/pdf" }, h => Starts(h, 0x25, 0x50, 0x44, 0x46)),
        [".docx"] = new("document", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }, Zip),
        [".xlsx"] = new("document", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }, Zip),
        [".pptx"] = new("document", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" }, Zip),
        [".doc"] = new("document", new[] { "application/msword" }, Ole),
        [".xls"] = new("document", new[] { "application/vnd.ms-excel" }, Ole),
        [".ppt"] = new("document", new[] { "application/vnd.ms-powerpoint" }, Ole),
    };

    public static IReadOnlyCollection<string> Extensions => ByExt.Keys;

    /// <summary>Validates an upload. <paramref name="header"/> = the first (up to) 8 bytes of the file.
    /// Returns the attachment kind or an Uzbek error.</summary>
    public static (string? Kind, string? Error) Validate(string? fileName, string? contentType, long length, byte[] header)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (!ByExt.TryGetValue(ext, out var spec)) return (null, ErrType);
        if (length <= 0) return (null, ErrEmpty);
        if (spec.Kind == "photo" && length > MaxPhotoBytes) return (null, ErrPhotoSize);
        if (length > MaxFileBytes) return (null, ErrFileSize);
        var ct = (contentType ?? "").Split(';')[0].Trim();
        if (!spec.ContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase)) return (null, ErrMagic);
        if (!spec.Magic(header)) return (null, ErrMagic);
        return (spec.Kind, null);
    }

    /// <summary>Display name: path separators and control characters removed, ≤ 200 chars (extension kept).</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "fayl";
        if (name.Length <= 200) return name;
        var ext = Path.GetExtension(name);
        return name[..(200 - ext.Length)] + ext;
    }

    /// <summary>Canonical stored content type for an extension (what the worker sends to Telegram).</summary>
    public static string ContentTypeOf(string ext) =>
        ByExt.TryGetValue(ext, out var s) ? s.ContentTypes[0] : "application/octet-stream";
}

/// <summary>
/// PRIVATE STORAGE of message attachments — <c>{uploads}/messaging/{guid:N}{ext}</c>. The folder is in the
/// <c>PrivateFolderFileProvider</c> set (never served statically, <c>uploads-security.md</c>); files are read only by
/// the outbox worker and the authorised <c>attachments/{id}/content</c> endpoint. Singleton.
/// </summary>
public sealed class MessageAttachmentStore(string uploadsRoot)
{
    public const string FolderName = "messaging";

    private static readonly Regex StoredRx = new(
        @"^/uploads/messaging/(?<name>[0-9a-f]{32}\.(?:jpg|jpeg|png|mp4|pdf|docx|xlsx|pptx|doc|xls|ppt))$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Folder => Path.Combine(uploadsRoot, FolderName);

    /// <summary>Physical path of a stored url — only for the server's own name pattern inside the folder
    /// (no <c>..</c>, no other folder); otherwise <c>null</c>.</summary>
    public string? PhysicalPath(string? fileUrl)
    {
        var m = StoredRx.Match(fileUrl ?? "");
        if (!m.Success) return null;
        var folder = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(folder, m.Groups["name"].Value));
        return full.StartsWith(folder, StringComparison.Ordinal) ? full : null;
    }

    public bool Exists(string? fileUrl) => PhysicalPath(fileUrl) is { } p && File.Exists(p);

    /// <summary>Streams <paramref name="head"/> + the rest of <paramref name="input"/> to a new GUID-named file.</summary>
    public async Task<(string FileUrl, long Size)> SaveAsync(byte[] head, Stream input, string ext, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var name = Guid.NewGuid().ToString("N") + ext.ToLowerInvariant();
        var path = Path.Combine(Folder, name);
        await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await fs.WriteAsync(head, ct);
            await input.CopyToAsync(fs, ct);
        }
        return ($"/uploads/{FolderName}/{name}", new FileInfo(path).Length);
    }

    public Stream OpenRead(string fileUrl) =>
        new FileStream(PhysicalPath(fileUrl) ?? throw new FileNotFoundException("attachment"),
            FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string? fileUrl)
    {
        if (PhysicalPath(fileUrl) is { } p && File.Exists(p)) File.Delete(p);
    }
}

/// <summary>Creating an attachment from an upload (validation + private storage + row). Does NOT save the row.</summary>
public static class MessageAttachments
{
    public static async Task<(MessageAttachment? Attachment, string? Error)> CreateAsync(
        IAppDbContext db, MessageAttachmentStore store, Stream input, string? fileName, string? contentType, long length,
        string? actorId, string actorName, CancellationToken ct = default)
    {
        var head = new byte[8];
        var read = 0;
        while (read < head.Length)
        {
            var n = await input.ReadAsync(head.AsMemory(read, head.Length - read), ct);
            if (n == 0) break;
            read += n;
        }
        var header = read == head.Length ? head : head[..read];
        var (kind, error) = MessageAttachmentRules.Validate(fileName, contentType, length, header);
        if (error is not null) return (null, error);

        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var (url, size) = await store.SaveAsync(header, input, ext, ct);
        if (size > (kind == "photo" ? MessageAttachmentRules.MaxPhotoBytes : MessageAttachmentRules.MaxFileBytes))
        {
            store.Delete(url);
            return (null, kind == "photo" ? MessageAttachmentRules.ErrPhotoSize : MessageAttachmentRules.ErrFileSize);
        }
        var a = new MessageAttachment
        {
            Kind = kind!,
            FileName = MessageAttachmentRules.SanitizeFileName(fileName),
            FileUrl = url,
            ContentType = MessageAttachmentRules.ContentTypeOf(ext),
            SizeBytes = size,
            CreatedById = actorId,
            CreatedByName = actorName,
            CreatedAt = AppClock.Now,
        };
        db.MessageAttachments.Add(a);
        return (a, null);
    }
}
