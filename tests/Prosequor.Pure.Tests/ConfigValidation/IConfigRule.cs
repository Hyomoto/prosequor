using Newtonsoft.Json.Linq;

namespace Prosequor.Pure.Tests.ConfigValidation;

/// <summary>
/// One family of config checks. New families (collections, pools, stats) register as
/// additional rules on <see cref="ConfigValidationRunner"/> without a new harness.
/// </summary>
public interface IConfigRule
{
    string Name { get; }

    /// <summary>
    /// Whether this rule should run on a path relative to
    /// <c>assets/prosequor/config/prosequor/</c> (forward slashes).
    /// </summary>
    bool AppliesTo(string relativePath);

    /// <summary>Append findings for one already-parsed document.</summary>
    void Check(string relativePath, JToken root, IList<ConfigFinding> findings);
}
