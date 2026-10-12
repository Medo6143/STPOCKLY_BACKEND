namespace Stockly.Api.Configuration;

/// <summary>
/// OpenAPI documents served at <c>/openapi/{name}.json</c> outside Production. The committed copy
/// (<c>docs/api/openapi.json</c>) is written by <c>OpenApiDocumentTests</c>, never by the build (ADR-0011).
/// </summary>
public static class OpenApiDocuments
{
    public const string V1 = "v1";
}
