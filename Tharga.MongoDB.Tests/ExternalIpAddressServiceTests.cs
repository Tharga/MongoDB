using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tharga.MongoDB.Atlas;
using Xunit;

namespace Tharga.MongoDB.Tests;

/// <summary>
/// Coverage for GitHub issue #201. A lookup endpoint answering with a body that is not an IP address made IPAddress.Parse throw,
/// which ended the loop before the remaining endpoints were tried.
/// </summary>
public class ExternalIpAddressServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _bodies;

        public StubHandler(params string[] bodies)
        {
            _bodies = new Queue<string>(bodies);
        }

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_bodies.Dequeue()) });
        }
    }

    private static ExternalIpAddressService Build(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        return new ExternalIpAddressService(factory.Object, NullLogger<ExternalIpAddressService>.Instance);
    }

    [Fact]
    public async Task NonIpBodyFromFirstUri_ContinuesWithNextUri()
    {
        var handler = new StubHandler("<html>Sign in to the network</html>", "52.1.2.3\n");
        var sut = Build(handler);

        var result = await sut.GetExternalIpAddressAsync();

        result.Should().Be(IPAddress.Parse("52.1.2.3"));
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task NoUriAnswersWithAnIp_ReturnsNull()
    {
        var handler = new StubHandler("nope", "still nope", "<html/>");
        var sut = Build(handler);

        var result = await sut.GetExternalIpAddressAsync();

        result.Should().BeNull();
        handler.Calls.Should().Be(3);
    }
}
