using Newtonsoft.Json.Linq;
using Prosequor.Client;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Level-up clip gain: default half, clamp, and mute.</summary>
public static class LevelUpAudioFixtures
{
    public static void VerifyAll()
    {
        if (LevelUpAudio.ToGain(LevelUpAudio.DefaultPercent) != 0.5f)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (default gain is half).");
        }

        if (LevelUpAudio.ToGain(0) != 0f || LevelUpAudio.ToGain(100) != 1f)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (mute and full gain).");
        }

        if (LevelUpAudio.ToGain(-10) != 0f || LevelUpAudio.ToGain(140) != 1f)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (clamp).");
        }

        JObject missing = new();
        if (!LevelUpAudio.Normalize(missing, out int filled) || filled != LevelUpAudio.DefaultPercent)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (missing volume defaults to 50).");
        }

        JObject muted = new() { [LevelUpAudio.VolumeProperty] = 0 };
        if (LevelUpAudio.Normalize(muted, out int zero) || zero != 0)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (explicit zero stays muted).");
        }

        JObject hot = new() { [LevelUpAudio.VolumeProperty] = 150.2 };
        if (!LevelUpAudio.Normalize(hot, out int clamped) || clamped != 100)
        {
            Assert.Fail("[prosequor] Level-up audio fixture failed (out of range clamps to 100).");
        }
    }
}
