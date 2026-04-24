using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PortfolioApi.Rpc;

namespace PortfolioApi.Tests.Rpc;

/// RpcHandlers.Typed adapts strongly-typed handler delegates into the
/// loose-typed RpcHandler signature. The translation rules are small but
/// load-bearing — every method on the public RPC surface flows through here.
public class TypedHandlerTests
{
    private sealed record FooParams { public required string Bar { get; init; } public int Baz { get; init; } = 0; }
    private sealed record FooResult(string Echo);

    private static RpcContext Ctx() => new() { Http = new DefaultHttpContext() };

    [Fact]
    public async Task Typed_with_params_invokes_the_handler_with_a_deserialised_record()
    {
        var handler = RpcHandlers.Typed<FooParams, FooResult>((p, _) =>
            Task.FromResult(new FooResult(p.Bar + ":" + p.Baz)));

        var json = JsonDocument.Parse("""{"bar":"hi","baz":7}""").RootElement;
        var result = (FooResult)(await handler(json, Ctx()))!;

        result.Echo.Should().Be("hi:7");
    }

    [Fact]
    public async Task Typed_treats_missing_params_as_empty_object_and_validates_required_fields()
    {
        // Bar is required → missing-property error, not "params required".
        // The previous behaviour (immediate "params required") rejected
        // perfectly-valid calls to methods whose every property had a
        // default (e.g. posts.list with no params).
        var handler = RpcHandlers.Typed<FooParams, FooResult>((_, _) => throw new Exception("should not run"));

        var act = async () => await handler(null, Ctx());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Bar*");
    }

    [Fact]
    public async Task Typed_with_all_optional_params_succeeds_when_called_with_no_payload()
    {
        var handler = RpcHandlers.Typed<AllOptionalParams, FooResult>((_, _) =>
            Task.FromResult(new FooResult("ran")));

        var result = (FooResult)(await handler(null, Ctx()))!;

        result.Echo.Should().Be("ran");
    }

    private sealed record AllOptionalParams { public int Page { get; init; } = 1; }

    [Fact]
    public async Task Typed_throws_InvalidOperation_when_a_required_property_is_missing()
    {
        var handler = RpcHandlers.Typed<FooParams, FooResult>((_, _) => throw new Exception("should not run"));

        // "bar" is required.
        var json = JsonDocument.Parse("""{"baz":1}""").RootElement;
        var act = async () => await handler(json, Ctx());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "missing required property must surface as a validation error rather than a 500");
    }

    [Fact]
    public async Task Typed_throws_InvalidOperation_when_the_payload_isnt_an_object()
    {
        var handler = RpcHandlers.Typed<FooParams, FooResult>((_, _) => throw new Exception("should not run"));

        var json = JsonDocument.Parse("\"not an object\"").RootElement;
        var act = async () => await handler(json, Ctx());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Typed_no_params_overload_calls_the_handler_with_just_the_context()
    {
        var handler = RpcHandlers.Typed<FooResult>(_ => Task.FromResult(new FooResult("ok")));

        var result = (FooResult)(await handler(null, Ctx()))!;

        result.Echo.Should().Be("ok");
    }
}
