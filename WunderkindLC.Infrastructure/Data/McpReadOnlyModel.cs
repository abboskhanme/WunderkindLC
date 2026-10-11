using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WunderkindLC.Application.Mcp;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>
/// The EF model of the MCP read-only context = the read-only role's grant (docs/SPEC-mcp.md §3).
///
/// <para>
/// Every property the role may not read (<see cref="McpReadOnlyGrants.GrantedProperties"/> — sensitive names
/// such as <c>Student.ParentPassportUrl</c>, <c>TelegramRegistration.ChatId</c>, <c>TestResult.AnswerKey</c>, and
/// everything outside the <c>Users</c> / <c>CenterMeta</c> / <c>AuditLog</c> allow-lists) is removed from the model
/// of the contexts <see cref="ReadOnlyDatabase"/> hands out. So every query on them — including a full-entity
/// load inside a shared panel service (<c>db.Students.FirstOrDefaultAsync(…)</c>, <c>db.Teachers.FindAsync(…)</c>)
/// — selects only granted columns and never hits PostgreSQL's <c>permission denied</c>, for today's tools and
/// for any tool or column added later. Removed properties keep their C# default on materialised entities; a
/// query that NAMES one (<c>Where(c =&gt; c.PdfUrl != "")</c>) fails to translate — loudly, on SQLite in tests too.
/// </para>
/// <para>The web panel's model is untouched: only the read-only options replace the customizer.</para>
/// </summary>
public static class McpReadOnlyModel
{
    /// <summary>Marks the reduced model, so the grants are never computed from it (<see cref="McpReadOnlyRole.Grants"/>).</summary>
    public const string Annotation = "WunderkindLC:McpReadOnlyModel";

    /// <summary>
    /// Removes every non-granted property of the entities the tools read. Other entities are left as they
    /// are: their tables are not granted at all.
    /// </summary>
    public static void Reduce(ModelBuilder modelBuilder)
    {
        foreach (var type in McpToolRegistry.ReadEntities)
        {
            var entity = modelBuilder.Model.FindEntityType(type);
            if (entity is null) continue;
            var mapped = entity.GetProperties().Select(p => p.Name).ToList();
            var granted = McpReadOnlyGrants.GrantedProperties(type, mapped);
            if (granted is null) continue;
            var keep = granted.ToHashSet(StringComparer.Ordinal);
            foreach (var name in mapped.Where(p => !keep.Contains(p)))
                modelBuilder.Entity(type).Ignore(name);
        }
        modelBuilder.HasAnnotation(Annotation, true);
    }

    /// <summary>Runs <see cref="Reduce"/> after the context's own <c>OnModelCreating</c>.</summary>
    public sealed class Customizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            Reduce(modelBuilder);
        }
    }

    /// <summary>The reduced model is cached apart from the application model of the same context type.</summary>
    public sealed class CacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), designTime, Annotation);
    }
}
