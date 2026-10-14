using System.Text;
using GmcV2.Calculation;
using GmcV2.Core;
using GmcV2.Infrastructure;
using GmcV2.Knowledge;
using GmcV2.Worker;

namespace GmcV2.Web;

/// <summary>Composition root. Builds app, DI, session; delegates route groups to Api and Pages.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var repo = WebHostFactory.BuildRepo();
        if (repo is InMemoryRepository mem) await SeedRunner.SeedReferenceDataAsync(mem);
        await WebSeed.EnsureAsync(repo);
        if (Environment.GetEnvironmentVariable("GMCV2_SQL") is { Length: > 0 } cs) ConnectionStringHolder.Set(cs);

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton(repo);
        // AI gateway (AD-1/AD-2): env-driven; empty registry = deterministic-only.
        IAIProviderRegistry aiRegistry = new AIProviderRegistry();
        if (OllamaAIProvider.FromEnv() is { IsAvailable: true } ollama)
            aiRegistry = new AIProviderRegistry { Extraction = ollama, Mapping = ollama, Clinical = ollama, Narrative = ollama };
        builder.Services.AddSingleton(aiRegistry);
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromHours(8); o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; });
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
        var app = builder.Build();
        app.UseCors();
        app.UseSession();

        Api.MapAll(app);
        ApiAdmin.MapAll(app);
        ApiCoding.MapAll(app);
        QuoteOverrideApi.Map(app);
        SIScenarioApi.Map(app);
        ApiMapping.MapAll(app);
        DiscountApi.Map(app);
        AnalyticsApi.Map(app);
        BenefitDeltaApi.Map(app);
        FinalQuoteApi.Map(app);
        StandardizeApi.Map(app);
        Pages.MapAll(app);

        app.UseStaticFiles(new StaticFileOptions
        {
            // always revalidate: ETag/304 when unchanged, fresh bytes when deployed —
            // prevents stale app.js breaking the console after releases
            OnPrepareResponse = ctx => ctx.Context.Response.Headers["Cache-Control"] = "no-cache"
        });
        app.Run();
        return 0;
    }
}

/// <summary>Repo construction shared with tests/tools.</summary>
public static class WebHostFactory
{
    public static IGmcRepository BuildRepo()
    {
        var sql = Environment.GetEnvironmentVariable("GMCV2_SQL");
        return string.IsNullOrWhiteSpace(sql) ? new InMemoryRepository() : new SqlRepository(sql);
    }
}
