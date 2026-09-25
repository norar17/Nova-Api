namespace ECommerce.Infrastructure.Persistence.Mongo;

/// <summary>
/// Bound from the "MongoDb" configuration section. Replaces the old
/// ConnectionStrings:DefaultConnection Postgres value.
/// </summary>
public class MongoOptions
{
    public const string SectionName = "MongoDb";

    /// <summary>Full connection string, e.g. "mongodb://localhost:27017" or an Atlas SRV URI.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Database name, e.g. "ecommerce_dev".</summary>
    public string DatabaseName { get; set; } = string.Empty;
}
