namespace Prosequor.Ability;

/// <summary>Per-animal alert meter runtime fields (server RAM only).</summary>
public sealed class AnimalAlertState
{
    public float Alert;
    public float CurrentThreat;
    public bool Awake;
    public bool Committed;
    public long AlertTargetEntityId;
    public float AlertTargetThreat;
    public bool PropagatedHerd;
    public float CharacteristicRange = 30f;
    public float TamingGenerations = 10f;
    public float InstaFleeOnDamageChance;
    public float HerdNotifyRange = 12f;
    public bool SensesPlayers;
    public bool WasCommitted;
    public long LastIntegrateMs;

    /// <summary>Last sampled instantaneous threat (flee rolls only on a rise).</summary>
    public float LastThreatSample;

    /// <summary>Last movement band of the alert target (rolls on posture change).</summary>
    public int LastMovementBand = AnimalAlertMath.BandStill;

    public int LastSyncedAlert = -1;
    public int LastSyncedThreat = -1;
    public int LastSyncedFlags = -1;
}
