using Sandbox;

namespace ZS;

/// <summary>
/// Component that destroys its GameObject after `LifeSeconds` elapses. Runs on the main thread in OnUpdate.
/// </summary>
public class TemporaryLife : Component
{
    [Property]
    public float LifeSeconds { get; set; } = 1f;

    private float elapsed = 0f;

    protected override void OnUpdate()
    {
        elapsed += Time.Delta;
        if ( elapsed >= LifeSeconds )
        {
            // Destroy must be called on the main thread; OnUpdate runs on the main thread.
            GameObject.Destroy();
        }
    }
}
