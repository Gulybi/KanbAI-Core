using System.Text.Json;
using FluentAssertions;
using KanbAI_Core.DTOs;

namespace KanbAI_Core.Tests.DTOs;

public class ApiResponseTests
{
    #region Non-generic ApiResponse — Ok

    [Fact]
    public void Ok_NoMessage_SetsSuccessTrueAndEmptyErrors()
    {
        // Arrange & Act
        var response = ApiResponse.Ok();

        // Assert
        response.Success.Should().BeTrue();
        response.Message.Should().BeNull();
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Ok_WithMessage_SetsSuccessTrueAndMessage()
    {
        // Arrange
        const string message = "Operation completed";

        // Act
        var response = ApiResponse.Ok(message);

        // Assert
        response.Success.Should().BeTrue();
        response.Message.Should().Be(message);
        response.Errors.Should().BeEmpty();
    }

    #endregion

    #region Non-generic ApiResponse — Fail

    [Fact]
    public void Fail_WithMessage_SetsSuccessFalseAndMessage()
    {
        // Arrange
        const string message = "Something went wrong";

        // Act
        var response = ApiResponse.Fail(message);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Be(message);
    }

    [Fact]
    public void Fail_WithErrors_SetsErrorCollection()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };

        // Act
        var response = ApiResponse.Fail(errors);

        // Assert
        response.Success.Should().BeFalse();
        response.Errors.Should().HaveCount(2);
        response.Errors.Should().ContainInOrder("Error 1", "Error 2");
    }

    [Fact]
    public void Fail_WithEmptyErrors_SetsEmptyErrorCollection()
    {
        // Arrange
        var errors = Enumerable.Empty<string>();

        // Act
        var response = ApiResponse.Fail(errors);

        // Assert
        response.Success.Should().BeFalse();
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Fail_WithErrors_ErrorsCollectionIsReadOnly()
    {
        // Arrange
        var errors = new[] { "Error 1" };

        // Act
        var response = ApiResponse.Fail(errors);

        // Assert
        response.Errors.Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    #endregion

    #region Non-generic ApiResponse — Defaults

    [Fact]
    public void DefaultErrorsProperty_IsEmptyCollection()
    {
        // Arrange & Act
        var response = ApiResponse.Ok();

        // Assert
        response.Errors.Should().NotBeNull();
        response.Errors.Should().BeEmpty();
    }

    #endregion

    #region Generic ApiResponse<T> — Ok

    [Fact]
    public void GenericOk_WithData_SetsDataAndSuccessTrue()
    {
        // Arrange
        var data = new[] { "item1", "item2" };

        // Act
        var response = ApiResponse<string[]>.Ok(data);

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().BeSameAs(data);
        response.Message.Should().BeNull();
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GenericOk_WithDataAndMessage_SetsBothFields()
    {
        // Arrange
        const int data = 42;
        const string message = "Found it";

        // Act
        var response = ApiResponse<int>.Ok(data, message);

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().Be(42);
        response.Message.Should().Be(message);
    }

    [Fact]
    public void GenericOk_WithNullableReferenceData_AllowsNullData()
    {
        // Arrange & Act
        var response = ApiResponse<string?>.Ok(null!);

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().BeNull();
    }

    #endregion

    #region Generic ApiResponse<T> — Fail

    [Fact]
    public void GenericFail_WithMessage_SetsDataToDefault()
    {
        // Arrange & Act
        var response = ApiResponse<string>.Fail("Not found");

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Be("Not found");
        response.Data.Should().BeNull();
    }

    [Fact]
    public void GenericFail_WithErrors_SetsDataToDefault()
    {
        // Arrange
        var errors = new[] { "Name is required", "Email format is invalid" };

        // Act
        var response = ApiResponse<int>.Fail(errors);

        // Assert
        response.Success.Should().BeFalse();
        response.Errors.Should().HaveCount(2);
        response.Data.Should().Be(default(int));
    }

    [Fact]
    public void GenericFail_WithMessage_ErrorsRemainEmpty()
    {
        // Arrange & Act
        var response = ApiResponse<string>.Fail("Forbidden");

        // Assert
        response.Errors.Should().BeEmpty();
    }

    #endregion

    #region Inheritance

    [Fact]
    public void GenericApiResponse_IsAssignableToBaseApiResponse()
    {
        // Arrange & Act
        var response = ApiResponse<string>.Ok("test");

        // Assert
        response.Should().BeAssignableTo<ApiResponse>();
    }

    [Fact]
    public void GenericApiResponse_CastToBase_RetainsSuccessAndErrors()
    {
        // Arrange
        var errors = new[] { "Err1", "Err2" };
        var response = ApiResponse<string>.Fail(errors);

        // Act
        ApiResponse baseResponse = response;

        // Assert
        baseResponse.Success.Should().BeFalse();
        baseResponse.Errors.Should().HaveCount(2);
    }

    #endregion

    #region JSON Serialization

    [Fact]
    public void SuccessResponse_SerializedWithCamelCase_MatchesDocumentedShape()
    {
        // Arrange
        var data = new[] { new { date = "2026-04-07", temperatureC = 25, summary = "Warm" } };
        var response = ApiResponse<object[]>.Ok(data);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(response, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("message").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("errors").GetArrayLength().Should().Be(0);
        root.GetProperty("data").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void FailureResponse_SerializedWithCamelCase_MatchesDocumentedShape()
    {
        // Arrange
        var errors = new[] { "Name is required", "Email format is invalid" };
        var response = ApiResponse<object>.Fail(errors);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(response, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("errors").GetArrayLength().Should().Be(2);
        root.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void SuccessResponse_SerializedJson_ContainsAllExpectedProperties()
    {
        // Arrange
        var response = ApiResponse<string>.Ok("payload", "All good");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(response, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        root.TryGetProperty("success", out _).Should().BeTrue();
        root.TryGetProperty("message", out _).Should().BeTrue();
        root.TryGetProperty("errors", out _).Should().BeTrue();
        root.TryGetProperty("data", out _).Should().BeTrue();
    }

    [Fact]
    public void NonGenericResponse_SerializedJson_DoesNotContainDataProperty()
    {
        // Arrange
        var response = ApiResponse.Ok("Done");
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Act
        var json = JsonSerializer.Serialize(response, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        root.TryGetProperty("data", out _).Should().BeFalse(
            "non-generic ApiResponse should not include a 'data' property");
    }

    #endregion
}
