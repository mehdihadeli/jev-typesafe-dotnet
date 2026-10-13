using System.Net;

namespace Typesafe.Sdk;

public sealed class TypeSafeApiException : Exception
{
    public TypeSafeApiException(HttpStatusCode statusCode, string responseBody)
        : base($"TypeSafe API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}