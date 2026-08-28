namespace Chatbot.Sst.Application.Abstractions;

public sealed class ChatDispatchException(string errorCode, string message, int? statusCode = null)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    public int? StatusCode { get; } = statusCode;
}
