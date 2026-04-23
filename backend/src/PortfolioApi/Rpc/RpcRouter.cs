using System.Security.Claims;
using System.Text.Json;
using PortfolioApi.Rpc.Methods;

namespace PortfolioApi.Rpc;

public record RpcRequest(string Method, JsonElement? Params);
public record RpcError(string Code, string Message);
public record RpcResponse(object? Result = null, RpcError? Error = null);

public delegate Task<object?> RpcHandler(JsonElement? @params, RpcContext ctx);

public class RpcContext
{
    public required HttpContext Http { get; init; }
    public ClaimsPrincipal User => Http.User;

    public Guid? UserId =>
        Guid.TryParse(User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id : null;

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAccessException("Not signed in");
}

public class RpcRouter
{
    private readonly Dictionary<string, RpcHandler> _handlers = new(StringComparer.Ordinal);

    public RpcRouter(AuthMethods auth, PostMethods posts, CommentMethods comments)
    {
        // Auth
        Register("auth.register", auth.Register);
        Register("auth.login", auth.Login);
        Register("auth.verifyEmail", auth.VerifyEmail);
        Register("auth.me", auth.Me);

        // Posts
        Register("posts.list", posts.List);
        Register("posts.get", posts.Get);
        Register("posts.create", posts.Create);
        Register("posts.update", posts.Update);
        Register("posts.delete", posts.Delete);
        Register("posts.uploadImage", posts.UploadImage);

        // Comments
        Register("comments.list", comments.List);
        Register("comments.create", comments.Create);
        Register("comments.delete", comments.Delete);
    }

    private void Register(string method, RpcHandler handler) => _handlers[method] = handler;

    public async Task HandleAsync(HttpContext http)
    {
        RpcRequest? req;
        try
        {
            req = await JsonSerializer.DeserializeAsync<RpcRequest>(http.Request.Body, JsonOpts);
        }
        catch
        {
            await Write(http, 400, new RpcResponse(Error: new RpcError("bad_request", "Invalid JSON")));
            return;
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Method))
        {
            await Write(http, 400, new RpcResponse(Error: new RpcError("bad_request", "method required")));
            return;
        }

        if (!_handlers.TryGetValue(req.Method, out var handler))
        {
            await Write(http, 404, new RpcResponse(Error: new RpcError("not_found", $"Unknown method '{req.Method}'")));
            return;
        }

        try
        {
            var result = await handler(req.Params, new RpcContext { Http = http });
            await Write(http, 200, new RpcResponse(Result: result));
        }
        catch (UnauthorizedAccessException ex)
        {
            await Write(http, 401, new RpcResponse(Error: new RpcError("unauthorized", ex.Message)));
        }
        catch (InvalidOperationException ex)
        {
            await Write(http, 400, new RpcResponse(Error: new RpcError("invalid", ex.Message)));
        }
        catch (Exception ex)
        {
            await Write(http, 500, new RpcResponse(Error: new RpcError("internal", ex.Message)));
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task Write(HttpContext http, int status, RpcResponse body)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(http.Response.Body, body, JsonOpts);
    }
}
