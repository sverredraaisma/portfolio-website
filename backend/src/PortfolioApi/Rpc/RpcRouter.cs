using System.Security.Claims;
using System.Text.Json;
using PortfolioApi.Rpc.Methods;
using PortfolioApi.Services;

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

    public bool IsAdmin => User.FindFirst("admin")?.Value == "true";

    public Guid RequireUserId() =>
        UserId ?? throw new AuthFailedException("Not signed in");

    public Guid RequireAdmin()
    {
        var id = RequireUserId();
        if (!IsAdmin) throw new AuthFailedException("Admin privileges required");
        return id;
    }

    /// Aborted when the client disconnects. Pass into EF calls so query work
    /// stops early instead of holding a DB connection until completion.
    public CancellationToken CancellationToken => Http.RequestAborted;
}

public class RpcRouter
{
    private readonly Dictionary<string, RpcHandler> _handlers = new(StringComparer.Ordinal);
    private readonly ILogger<RpcRouter> _log;

    public RpcRouter(AuthMethods auth, PostMethods posts, CommentMethods comments, SigningMethods signing, AccountMethods accounts, ILogger<RpcRouter> log)
    {
        _log = log;

        // Auth
        Register("auth.register", RpcHandlers.Typed<RegisterParams, RegisterResult>(auth.Register));
        Register("auth.login", RpcHandlers.Typed<LoginParams, AuthSuccess>(auth.Login));
        Register("auth.refresh", RpcHandlers.Typed<RefreshParams, AuthSuccess>(auth.Refresh));
        Register("auth.logout", RpcHandlers.Typed<LogoutParams, OkResult>(auth.Logout));
        Register("auth.verifyEmail", RpcHandlers.Typed<VerifyEmailParams, VerifyResult>(auth.VerifyEmail));
        Register("auth.requestPasswordReset", RpcHandlers.Typed<RequestPasswordResetParams, OkResult>(auth.RequestPasswordReset));
        Register("auth.resetPassword", RpcHandlers.Typed<ResetPasswordParams, OkResult>(auth.ResetPassword));
        Register("auth.resendVerification", RpcHandlers.Typed<ResendVerificationParams, OkResult>(auth.ResendVerification));
        Register("auth.changePassword", RpcHandlers.Typed<ChangePasswordParams, OkResult>(auth.ChangePassword));
        Register("auth.revokeAllSessions", RpcHandlers.Typed<OkResult>(auth.RevokeAllSessions));
        Register("auth.me", RpcHandlers.Typed<UserDto>(auth.Me));

        // Posts
        Register("posts.list", RpcHandlers.Typed<PostListParams, PaginatedResult<PostSummary>>(posts.List));
        Register("posts.get", RpcHandlers.Typed<GetPostParams, PostDetail>(posts.Get));
        Register("posts.create", RpcHandlers.Typed<CreatePostParams, CreatePostResult>(posts.Create));
        Register("posts.update", RpcHandlers.Typed<UpdatePostParams, OkResult>(posts.Update));
        Register("posts.delete", RpcHandlers.Typed<DeletePostParams, OkResult>(posts.Delete));
        Register("posts.uploadImage", RpcHandlers.Typed<UploadImageParams, ImageUploadResult>(posts.UploadImage));

        // Comments
        Register("comments.list", RpcHandlers.Typed<ListCommentsParams, PaginatedResult<CommentDto>>(comments.List));
        Register("comments.create", RpcHandlers.Typed<CreateCommentParams, CommentDto>(comments.Create));
        Register("comments.delete", RpcHandlers.Typed<DeleteCommentParams, OkResult>(comments.Delete));

        // Signing (Falcon-512 PQC)
        Register("signing.publicKey", RpcHandlers.Typed<PublicKeyDto>(signing.PublicKey));
        Register("signing.sign", RpcHandlers.Typed<SignStatementParams, SignedStatement>(signing.Sign));
        Register("signing.verify", RpcHandlers.Typed<VerifyStatementParams, VerifyResultDto>(signing.Verify));

        // Account (AVG / GDPR — data export and right-to-erasure)
        Register("account.export", RpcHandlers.Typed<AccountExport>(accounts.Export));
        Register("account.delete", RpcHandlers.Typed<DeleteAccountParams, OkResult>(accounts.Delete));
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
        catch (AuthFailedException ex)
        {
            // The detail is for the log; the client gets a uniform message so an
            // attacker can't tell whether the user, password, token, or scope failed.
            _log.LogInformation("auth failure on {Method}: {Reason}", req.Method, ex.Message);
            await Write(http, 401, new RpcResponse(Error: new RpcError("unauthorized", "Not authorized")));
        }
        catch (InvalidOperationException ex)
        {
            // Client-correctable: bad params, validation failure, etc.
            await Write(http, 400, new RpcResponse(Error: new RpcError("invalid", ex.Message)));
        }
        catch (Exception ex)
        {
            // Never leak ex.Message — it can carry stack traces, SQL fragments,
            // or PII. Log it server-side, return a generic message to the caller.
            _log.LogError(ex, "Unhandled error in RPC method {Method}", req.Method);
            await Write(http, 500, new RpcResponse(Error: new RpcError("internal", "Internal server error")));
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
