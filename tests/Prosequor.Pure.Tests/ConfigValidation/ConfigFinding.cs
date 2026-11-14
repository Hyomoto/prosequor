namespace Prosequor.Pure.Tests.ConfigValidation;

/// <summary>One key or parse problem reported by a config validation rule.</summary>
public sealed record ConfigFinding(
    string RelativePath,
    string Pointer,
    string RuleName,
    string Message);
