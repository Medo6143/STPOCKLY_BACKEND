using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Stockly.Api.Errors;
using Stockly.Domain.Common;

namespace Stockly.Api.Configuration;

/// <summary>Contract conventions (§1): JSON shape, request languages, and request-shape errors.</summary>
public static class ApiConventions
{
    public const string Arabic = "ar";
    public const string English = "en";

    private const string JsonPathRoot = "$";
    private const string BodyField = "body";

    /// <summary>
    /// Controllers with camelCase JSON, enums as PascalCase names (numbers rejected), and model-binding errors as
    /// <c>400 Validation.Failed</c>. Required fields are checked by FluentValidation (localized), not by MVC.
    /// </summary>
    public static IServiceCollection AddStocklyControllers(this IServiceCollection services)
    {
        services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = InvalidModelState)
            .AddJsonOptions(options => Configure(options.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(options => Configure(options.SerializerOptions));
        return services;
    }

    /// <summary>
    /// <c>Accept-Language</c> selects the UI culture, <c>ar</c> (default) or <c>en</c>, for error text and validation
    /// messages. Formatting stays culture-invariant: the API never formats numbers or dates for people.
    /// </summary>
    public static IServiceCollection AddStocklyLocalization(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(options =>
        {
            CultureInfo[] languages = [new(Arabic), new(English)];
            options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, languages[0]);
            options.SupportedCultures = [CultureInfo.InvariantCulture];
            options.SupportedUICultures = languages;
            options.FallBackToParentUICultures = true;
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

    private static void Configure(JsonSerializerOptions options) =>
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));

    private static ObjectResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .GroupBy(entry => FieldKey(entry.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, _ => new[] { ErrorText.InvalidValue }, StringComparer.Ordinal);

        var problem = StocklyProblems.Create(context.HttpContext, StatusCodes.Status400BadRequest, ValidationFailure.Code, errors);
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { StocklyProblems.ContentType } };
    }

    // "$.shop.name" → "shop.name"; the body itself ("$", "" or the parameter name) → "body".
    private static string FieldKey(string modelStateKey)
    {
        var path = modelStateKey.StartsWith(JsonPathRoot + ".", StringComparison.Ordinal)
            ? modelStateKey[(JsonPathRoot.Length + 1)..]
            : null;
        return string.IsNullOrEmpty(path) ? BodyField : Application.Validation.FieldPath.ToCamelCase(path);
    }
}
