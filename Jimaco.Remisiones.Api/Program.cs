using System.Text;
using Jimaco.Remisiones.Api;
using System.Text.Json.Serialization;
using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio;
using Jimaco.Remisiones.Negocio.Documentos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddNegocio();
builder.Services.Configure<Jimaco.Remisiones.Negocio.Servicios.WorldOfficeOptions>(builder.Configuration.GetSection(Jimaco.Remisiones.Negocio.Servicios.WorldOfficeOptions.SeccionConfiguracion));
builder.Services.Configure<DatosEmpresaOptions>(builder.Configuration.GetSection(DatosEmpresaOptions.SeccionConfiguracion));

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Falta configurar Jwt:Secret.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Jimaco.Remisiones";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Jimaco.Remisiones";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Jimaco Remisiones API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingresar el token JWT con el prefijo 'Bearer '."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedAdminAsync(db);
    if (builder.Configuration.GetValue<bool>("Seed:DatosDemo"))
        await DatosDemo.SembrarAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static async Task SeedAdminAsync(AppDbContext db)
{
    if (await db.Usuarios.AnyAsync())
        return;

    db.Usuarios.Add(new Usuario
    {
        Nombre = "Administrador",
        Email = "admin@jimaco.local",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
        Rol = RolUsuario.Admin,
        Activo = true
    });

    await db.SaveChangesAsync();
}
