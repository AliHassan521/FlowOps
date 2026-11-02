using System.Text.Json;
using FlowOps.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class HttpErrorTests
{
    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    [InlineData(431)]
    public async Task Protocol_errors_preserve_client_status_without_leaking_server_details(int status)
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream(); context.Response.Body = body;
        var handler = new ExceptionHandlingMiddleware(_ => throw new BadHttpRequestException("Internal parser details", status),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        await handler.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        body.Position = 0;
        using var result = await JsonDocument.ParseAsync(body);
        Assert.Equal("The HTTP request was rejected.", result.RootElement.GetProperty("message").GetString());
    }
}
