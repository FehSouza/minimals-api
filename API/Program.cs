using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using minimals_api.Domain.DTOs;
using minimals_api.Domain.Entities;
using minimals_api.Domain.Interfaces;
using minimals_api.Domain.ModelViews;
using minimals_api.Domain.Services;
using minimals_api.Infrastructure.Db;

#region Builder
var builder = WebApplication.CreateBuilder(args);

// Key para autenticação
var key = builder.Configuration.GetSection("Jwt").ToString();
if (string.IsNullOrEmpty(key)) key = "123456";

// Configuração para autenticação
builder.Services.AddAuthentication(option =>
{
  option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
  option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

}).AddJwtBearer(option =>
{
  option.TokenValidationParameters = new TokenValidationParameters
  {
    ValidateLifetime = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
    ValidateIssuer = false,
    ValidateAudience = false
  };
});

// Configuração para autorização
builder.Services.AddAuthorization();

builder.Services.AddScoped<IAdministratorService, AdministratorService>();
builder.Services.AddScoped<IVehicleService, VehicleService>();

// Configuração do Swagger
builder.Services.AddEndpointsApiExplorer();

// Configuração do Swagger com Authorization
builder.Services.AddSwaggerGen(options =>
{
  options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
  {
    Name = "Authorization",
    Type = SecuritySchemeType.Http,
    Scheme = "bearer",
    BearerFormat = "JWT",
    In = ParameterLocation.Header,
    Description = "Insira o token JKT aqui"
  });

  options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
    Array.Empty<string>()
   }
  });
});

// Adiciona a configuração para a conexão da DBContext (no caso aqui MinimalsContext) com o Banco de Dados
builder.Services.AddDbContext<MinimalsContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("connectionSqlServer")));

var app = builder.Build();
#endregion

#region Home
// Instância da ModelView da Home
app.MapGet("/", () => Results.Json(new Home())).AllowAnonymous().WithTags("Home");
#endregion

#region Administrators
string GenerateTokenJwt(Administrator administrator)
{
  if (string.IsNullOrEmpty(key)) return string.Empty;

  var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
  var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

  var claims = new List<Claim>()
  {
    new("Email", administrator.Email),
    new("Perfil", administrator.Profile),
    new(ClaimTypes.Role, administrator.Profile)
  };

  var token = new JwtSecurityToken(
    claims: claims,
    expires: DateTime.Now.AddDays(1),
    signingCredentials: credentials
  );

  return new JwtSecurityTokenHandler().WriteToken(token);
}

static ErrorAdministrator ErrorAdministratorValidation(AdministratorDTO administratorDTO)
{
  var error = new ErrorAdministrator { Messages = [] };

  if (administratorDTO.Email.IsNullOrEmpty()) error.Messages.Add("O e-mail não pode ser um dado vazio.");
  if (!administratorDTO.Email.Contains('@')) error.Messages.Add("Digite um e-mail válido.");
  if (administratorDTO.Password.IsNullOrEmpty()) error.Messages.Add("A senha não pode ser um dado vazio.");
  if (administratorDTO.Profile.ToString() != "Admin" && administratorDTO.Profile.ToString() != "Editor")
    error.Messages.Add("O perfil não pode ser um dado vazio. Escolha entre Admin (0) ou Editor (1)");

  return error;
}

app.MapPost("/administrators/login", ([FromBody] LoginDTO loginDTO, IAdministratorService administratorService) =>
{
  var administrator = administratorService.Login(loginDTO);

  if (administrator != null)
  {
    string token = GenerateTokenJwt(administrator);

    return Results.Ok(new AdministratorLogged
    {
      Email = administrator.Email,
      Profile = administrator.Profile,
      Token = token
    });
  };
  return Results.Unauthorized();
})
.AllowAnonymous()
.WithTags("Administrators");

app.MapPost("/administrators", ([FromBody] AdministratorDTO administratorDTO, IAdministratorService administratorService) =>
{
  var validation = ErrorAdministratorValidation(administratorDTO);
  if (validation.Messages.Count > 0) return Results.BadRequest(validation);

  var administrator = new Administrator
  {
    Email = administratorDTO.Email,
    Password = administratorDTO.Password,
    Profile = administratorDTO.Profile.ToString()
  };

  administratorService.PostAdministrator(administrator);

  var administratorMV = new AdministratorMV
  {
    Id = administrator.Id,
    Email = administrator.Email,
    Profile = administrator.Profile,
  };

  return Results.Created($"/administrator/{administrator.Id}", administratorMV);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Administrators");

app.MapGet("/administrators", ([FromQuery] int? page, IAdministratorService administratorService) =>
{
  var administrators = administratorService.GetAdministrators(page);
  var administratorsMV = new List<AdministratorMV>();

  foreach (var adm in administrators)
  {
    administratorsMV.Add(new AdministratorMV { Id = adm.Id, Email = adm.Email, Profile = adm.Profile });
  }

  return Results.Ok(administratorsMV);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Administrators");

app.MapGet("/administrator/{id}", ([FromRoute] int id, IAdministratorService administratorService) =>
{
  var administrator = administratorService.GetAdministratorId(id);
  if (administrator == null) return Results.NotFound();

  var administratorMV = new AdministratorMV
  {
    Id = administrator.Id,
    Email = administrator.Email,
    Profile = administrator.Profile,
  };

  return Results.Ok(administratorMV);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Administrators");

app.MapDelete("/administrator/{id}", ([FromRoute] int id, IAdministratorService administratorService) =>
{
  var administrator = administratorService.GetAdministratorId(id);
  if (administrator == null) return Results.NotFound();

  administratorService.DeleteAdministrator(administrator);
  return Results.NoContent();
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Administrators");
#endregion

#region Vehicles
static ErrorVehicle ErrorVehicleValidation(VehicleDTO vehicleDTO)
{
  var error = new ErrorVehicle { Messages = [] };

  if (vehicleDTO.Name.IsNullOrEmpty()) error.Messages.Add("O nome não pode ser um dado vazio.");
  if (vehicleDTO.Brand.IsNullOrEmpty()) error.Messages.Add("A marca não pode ser um dado vazio.");
  if (vehicleDTO.Year < 1950) error.Messages.Add("Veículo muito antigo. A ano deve ser igual ou superior a 1950.");

  return error;
};

app.MapPost("/vehicles", ([FromBody] VehicleDTO vehicleDTO, IVehicleService vehicleService) =>
{
  var validation = ErrorVehicleValidation(vehicleDTO);
  if (validation.Messages.Count > 0) return Results.BadRequest(validation);

  var vehicle = new Vehicle
  {
    Name = vehicleDTO.Name,
    Brand = vehicleDTO.Brand,
    Year = vehicleDTO.Year
  };

  vehicleService.PostVehicle(vehicle);
  return Results.Created($"/vehicle/{vehicle.Id}", vehicle);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin, Editor" })
.WithTags("Vehicles");

app.MapGet("/vehicles", ([FromQuery] int? page, IVehicleService vehicleService) =>
{
  var vehicles = vehicleService.GetVehicle(page);
  return Results.Ok(vehicles);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin, Editor" })
.WithTags("Vehicles");

app.MapGet("/vehicle/{id}", ([FromRoute] int id, IVehicleService vehicleService) =>
{
  var vehicle = vehicleService.GetVehicleId(id);
  if (vehicle == null) return Results.NotFound();
  return Results.Ok(vehicle);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin, Editor" })
.WithTags("Vehicles");

app.MapGet("/vehiclesName/{name}", ([FromRoute] string name, int? page, IVehicleService vehicleService) =>
{
  var vehicles = vehicleService.GetVehicleName(page, name);
  return Results.Ok(vehicles);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin, Editor" })
.WithTags("Vehicles");

app.MapGet("/vehiclesBrand/{brand}", ([FromRoute] string brand, int? page, IVehicleService vehicleService) =>
{
  var vehicles = vehicleService.GetVehicleBrand(page, brand);
  return Results.Ok(vehicles);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin, Editor" })
.WithTags("Vehicles");

app.MapPut("/vehicle/{id}", ([FromRoute] int id, VehicleDTO vehicleDTO, IVehicleService vehicleService) =>
{
  var vehicle = vehicleService.GetVehicleId(id);
  if (vehicle == null) return Results.NotFound();

  var validation = ErrorVehicleValidation(vehicleDTO);
  if (validation.Messages.Count > 0) return Results.BadRequest(validation);

  vehicle.Name = vehicleDTO.Name;
  vehicle.Brand = vehicleDTO.Brand;
  vehicle.Year = vehicleDTO.Year;

  vehicleService.UpdateVehicle(vehicle);
  return Results.Ok(vehicle);
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Vehicles");

app.MapDelete("/vehicle/{id}", ([FromRoute] int id, IVehicleService vehicleService) =>
{
  var vehicle = vehicleService.GetVehicleId(id);
  if (vehicle == null) return Results.NotFound();

  vehicleService.DeleteVehicle(vehicle);
  return Results.NoContent();
})
.RequireAuthorization()
.RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" })
.WithTags("Vehicles");
#endregion

#region App
// Configuração do Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Configuração para autenticação e autorização 
app.UseAuthentication();
app.UseAuthorization();

app.Run();
#endregion