using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Tharga.MongoDB;

namespace HostSample;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddSwaggerGen(c => { c.SwaggerDoc("v1", new OpenApiInfo { Title = "Tharga.MongoDB.HostSample", Version = "v1" }); });

        services.AddMongoDB(o =>
        {
            o.ConfigurationName = "Default"; //"Other";
            //o.ConnectionStringLoader = (_, _) => Task.FromResult<ConnectionString>("mongodb://localhost:27017/Tharga{environment}_HostSample{part}");
            o.ActionEvent += e => { System.Console.WriteLine($"---> {e.Action.Message}"); };
        });
        services.AddLogging(x =>
        {
            x.AddConsole();
            x.SetMinimumLevel(LogLevel.Trace);
        });

        services.AddTransient<IMongoUrlBuilder, MyMongoUrlBuilder>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tharga.MongoDB.HostSample v1"));
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });

        app.UseMongoDB();
    }
}