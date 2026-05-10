namespace HybridCodebaseIndex.Core;

public sealed record DocDraftResponse(
    int IndexFormatVersion,
    string DatabasePath,
    string Title,
    string Markdown,
    string? Err);

