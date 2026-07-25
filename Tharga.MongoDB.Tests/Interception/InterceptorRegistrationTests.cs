using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Tharga.MongoDB.Interception;
using Tharga.MongoDB.Internals;
using Xunit;

namespace Tharga.MongoDB.Tests.Interception;

public class InterceptorRegistrationTests
{
    private static ServiceCollection BuildServices()
    {
        var services = new ServiceCollection().AddLogging();

        // AddMongoDB's firewall state service takes IHostEnvironment, which a bare ServiceCollection
        // has no reason to contain. Nothing to do with interception — just what the graph needs to
        // resolve IMongoDbServiceFactory outside a real host.
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(x => x.EnvironmentName).Returns("Production");
        services.AddSingleton(hostEnvironment.Object);

        return (ServiceCollection)services;
    }

    private static MongoDbServiceFactory ResolveFactory(IServiceCollection services)
    {
        return (MongoDbServiceFactory)services.BuildServiceProvider().GetRequiredService<IMongoDbServiceFactory>();
    }

    private static MongoDbServiceFactory BuildFactory(Action<Configuration.DatabaseOptions> configure)
    {
        var services = BuildServices();
        var configuration = new ConfigurationBuilder().Build();

        services.AddMongoDB(configuration, o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            configure(o);
        });

        return ResolveFactory(services);
    }

    [Fact]
    public void NoInterceptorsRegistered_FastPathFlagsAreFalse()
    {
        var factory = BuildFactory(_ => { });

        factory.Interceptors.Should().BeEmpty();
        factory.HasInvocationInterceptors.Should().BeFalse();
        factory.HasEnumerationInterceptors.Should().BeFalse();
    }

    [Fact]
    public void RegisteredByType_IsResolvedFromDi()
    {
        var factory = BuildFactory(o => o.AddCollectionInterceptor<RecordingInterceptor>());

        factory.Interceptors.Should().ContainSingle().Which.Should().BeOfType<RecordingInterceptor>();
        factory.HasInvocationInterceptors.Should().BeTrue();
        factory.HasEnumerationInterceptors.Should().BeFalse();
    }

    [Fact]
    public void RegisteredByInstance_IsUsedAsSupplied()
    {
        var instance = new RecordingInterceptor();

        var factory = BuildFactory(o => o.AddCollectionInterceptor(instance));

        factory.Interceptors.Should().ContainSingle().Which.Should().BeSameAs(instance);
    }

    [Fact]
    public void RegistrationOrderIsPreserved()
    {
        var first = new RecordingInterceptor();
        var second = new RecordingInterceptor();
        var third = new RecordingInterceptor();

        var factory = BuildFactory(o =>
        {
            o.AddCollectionInterceptor(first);
            o.AddCollectionInterceptor(second);
            o.AddCollectionInterceptor(third);
        });

        factory.Interceptors.Should().Equal(first, second, third);
    }

    [Fact]
    public void ConsumerRegisteredType_IsNotOverwritten()
    {
        // An interceptor with dependencies is registered by the consumer first; AddMongoDB must not
        // clobber that registration with a plain TryAddSingleton of the same type.
        var services = BuildServices();
        var configuration = new ConfigurationBuilder().Build();
        var preRegistered = new DependentInterceptor("configured");
        services.AddSingleton(preRegistered);

        services.AddMongoDB(configuration, o =>
        {
            o.AutoRegisterRepositories = false;
            o.AutoRegisterCollections = false;
            o.AddCollectionInterceptor<DependentInterceptor>();
        });

        var factory = ResolveFactory(services);

        factory.Interceptors.Should().ContainSingle().Which.Should().BeSameAs(preRegistered);
    }

    [Fact]
    public void EnumerationPointInterceptor_SetsOnlyTheEnumerationFlag()
    {
        var factory = BuildFactory(o => o.AddCollectionInterceptor(new EnumerationOnlyInterceptor()));

        factory.HasInvocationInterceptors.Should().BeFalse();
        factory.HasEnumerationInterceptors.Should().BeTrue();
    }

    [Fact]
    public void MixedPoints_SetBothFlags()
    {
        var factory = BuildFactory(o =>
        {
            o.AddCollectionInterceptor(new RecordingInterceptor());
            o.AddCollectionInterceptor(new EnumerationOnlyInterceptor());
        });

        factory.HasInvocationInterceptors.Should().BeTrue();
        factory.HasEnumerationInterceptors.Should().BeTrue();
    }

    [Fact]
    public void InterceptorsDoNotLeakBetweenContainers()
    {
        // This is the defect that rules out the existing static RepositoryCollectionBase.ActionEvent
        // for policy work: it is global to the process, so a second AddMongoDB in the same process
        // sees the first one's handlers. The DI-resolved chain must not behave that way — otherwise
        // two hosts in one process, or two tests in one run, contaminate each other.
        var firstInterceptor = new RecordingInterceptor();
        var secondInterceptor = new RecordingInterceptor();

        var firstFactory = BuildFactory(o => o.AddCollectionInterceptor(firstInterceptor));
        var secondFactory = BuildFactory(o => o.AddCollectionInterceptor(secondInterceptor));

        firstFactory.Interceptors.Should().ContainSingle().Which.Should().BeSameAs(firstInterceptor);
        secondFactory.Interceptors.Should().ContainSingle().Which.Should().BeSameAs(secondInterceptor);
    }

    [Fact]
    public void ContainerWithNoInterceptors_IsUnaffectedByAnotherContainerThatHasThem()
    {
        var withInterceptor = BuildFactory(o => o.AddCollectionInterceptor(new RecordingInterceptor()));
        var withoutInterceptor = BuildFactory(_ => { });

        withInterceptor.Interceptors.Should().ContainSingle();
        withoutInterceptor.Interceptors.Should().BeEmpty();
        withoutInterceptor.HasInvocationInterceptors.Should().BeFalse();
    }

    [Fact]
    public void AddCollectionInterceptor_NullInstance_Throws()
    {
        var options = new Configuration.DatabaseOptions();

        Action act = () => options.AddCollectionInterceptor(null);

        act.Should().Throw<ArgumentNullException>();
    }

    private class RecordingInterceptor : ICollectionInterceptor
    {
        public List<CollectionCallInfo> Calls { get; } = [];

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            Calls.Add(call);
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }

    private class EnumerationOnlyInterceptor : ICollectionInterceptor
    {
        public InterceptionPoint Points => InterceptionPoint.Enumeration;

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }

    private class DependentInterceptor : ICollectionInterceptor
    {
        public DependentInterceptor(string setting)
        {
            Setting = setting;
        }

        public string Setting { get; }

        public ValueTask<InterceptDecision> BeforeCallAsync(CollectionCallInfo call, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(InterceptDecision.Proceed);
        }
    }
}
