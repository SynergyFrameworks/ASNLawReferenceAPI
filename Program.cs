using LegalReferenceAPI.Services;
using LegalReferenceAPI.Data;
using LegalReferenceAPI.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using LegalReferenceAPI.Background;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { 
        Title = "Legal Reference Platform API", 
        Version = "v1",
        Description = "API for legal document management with RAG capabilities and PDF integrity"
    });
    
    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
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
            Array.Empty<string>()
        }
    });
});

// Configure database
builder.Services.AddDbContext<DocumentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ResearcherPolicy", policy => policy.RequireRole("Researcher"));
    options.AddPolicy("ViewerPolicy", policy => policy.RequireRole("Viewer"));
});

// Register services
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IAnnotationService, AnnotationService>();
builder.Services.AddScoped<IIndexingService, IndexingService>();

// Register repository pattern implementations
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IChunkRepository, ChunkRepository>();
builder.Services.AddScoped<IAnnotationRepository, AnnotationRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Register infrastructure services
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
builder.Services.AddSingleton<IVectorDbService, QdrantVectorDbService>();
builder.Services.AddSingleton<IFullTextSearchService, OpenSearchService>();
builder.Services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();

// Register background services
builder.Services.AddHostedService<DocumentProcessingBackgroundService>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DocumentDbContext>()
    .AddCheck<BlobStorageHealthCheck>("BlobStorage")
    .AddCheck<VectorDbHealthCheck>("VectorDb")
    .AddCheck<OpenSearchHealthCheck>("OpenSearch");

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
       corsBuilder => corsBuilder
           .WithOrigins(
               builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()
           )
           .AllowAnyMethod()
           .AllowAnyHeader()
           .AllowCredentials()
   );
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Run database migrations in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    dbContext.Database.Migrate();
}

app.Run();