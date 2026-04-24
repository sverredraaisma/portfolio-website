using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApi.Rpc;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Rpc;

/// RpcRouter is the public face of every backend operation — every status
/// code, every error shape, every "is this method known" decision flows
/// through here. These tests exercise the dispatcher by reflectively
/// constructing a router with a hand-rolled handler set, sidestepping the
/// real method classes and their dependency graph.
public class RpcRouterTests
{
    private static RpcRouter NewRouter(Action<RpcRouter> register)
    {
        // The real ctor wants the full DI graph (Auth/Post/Comment/Signing/
        // Account methods). For dispatcher-only tests we don't care about
        // those, so reflect-construct an empty instance and use the private
        // Register helper to wire test handlers.
        var router = (RpcRouter)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(RpcRouter));
        // Set the readonly _log + _handlers via reflection.
        typeof(RpcRouter).GetField("_log",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(router, NullLogger<RpcRouter>.Instance);
        typeof(RpcRouter).GetField("_handlers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(router, new Dictionary<string, RpcHandler>(StringComparer.Ordinal));
        register(router);
        return router;
    }

    private static void AddHandler(RpcRouter router, string method, RpcHandler handler)
    {
        var dict = (Dictionary<string, RpcHandler>)typeof(RpcRouter)
            .GetField("_handlers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(router)!;
        dict[method] = handler;
    }

    private static async Task<(int Status, JsonDocument Body)> Invoke(RpcRouter router, string body)
    {
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Response.Body = new MemoryStream();

        await router.HandleAsync(http);

        http.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, json);
    }

    [Fact]
    public async Task Returns_404_with_an_error_envelope_for_an_unknown_method()
    {
        var router = NewRouter(_ => { });

        var (status, body) = await Invoke(router, """{"method":"does.not.exist"}""");

        status.Should().Be((int)HttpStatusCode.NotFound);
        body.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task Returns_400_when_the_body_is_not_valid_JSON()
    {
        var router = NewRouter(_ => { });

        var (status, body) = await Invoke(router, "{this is not json");

        status.Should().Be((int)HttpStatusCode.BadRequest);
        body.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("bad_request");
    }

    [Fact]
    public async Task Returns_400_when_the_method_is_missing_or_blank()
    {
        var router = NewRouter(_ => { });

        var (status, body) = await Invoke(router, """{"method":""}""");

        status.Should().Be((int)HttpStatusCode.BadRequest);
        body.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("method required");
    }

    [Fact]
    public async Task Returns_200_with_a_result_envelope_on_success()
    {
        var router = NewRouter(r =>
            AddHandler(r, "echo", (_, _) => Task.FromResult<object?>(new { said = "hi" })));

        var (status, body) = await Invoke(router, """{"method":"echo"}""");

        status.Should().Be((int)HttpStatusCode.OK);
        body.RootElement.GetProperty("result").GetProperty("said").GetString().Should().Be("hi");
    }

    [Fact]
    public async Task Maps_AuthFailedException_to_a_uniform_401_unauthorized_error()
    {
        var router = NewRouter(r =>
            AddHandler(r, "boom", (_, _) => throw new AuthFailedException("any reason at all")));

        var (status, body) = await Invoke(router, """{"method":"boom"}""");

        status.Should().Be((int)HttpStatusCode.Unauthorized);
        var err = body.RootElement.GetProperty("error");
        err.GetProperty("code").GetString().Should().Be("unauthorized");
        // The wire-level message is generic — the specific reason stays in
        // server logs so an attacker can't tell which guard fired.
        err.GetProperty("message").GetString().Should().Be("Not authorized");
    }

    [Fact]
    public async Task Maps_InvalidOperationException_to_a_400_with_the_thrown_message()
    {
        var router = NewRouter(r =>
            AddHandler(r, "validate", (_, _) => throw new InvalidOperationException("body too long")));

        var (status, body) = await Invoke(router, """{"method":"validate"}""");

        status.Should().Be((int)HttpStatusCode.BadRequest);
        var err = body.RootElement.GetProperty("error");
        err.GetProperty("code").GetString().Should().Be("invalid");
        err.GetProperty("message").GetString().Should().Be("body too long");
    }

    [Fact]
    public async Task Unhandled_exceptions_become_500_with_a_generic_message_so_internals_dont_leak()
    {
        var router = NewRouter(r =>
            AddHandler(r, "crash", (_, _) => throw new InvalidCastException("internal type X expected Y at line 42")));

        var (status, body) = await Invoke(router, """{"method":"crash"}""");

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        var err = body.RootElement.GetProperty("error");
        err.GetProperty("code").GetString().Should().Be("internal");
        err.GetProperty("message").GetString().Should().Be("Internal server error");
    }
}
