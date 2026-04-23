namespace PortfolioApi.Rpc.Methods;

/// Plain success acknowledgement — used by mutating endpoints that don't have
/// anything more interesting to return.
public sealed record OkResult(bool Ok = true);
