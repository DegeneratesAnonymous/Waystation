// ============================================================
// DoorRenderer.cs
// Assets/Scripts/World/DoorRenderer.cs
// ============================================================
using UnityEngine;

public class DoorRenderer : MonoBehaviour
{
    [Header("Data")]
    public DoorAtlasData atlasData;

    [Header("Components")]
    public SpriteRenderer spriteRenderer;

    [Header("Orientation")]
    public DoorOrientation orientation = DoorOrientation.NS;

    [Header("State")]
    [Range(0f, 1f)] public float openFraction = 0f;
    public DoorHealthState healthState = DoorHealthState.Normal;

    private float _targetFraction;
    private float _animSpeed;
    private bool  _animating;

    void OnValidate() { ApplyOrientation(); Apply(); }

    void Start()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        ApplyOrientation();
        Apply();
    }

    void Update()
    {
        if (!_animating) return;
        openFraction = Mathf.MoveTowards(openFraction, _targetFraction, _animSpeed * Time.deltaTime);
        Apply();
        if (Mathf.Approximately(openFraction, _targetFraction))
            _animating = false;
    }

    // Instantly snap to a fraction
    public void SetOpenFraction(float f)
    {
        _animating   = false;
        openFraction = Mathf.Clamp01(f);
        Apply();
    }

    // Smooth animation to target (speed = fraction/sec, 3f = full travel in 0.33s)
    public void AnimateTo(float target, float speed = 3f)
    {
        _targetFraction = Mathf.Clamp01(target);
        _animSpeed      = speed;
        _animating      = true;
    }

    public void Open(float speed = 3f)  => AnimateTo(1f, speed);
    public void Close(float speed = 3f) => AnimateTo(0f, speed);

    public void SetHealthState(DoorHealthState state)
    {
        healthState = state;
        if (state != DoorHealthState.Normal) _animating = false;
        Apply();
    }

    // Pass normalised hp 0..1
    public void SetHealthFromNormalised(float normHp)
    {
        if      (normHp <= 0f)   SetHealthState(DoorHealthState.Destroyed);
        else if (normHp <= 0.5f) SetHealthState(DoorHealthState.Damaged);
        else                     SetHealthState(DoorHealthState.Normal);
    }

    public void SetOrientation(DoorOrientation o)
    {
        orientation = o;
        ApplyOrientation();
    }

    void ApplyOrientation() =>
        transform.rotation = Quaternion.Euler(0f, 0f, orientation == DoorOrientation.EW ? 90f : 0f);

    void Apply()
    {
        if (atlasData == null || spriteRenderer == null) return;
        spriteRenderer.sprite = atlasData.GetSprite(healthState, openFraction);
    }
}
