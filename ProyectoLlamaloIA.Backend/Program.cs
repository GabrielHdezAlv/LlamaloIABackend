using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ProyectoLlamaloIA.Backend.Repository;
using System.IO;
using Google.Cloud.Firestore;
using Microsoft.IdentityModel.Logging;

var builder = WebApplication.CreateBuilder(args);

// 🔐 Verificar si estamos en Render (por la variable de entorno)
var credentialsJson = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS_JSON");

string credentialPath;

if (!string.IsNullOrWhiteSpace(credentialsJson))
{
    // 👉 Estamos en Render u otro entorno cloud sin archivo físico
    credentialPath = Path.GetTempFileName();
    File.WriteAllText(credentialPath, credentialsJson);
}
else
{
    // 👉 Estamos en local
    credentialPath = Path.Combine(Directory.GetCurrentDirectory(), "llamaloio-firebase-adminsdk-fbsvc-2884e73711.json");

    if (!File.Exists(credentialPath))
    {
        throw new FileNotFoundException($"❌ Archivo de credenciales no encontrado en: {credentialPath}");
    }
}

// 🔥 Inicializar Firebase
if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions()
    {
        Credential = GoogleCredential.FromFile(credentialPath)
    });
}

// 🔹 Inicializar Firestore
Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialPath);
var firestoreDb = FirestoreDb.Create("llamaloio");
builder.Services.AddSingleton(firestoreDb); 

// 🔹 Habilitar CORS para permitir peticiones desde el frontend (Ejemplo: Angular, React)
var corsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy,
        policy =>
        {
            policy.WithOrigins("http://localhost:4200") // Permite solo el frontend local (cámbialo si es necesario)
                  .AllowAnyHeader() // Permitir cualquier header (Content-Type, Authorization, etc.)
                  .AllowAnyMethod() // Permitir métodos como GET, POST, PUT, DELETE
                  .AllowCredentials(); // Permitir credenciales (Tokens JWT)
        });
});

// 🔹 Configurar autenticación con Firebase JWT
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://securetoken.google.com/llamaloio";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://securetoken.google.com/llamaloio",
            ValidateAudience = true,
            ValidAudience = "llamaloio",
            ValidateLifetime = true
        };

        // 🔹 Habilitar logs detallados para debugging
        IdentityModelEventSource.ShowPII = true;

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"🔥 Error de autenticación: {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Console.WriteLine("✅ Token validado correctamente.");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// 🔹 Agregar Swagger con autenticación JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LlamaloIO API",
        Version = "v1",
        Description = "API para gestionar usuarios y tareas en Firestore con Firebase Auth"
    });

    // Configurar autenticación JWT en Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Ingrese el token de Firebase en el formato: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

// 🔹 Inyección de dependencias (Repositories)
builder.Services.AddSingleton<UserRepository>();
//builder.Services.AddSingleton<TaskRepository>(); // 🔹 Inyectar el repositorio de tareas

var app = builder.Build();

// 🔹 Habilitar Swagger en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Llamalo IO API v1");
        c.RoutePrefix = string.Empty;
    });
}

// app.UseHttpsRedirection();

// 🔹 Aplicar CORS antes de la autenticación
app.UseCors(corsPolicy);

// 🔹 Middleware de autenticación y autorización
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 🔧 Escuchar en el puerto definido por Render (variable de entorno PORT)
var port = Environment.GetEnvironmentVariable("PORT") ?? "7027";
app.Urls.Add($"http://*:{port}");

app.Run();