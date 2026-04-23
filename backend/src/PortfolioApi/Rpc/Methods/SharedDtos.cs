namespace PortfolioApi.Rpc.Methods;

/// Plain success acknowledgement — used by mutating endpoints that don't have
/// anything more interesting to return.
public sealed record OkResult(bool Ok = true);

/// Generic page wrapper. HasMore is computed by fetching pageSize+1 rows; the
/// caller doesn't need a separate count query.
public sealed record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, bool HasMore);
