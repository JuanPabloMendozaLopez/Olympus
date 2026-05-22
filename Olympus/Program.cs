using Olympus.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Servicios de la app
builder.Services.AddSingleton<SolarIndexService>();
builder.Services.AddScoped<SolarService>();
builder.Services.AddScoped<AiService>();

// HttpClient (necesario para SolarService y AiService)
builder.Services.AddHttpClient();

// CORS para frontend y móvil
builder.Services.AddCors(options =>
{

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin();
        policy.AllowAnyMethod();
        policy.AllowAnyHeader();
    });

});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowAll");

// Solo redirigir a HTTPS en producción; en desarrollo la API escucha en HTTP
// para que compañeros en la red local puedan conectarse sin certificado.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
