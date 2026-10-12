using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests;

/// <summary>The generated spec shows the contract's JSON shape (§1.1, §1.2) to stockly-mobile.</summary>
public sealed class OpenApiShapeTests
{
    [Fact]
    public async Task Spec_ContractEnums_ArePascalCaseStrings()
    {
        var document = await OpenApiSpec.LoadRuntimeAsync();
        var schemas = document["components"]!["schemas"]!;

        schemas["ShopType"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>())
            .Should().Equal("Pharmacy", "Grocery", "MobileShop", "Other");
        schemas["ShopStatus"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).Should().Equal("Active", "Suspended");
        schemas["ShopRole"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).Should().Equal("Owner", "Staff");
    }

    [Fact]
    public async Task Spec_AuthResponse_HasCamelCaseFieldsAndDateTimeExpiries()
    {
        var document = await OpenApiSpec.LoadRuntimeAsync();
        var properties = document["components"]!["schemas"]!["AuthResponse"]!["properties"]!.AsObject();

        properties.Select(pair => pair.Key).Should().BeEquivalentTo(
            "accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "user", "shop");
        properties["accessTokenExpiresAt"]!["format"]!.GetValue<string>().Should().Be("date-time");
    }
}
