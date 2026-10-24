namespace DatumForge.Core.Models;

public sealed record HistoryEntry(string Scope, Guid DocumentId, int Revision, DateTimeOffset CreatedAtUtc, string RelativePath);
