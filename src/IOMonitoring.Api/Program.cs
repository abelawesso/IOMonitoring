using System.Text;
using IOMonitoring.Api.Endpoints;
using IOMonitoring.Api.Infrastructure;
using IOMonitoring.Application;
using IOMonitoring.Infrastructure;
using IOMonitoring.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ----- Layers -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ----- Authentication (JWT bearer) -----
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
if (jwt.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = AppClaims.Username,
            RoleClaimType = AppClaims.Role
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanWrite, p => p.RequireRole("Admin", "Operator"))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole("Admin"));

// ----- Errors -----
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ----- Swagger -----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "IO Monitoring API", Version = "v1" });

    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Call POST /api/auth/login, then paste the token here.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", bearer);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { bearer, Array.Empty<string>() } });
});

var app = builder.Build();

// Fill the in-memory database with dummy IO Box test data.
await app.Services.SeedDatabaseAsync();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseWebAssemblyDebugging();
}

app.UseHttpsRedirection();

// Serve the Blazor WebAssembly client from this host (single URL for UI + API).
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapDeviceEndpoints();
app.MapTestRunEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
