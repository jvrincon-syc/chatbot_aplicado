namespace Chatbot.Sst.Domain;

/// <summary>Raw question as received from the client, before any processing.</summary>
public sealed record UserQuestion(string Text);

/// <summary>Question after normalization (trim, casing, whitespace, etc.).</summary>
public sealed record NormalizedQuestion(string Text);
