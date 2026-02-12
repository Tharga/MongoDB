using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.AddMongoDB(o =>
{
    o.AssureIndex = AssureIndexMode.BySchema;
    o.DefaultConfigurationName = "NoDefault";
    o.Limiter.MaxConcurrent = 1;
    o.Monitor.SlowCallsToKeep = 1;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.UseMongoDB();

app.Run();
