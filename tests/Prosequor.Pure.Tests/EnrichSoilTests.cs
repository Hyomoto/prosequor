using Prosequor.Ability.Actions;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Fertility-key selection for enrich-soil (no world required).</summary>
public class EnrichSoilTests
{
    [Fact]
    [Trait("Layer", "Action")]
    public void TryResolveFertilityKey_UsesNutrientCeiling()
    {
        // Original fertility low (25); remaining nutrients high; maxFertility 0 →
        // ceiling stays at original min, so key tracks nutrients capped by original.
        float[] nutrients = [40f, 40f, 40f];
        int[] original = [25, 25, 25];
        string? key = EnrichSoilAction.TryResolveFertilityKey(nutrients, original, maxFertility: 0f);
        Assert.Equal("low", key);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void TryResolveFertilityKey_AllowsMaxFertilityAboveOriginal()
    {
        float[] nutrients = [65f, 65f, 65f];
        int[] original = [5, 5, 5];
        string? key = EnrichSoilAction.TryResolveFertilityKey(nutrients, original, maxFertility: 65f);
        Assert.Equal("compost", key);
    }

    [Fact]
    [Trait("Layer", "Action")]
    public void TryResolveFertilityKey_ReturnsNullOnEmptyArrays()
    {
        Assert.Null(EnrichSoilAction.TryResolveFertilityKey([], [25], 65f));
        Assert.Null(EnrichSoilAction.TryResolveFertilityKey([25f], [], 65f));
    }
}
