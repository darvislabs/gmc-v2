namespace GmcV2.Core;

/// <summary>Input row as received from a TPA workbook (ing.RawRow). Structure, never interpreted.</summary>
public sealed class RawRow
{
    public long RawRowId { get; set; }
    public long DocumentId { get; set; }
    public int RowNumber { get; set; }
    public string SheetName { get; set; } = "";
    public string[] Cells { get; set; } = Array.Empty<string>();
    public string RowHash { get; set; } = "";
}

/// <summary>Profiled source column (ing.SourceColumn) with suggestions (ing.ColumnMapping).</summary>
public sealed class SourceColumn
{
    public long SourceColumnId { get; set; }
    public long DocumentId { get; set; }
    public string Header { get; set; } = "";
    public int ColumnIndex { get; set; }
    public int NonEmptyCount { get; set; }
    public int DistinctCount { get; set; }
    public string DetectedType { get; set; } = "Text";   // Text | Number | Date
    public string[] SampleValues { get; set; } = Array.Empty<string>();

    // mapping suggestion
    public string? TargetParameter { get; set; }
    public decimal Confidence { get; set; }
    public string? ProvenanceTier { get; set; }
    public string? Reason { get; set; }
    public string[] Alternatives { get; set; } = Array.Empty<string>();
    public bool IsRequired { get; set; }
}

/// <summary>Append-only mapping decision (ing.ColumnMapping row).</summary>
public sealed class ColumnMapping
{
    public long MappingId { get; set; }
    public long SourceColumnId { get; set; }
    public string TargetParameter { get; set; } = "";
    public decimal Confidence { get; set; }
    public string ProvenanceTier { get; set; } = "";
    public bool AutoApproved { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>DQ rule outcome (ing.DataQualityResult).</summary>
public sealed class DataQualityResult
{
    public string RuleCode { get; set; } = "";   // DQ-CLAIM-LINKAGE, DQ-REQUIRED-FIELDS, DQ-DATE-VALID, DQ-AMOUNT-RECONCILE, DQ-DUPLICATES
    public bool Passed { get; set; }
    public int AffectedRows { get; set; }
    public decimal ScorePct { get; set; }
    public string? SampleOffendingJson { get; set; }
}

/// <summary>Learned alias (kb.MasterColumnSynonym).</summary>
public sealed class ColumnSynonym
{
    public long SynonymId { get; set; }
    public string SynonymText { get; set; } = "";
    public string TargetParameter { get; set; } = "";
    public string? ScopedInsurerTpa { get; set; }
    public int UseCount { get; set; }
    public bool LearnedFromApproval { get; set; }
    public string? LearnedFromCase { get; set; }
}

/// <summary>Known diagnosis taxonomy row + alias (kb.DiseaseTaxonomy / kb.DiseaseAlias).</summary>
public sealed class DiseaseEntry
{
    public long DiseaseId { get; set; }
    public string DiseaseName { get; set; } = "";
    public string DiseaseGroup { get; set; } = "";
    public string? IcdLevel1 { get; set; }
    public string? IcdLevel2 { get; set; }
    public string[] MatchKeywords { get; set; } = Array.Empty<string>();
    public bool IsChronic { get; set; }
}

/// <summary>Golden dataset entry (kb.GoldenEntry): human-confirmed text → ICD coding.</summary>
public sealed class GoldenEntry
{
    public long GoldenEntryId { get; set; }
    public string GoldenVersion { get; set; } = "GD-v1.0";
    public string RawTextNorm { get; set; } = "";
    public string IcdLevel1 { get; set; } = "";
    public string? IcdLevel2 { get; set; }
    public int UseCount { get; set; }
    public bool WasCorrected { get; set; }
    public string Source { get; set; } = "Migration";  // Migration | ReviewCorrection
}
