namespace GmcV2.Core;

/// <summary>Artifact metadata row (uw.QuoteArtifact, without the payload).</summary>
public sealed class QuoteArtifactInfo
{
    public long QuoteArtifactId { get; set; }
    public long QuoteVersionId { get; set; }
    public string ArtifactType { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string? GeneratedBy { get; set; }
}
