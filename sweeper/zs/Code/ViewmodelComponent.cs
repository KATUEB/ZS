using Sandbox;

namespace ZS;

public sealed class ViewmodelComponent : Component
{
    [Property] public GameObject ViewmodelObject { get; set; }

    protected override void OnUpdate()
    {
        if ( ViewmodelObject == null ) return;

        // Фиксируем локальный rotation рук — только yaw камеры, без pitch
        ViewmodelObject.Transform.LocalRotation = Rotation.Identity;
        ViewmodelObject.Transform.LocalPosition = new Vector3( 0, 0, 0 );
    }
}
