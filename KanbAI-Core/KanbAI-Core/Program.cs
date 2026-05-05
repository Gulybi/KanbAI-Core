using KanbAI_Core.Extensions;
using System.Text;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Services.Auth;
using KanbAI_Core.Services.Columns;
using KanbAI_Core.Services.Projects;
using KanbAI_Core.Services.Tasks;
using KanbAI_Core.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtSettings = new JwtSettings();
builder.Configuration.Bind("JwtSettings", jwtSettings);
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddTransient<ITokenService, TokenService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IColumnService, ColumnService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddSignalR();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services
    .AddAuthorization()
    .AddPersistence(builder.Configuration)
    .AddApiInfrastructure()
    .AddAuthServices()
    .AddCorsPolicy(builder.Configuration)
    .AddSignalRInfrastructure()
    .AddFileStorage(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    if (!app.Configuration.GetValue<bool>("Testing:SkipScalar"))
    {
        app.MapScalarUi();
    }
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<KanbanHub>("/hubs/kanban");

app.Run();

public partial class Program { }
