namespace Pam.Shared.Pagination;

public record PaginatedResult<TEntity>(
    int PageIndex,
    int PageSize,
    long Count,
    IEnumerable<TEntity> Data);
