﻿using Api.ExceptionMappers;
using Api.Middlewares;
using Api.Properties;
using Api.Swagger.AuthFilter;
using ExceptionCatcherMiddleware.Extensions;
using Infrastructure;
using Infrastructure.Auther;
using Infrastructure.Auther.Client;
using Infrastructure.Auther.Exceptions;
using Infrastructure.Auther.Helpers;
using Infrastructure.CorrelationIdSystem.Repository;
using Infrastructure.CorrelationIdSystem.Serilog;
using Infrastructure.CorrelationIdSystem.Yarp;
using Infrastructure.Yarp;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using SwaggerEnrichers.Extensions;

namespace Api.Extensions;

public static class DiExtensions
{
    public static void AddConfiguredExceptionCatcherMiddleware(this IServiceCollection services)
    {
        services.AddExceptionCatcherMiddlewareServices(builder =>
        {
            builder.RegisterExceptionMapper<SecurityTokenExpiredException, SecurityTokenExpiredExceptionMapper>();
            builder.RegisterExceptionMapper<JwtTokenNotProvidedException, JwtTokenNotProvidedExceptionMapper>();
        });
        services.AddScoped<ServiceBadResponseExceptionCatcherMiddleware>();
    }

    public static void AddAuther(this IServiceCollection services, AccountServiceUrlProvider urlProvider)
    {
        services.AddScoped<HttpClient>();
        services.AddScoped<ISimpleHttpClient, SimpleHttpClient>();
        services.AddScoped<IJwtTokenProvider>(_ =>
        {
            HttpContext? context = new HttpContextAccessor().HttpContext;
            if (context is null)
            {
                throw new InvalidOperationException("You are trying to resolve JwtTokenProvider out of request");
            }

            return new JwtTokenProvider(context);
        });
        services.AddSingleton(urlProvider);
        services.AddScoped<IAuther, Auther>();
    }

    public static void AddForwardInfoOptions(this IServiceCollection services, ParametersProvider parameters)
    {
        services.AddSingleton(parameters.ProductServiceForwardInfoOptions);
        services.AddSingleton(parameters.BasketServiceForwardInfoOptions);
        services.AddSingleton(parameters.AccountServiceForwardInfoOptions);
    }

    public static void AddConfiguredCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.AllowAnyOrigin();
                builder.AllowAnyHeader();
                builder.AllowAnyHeader();
            });
        });
    }

    public static void AddCorrelationId(this IServiceCollection services)
    {
        services.AddScoped<CorrelationIdGeneratorMiddleware>();
        services.AddScoped<CorrelationIdRequestTransformer>();
        services.AddScoped<ICorrelationIdProvider, CorrelationIdHttpContextRepository>();
        services.AddScoped<ICorrelationIdSaver, CorrelationIdHttpContextRepository>();
        services.AddScoped<SerilogCorrelationIdEnricher>();
    }

    public static void AddConfiguredSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter a valid token",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });
            options.OperationFilter<AuthOperationFilter>();
            
            options.AddEnricherFilters();
        });
    }

    public static void AddSerilog(this WebApplicationBuilder builder, string seqUrl)
    {
        builder.Services.AddHttpContextAccessor();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", "Gateway")
            .Enrich.With(builder.Services.BuildServiceProvider().GetRequiredService<SerilogCorrelationIdEnricher>())
            .WriteTo.Console()
            .WriteTo.Seq(seqUrl)
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();
        builder.Host.UseSerilog();
    }

    public static void AddYarp(this IServiceCollection services, IConfiguration yarp)
    {
        services.AddScoped<YarpExceptionRethrowerMiddleware>();

        IReverseProxyBuilder builder = services.AddReverseProxy();
        
        var registrator = new YarpConfigurator(services);
        registrator.RegisterForwardInfosFromAssembly<InfrastructureAssemblyReference>();
        registrator.RegisterRequestTransformer<CorrelationIdRequestTransformer>();
        registrator.Apply(builder);
        
        builder.LoadFromConfig(yarp);
    }
}