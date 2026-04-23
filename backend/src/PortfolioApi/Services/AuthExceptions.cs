namespace PortfolioApi.Services;

/// Thrown when authentication or authorization fails. The RPC layer maps this
/// to a 401 with a generic message — the inner Message is for the server log,
/// not the client.
public class AuthFailedException : Exception
{
    public AuthFailedException(string message) : base(message) { }
}
