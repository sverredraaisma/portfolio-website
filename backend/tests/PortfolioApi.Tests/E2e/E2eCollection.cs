namespace PortfolioApi.Tests.E2e;

/// AppFactory mutates per-process env vars (Jwt__Key, Signing__KeyPath,
/// Image__MediaPath) in its ctor. xUnit runs different test classes in
/// parallel by default, so two factory instances stomp each other.
/// Grouping the e2e classes under a single Collection makes xUnit run
/// them serially. Each class still gets its own AppFactory (via
/// IClassFixture), but only one factory is alive at a time.
[CollectionDefinition("e2e", DisableParallelization = true)]
public sealed class E2eCollection { }
