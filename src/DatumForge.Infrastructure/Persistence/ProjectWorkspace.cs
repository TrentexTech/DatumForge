using DatumForge.Core.Models;

namespace DatumForge.Infrastructure.Persistence;

public sealed record ProjectWorkspace(string ProjectFilePath, ProjectDocument Project, IReadOnlyDictionary<Guid, DrawingDocument> Drawings);
