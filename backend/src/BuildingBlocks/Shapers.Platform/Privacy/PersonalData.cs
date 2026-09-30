namespace Shapers.Platform.Privacy;

/// <summary>
/// Every module that stores personal information implements this, so a person's data can be exported and erased
/// across the whole platform (POPIA sections 23 and 24). The Privacy module calls every registered source.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>A heading for the export, e.g. "Prayer requests".</summary>
    string Name { get; }

    /// <summary>
    /// Everything this module holds about the person, as plain serialisable data. Null when there is nothing.
    /// Exclude other people's details (e.g. who else booked).
    /// </summary>
    Task<object?> ExportAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the person's data, or anonymises it where a record must be kept (e.g. attendance counts, giving
    /// records for SARS). Returns how many records were affected. Must be safe to run twice.
    /// </summary>
    Task<int> EraseAsync(Guid personId, CancellationToken cancellationToken);
}
