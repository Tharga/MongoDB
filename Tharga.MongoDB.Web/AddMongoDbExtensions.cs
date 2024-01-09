using Tharga.MongoDB.Configuration;

namespace Tharga.MongoDB.Web;

public static class AddMongoDbExtensions
{
    public static void UseMongoDB(this IApplicationBuilder applicationBuilder, Action<UseMongoOptions> options = null)
    {
        applicationBuilder.ApplicationServices.UseMongoDB(options);
    }
}