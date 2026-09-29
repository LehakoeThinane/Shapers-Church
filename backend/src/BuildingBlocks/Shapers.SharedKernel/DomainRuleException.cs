namespace Shapers.SharedKernel;

/// <summary>A business rule was broken. The API maps it to a 400 response, never a server error.</summary>
public sealed class DomainRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
