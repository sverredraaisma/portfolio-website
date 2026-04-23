using System.Text.Json;

namespace PortfolioApi.Rpc;

/// Adapters that lift strongly-typed handler delegates into the loosely-typed
/// RpcHandler delegate that RpcRouter registers. The adapters handle JSON
/// deserialisation and translate JsonException into the same
/// InvalidOperationException the router maps to a clean 400 — keeps the
/// handler bodies from having to repeat the try/catch pattern.
public static class RpcHandlers
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// Adapter for a handler that takes typed params + RpcContext.
    /// Throws InvalidOperationException("params required") when the JsonElement
    /// is missing or null. JsonException (e.g. missing required property) is
    /// rethrown as InvalidOperationException so the router maps it to 400.
    public static RpcHandler Typed<TParams, TResult>(Func<TParams, RpcContext, Task<TResult>> impl)
        where TParams : class
    {
        return async (raw, ctx) =>
        {
            if (raw is null || raw.Value.ValueKind == JsonValueKind.Null || raw.Value.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("params required");

            TParams parsed;
            try
            {
                parsed = raw.Value.Deserialize<TParams>(JsonOpts)
                    ?? throw new InvalidOperationException("params required");
            }
            catch (JsonException jsonEx)
            {
                // Covers missing-required-property and shape-mismatch errors.
                // Surface as a 400 rather than the generic 500 the router would
                // otherwise emit on JsonException.
                throw new InvalidOperationException(jsonEx.Message);
            }

            var result = await impl(parsed, ctx);
            return (object?)result;
        };
    }

    /// Adapter for a handler that needs only RpcContext (no params payload).
    public static RpcHandler Typed<TResult>(Func<RpcContext, Task<TResult>> impl)
    {
        return async (_, ctx) =>
        {
            var result = await impl(ctx);
            return (object?)result;
        };
    }
}
