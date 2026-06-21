using Sandbox;

namespace ZS;

/// <summary>
/// Простая 3D-прицель: маленький квадратик, прикреплённый к камере.
/// </summary>
public class Crosshair : Component
{
    private GameObject marker;

    [Property]
    public float Distance { get; set; } = 200f;

    protected override void OnAwake()
    {
        base.OnAwake();

        var cam = Scene.Camera?.GameObject;
        if ( cam == null ) return;

        marker = new GameObject();
        marker.Name = "crosshair_marker";
        var mr = marker.Components.Create<ModelRenderer>();
        mr.Model = Model.Load( "models/dev/box.vmdl" );
        marker.Parent = cam;
        marker.LocalPosition = new Vector3( 0, 0, Distance );
        marker.LocalRotation = Rotation.Identity;
        marker.LocalScale = new Vector3( 0.02f, 0.02f, 0.001f );
    }
}
