using System;
using Tharga.Communication.Server;

namespace Tharga.MongoDB.Monitor.Server;

/// <summary>
/// Configuration for <see cref="MonitorServerRegistration.AddMongoDbMonitorServer(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{MongoDbMonitorOptions})"/>.
/// Mirrors the parts of Tharga.Communication's <c>CommunicationOptions</c> that consumers actually
/// need to control — at registration time — without having to call <c>AddThargaCommunicationServer</c>
/// themselves.
/// </summary>
public sealed class MongoDbMonitorOptions
{
    /// <summary>
    /// API keys accepted by the default validator. Forwarded directly to
    /// <c>CommunicationOptions.ApiKeys</c>. When <c>null</c> or empty AND no custom validator
    /// is registered via <see cref="UseApiKeyValidator{TValidator}"/>, all connections are accepted
    /// (no auth). Ignored when a custom validator IS registered — the validator decides.
    /// </summary>
    public string[] ApiKeys { get; set; }

    /// <summary>
    /// Captured forwarder for any custom validator registration; invoked against the
    /// underlying <see cref="CommunicationOptions"/> when the monitor wires up the communication
    /// server. Internal so consumers can't bypass <see cref="UseApiKeyValidator{TValidator}"/>.
    /// </summary>
    internal Action<CommunicationOptions> ConfigureCommunicationOptions { get; private set; }

    /// <summary>
    /// Registers a custom <see cref="IApiKeyValidator"/> against the underlying communication server.
    /// Use this to plug your own key store, mixed-mode (unauth + keyed) policy, or per-key claim logic.
    /// </summary>
    /// <typeparam name="TValidator">Concrete validator type. Resolved from DI.</typeparam>
    public void UseApiKeyValidator<TValidator>() where TValidator : class, IApiKeyValidator
    {
        ConfigureCommunicationOptions = options => options.RegisterApiKeyValidator<TValidator>();
    }
}
