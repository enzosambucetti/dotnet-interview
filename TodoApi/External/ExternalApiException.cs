using System.Net;

namespace TodoApi.External;

public class ExternalApiException : Exception
{
    public ExternalApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
