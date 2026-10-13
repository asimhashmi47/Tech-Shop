namespace TechShop.Domain.Common;

/// <summary>Base type for every persisted entity. All dates are stored in UTC.</summary>
public abstract class AuditableEntity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
