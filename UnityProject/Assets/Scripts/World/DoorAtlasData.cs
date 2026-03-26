// ============================================================
// DoorAtlasData.cs
// Assets/Scripts/World/DoorAtlasData.cs
// ============================================================
using UnityEngine;

[CreateAssetMenu(menuName = "Door/Door Atlas Data", fileName = "DoorAtlasData")]
public class DoorAtlasData : ScriptableObject
{
    [Header("Open stages — index 0 (closed) to 9 (fully open)")]
    [Tooltip("Populated automatically by DoorAtlasSetup (Tools → Door → Setup Door Atlas)")]
    public Sprite[] openStages = new Sprite[10];

    [Header("Damage states")]
    public Sprite damaged;    // door_ns_damaged   — hp <= 50%
    public Sprite destroyed;  // door_ns_destroyed — hp = 0

    // Returns the correct open sprite for a 0..1 fraction
    // Maps: index = Mathf.RoundToInt(fraction * 9)
    public Sprite GetOpenSprite(float fraction)
    {
        int i = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 9);
        i = Mathf.Clamp(i, 0, openStages.Length - 1);
        return openStages[i];
    }

    public Sprite GetSprite(DoorHealthState state, float openFraction = 0f)
    {
        return state switch
        {
            DoorHealthState.Destroyed => destroyed,
            DoorHealthState.Damaged   => damaged,
            _                         => GetOpenSprite(openFraction),
        };
    }
}

public enum DoorHealthState { Normal, Damaged, Destroyed }
public enum DoorOrientation  { NS, EW }
