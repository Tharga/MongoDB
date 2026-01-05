using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMongoDB(o =>
{
    o.AssureIndex = AssureIndexMode.BySchema;
    o.DefaultConfigurationName = "NoDefault";
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
