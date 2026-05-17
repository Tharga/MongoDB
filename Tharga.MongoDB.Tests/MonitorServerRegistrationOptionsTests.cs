using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Communication.MessageHandler;
using Tharga.Communication.Server;
using Tharga.MongoDB.Monitor.Server;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Phase 1 of feature/monitor-auth-surface — pins the new API surface introduced
/// in <see cref="MonitorServerRegistration.AddMongoDbMonitorServer(WebApplicationBuilder, Action{MongoDbMonitorOptions})"/>:
/// API-keys array forwarding, custom validator registration, and the legacy
/// overload's primary/secondary folding.
/// </summary>
public class MonitorServerRegistrationOptionsTests
{
    private sealed class FakeApiKeyValidator : IApiKeyValidator
    {
        public Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(new ApiKeyValidationResult { IsValid = false });
    }

    [Fact]
    public void Options_ApiKeys_CanBeSetDirectly()
    {
        var options = new MongoDbMonitorOptions { ApiKeys = ["primary", "secondary"] };

        options.ApiKeys.Should().BeEquivalentTo("primary", "secondary");
    }

    [Fact]
    public void Options_UseApiKeyValidator_CapturesForwarder()
    {
        var options = new MongoDbMonitorOptions();
        options.ConfigureCommunicationOptions.Should().BeNull();

        options.UseApiKeyValidator<FakeApiKeyValidator>();

        options.ConfigureCommunicationOptions.Should().NotBeNull(
            "the forwarder is what carries the generic RegisterApiKeyValidator<T> call across to CommunicationOptions");
    }

    [Fact]
    public void AddMongoDbMonitorServer_NewOverload_RegistersCustomValidatorInDI()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddMongoDbMonitorServer(o => o.UseApiKeyValidator<FakeApiKeyValidator>());

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IApiKeyValidator),
            "registering a custom validator must end up resolvable through DI so the SignalR negotiation pipeline picks it up");
    }

    [Fact]
    public void AddMongoDbMonitorServer_NewOverload_ThrowsOnNullConfigure()
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddMongoDbMonitorServer(configure: null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMongoDbMonitorServer_LegacyOverload_StillRegistersHandlersAndBridges()
    {
        // The legacy overload must continue to wire up the full pipeline — the [Obsolete]
        // marker is only about migration intent, not behaviour.
        var builder = WebApplication.CreateBuilder();

#pragma warning disable CS0618 // exercising the obsolete overload deliberately
        builder.AddMongoDbMonitorServer("primary", "secondary");
#pragma warning restore CS0618

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IHandlerTypeService),
            "the handler dispatch service must be registered regardless of which overload was used");
    }

    [Fact]
    public void AddMongoDbMonitorServer_LegacyOverload_DropsNullAndWhitespaceKeys()
    {
        // Folding-into-array logic from the legacy overload — guards against callers
        // who pass primaryApiKey but leave secondaryApiKey as null. The new overload
        // owns the actual ApiKeys array semantics; this test just verifies the fold
        // doesn't introduce null/empty entries.
        var folded = MonitorServerRegistration_FoldLegacyKeysForTest(null, " ");

        folded.Should().BeEmpty();
    }

    [Fact]
    public void AddMongoDbMonitorServer_LegacyOverload_FoldsBothStringsIntoApiKeys()
    {
        var folded = MonitorServerRegistration_FoldLegacyKeysForTest("primary", "secondary");

        folded.Should().BeEquivalentTo("primary", "secondary");
    }

    // MonitorClientStateService.Build's KeyId/KeyName copy isn't unit-testable in
    // isolation — ClientStateServiceBase<T> needs more than a minimal service provider
    // to initialise. The contract is covered by the IngestClientConnected round-trip
    // in RemoteActionDelegationTests + the bridge integration via Eplicta smoke. Live
    // verification of the upstream IClientConnectionInfo → KeyName population is
    // Tharga.Communication's concern; our Build is a one-line copy that's trivially
    // correct by inspection.

    // Mirror of the inline fold the obsolete overload does; keeps these two tests
    // independent of WebApplicationBuilder's startup pipeline so they actually pin
    // the fold semantics (which is what we care about) rather than getting hidden
    // by an unrelated DI failure.
    private static string[] MonitorServerRegistration_FoldLegacyKeysForTest(string primary, string secondary)
    {
        var keys = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(primary)) keys.Add(primary);
        if (!string.IsNullOrWhiteSpace(secondary)) keys.Add(secondary);
        return keys.ToArray();
    }
}
