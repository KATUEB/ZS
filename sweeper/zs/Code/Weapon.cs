using Sandbox;
using System.Linq;
using System.Collections.Generic;

namespace ZS;

/// <summary>
/// Компонент для огнестрельного оружия. Поддерживает экипировку в первое лицо при подборе.
/// </summary>
public class Weapon : Component
{
    [Property]
    public Vector3 FirstPersonOffset { get; set; } = new Vector3( 0.25f, -0.05f, 0.6f );

    [Property]
    public Rotation FirstPersonRotation { get; set; } = Rotation.Identity;

    [Property]
    public bool HideInWorldWhenEquipped { get; set; } = true;

    private bool isEquipped = false;
    private GameObject owner;
    private GameObject originalParent;
    private Vector3 originalPosition;
    private Rotation originalRotation;
    private Rigidbody rb;
    private List<Collider> disabledColliders = new();
    private GameObject armsObject;
    private Vector3 armsOriginalLocalPos;
    private Rotation armsOriginalLocalRot;
    private bool armsPoseApplied = false;

    [Property]
    public bool ApplyHandPose { get; set; } = true;

    [Property]
    public Vector3 ArmsLocalPositionOffset { get; set; } = new Vector3( 0f, 0f, 0f );

    [Property]
    public Rotation ArmsLocalRotationOffset { get; set; } = Rotation.Identity;

    public void Equip( GameObject picker )
    {
        if ( isEquipped ) return;

        owner = picker;
        originalParent = GameObject.Parent;
        originalPosition = GameObject.WorldPosition;
        originalRotation = GameObject.WorldRotation;

        // Попробуем найти камеру игрока
        var use = picker?.Components.Get<UseButton>();
        var camGo = use?.CameraObject ?? Scene.Camera?.GameObject ?? picker;

        // Отключаем физику, чтобы не мешала при привязке
        rb = GameObject.Components.Get<Rigidbody>();
        if ( rb != null )
        {
            rb.Enabled = false;
            rb.Gravity = false;
        }

        // Прикрепляем к камере: найдём точку крепления в камере (weapon_root), если есть
        GameObject attachTo = FindChildByName( camGo, "weapon_root" ) ?? camGo;
        GameObject.Parent = attachTo;

        // Всегда применяем локальный оффсет/ротацию относительно узла крепления.
        // Это предотвращает ситуации, когда weapon_root в сцене расположен на голове.
        GameObject.LocalPosition = FirstPersonOffset;
        GameObject.LocalRotation = FirstPersonRotation;

        // Отключим все коллайдеры в иерархии оружия при экипировке,
        // иначе модель может физически толкать игрока
        disabledColliders.Clear();
        CollectColliders( GameObject, disabledColliders );
        foreach ( var c in disabledColliders )
        {
            c.Enabled = false;
        }

        // Попробуем найти модель рук и применить простую локальную поправку
        if ( ApplyHandPose )
        {
            armsObject = FindChildByName( camGo, "v_first_person_arms_human" );
            if ( armsObject != null )
            {
                armsOriginalLocalPos = armsObject.LocalPosition;
                armsOriginalLocalRot = armsObject.LocalRotation;
                armsObject.LocalPosition = armsOriginalLocalPos + ArmsLocalPositionOffset;
                armsObject.LocalRotation = ArmsLocalRotationOffset * armsOriginalLocalRot;
                armsPoseApplied = true;
            }
        }

        // Убедимся, что на камере есть Crosshair
        var cross = camGo.Components.Get<Crosshair>();
        if ( cross == null )
            camGo.Components.Create<Crosshair>();

        if ( HideInWorldWhenEquipped )
        {
            // Если нужно, можно скрыть физическую модель и включить отдельную FP-модель.
            // Здесь пока просто отключаем рендер-джобы через флаг Components (если есть Renderer-компонент,
            // у него нет общего интерфейса в этом репо), поэтому оставляем заглушку.
        }

        isEquipped = true;

        Log.Info( $"Оружие {GameObject.Name} экипировано игроком {picker?.Name ?? "(неизвестный)"}" );
    }

    private static GameObject FindChildByName( GameObject root, string name )
    {
        if ( root == null ) return null;
        if ( root.Name == name ) return root;

        foreach ( var child in root.Children )
        {
            var found = FindChildByName( child, name );
            if ( found != null ) return found;
        }

        return null;
    }

    private static void CollectColliders( GameObject go, List<Collider> result )
    {
        var c = go.Components.Get<Collider>();
        if ( c != null ) result.Add( c );
        foreach ( var child in go.Children ) CollectColliders( child, result );
    }

    public void Unequip( GameObject picker )
    {
        if ( !isEquipped ) return;

        // Отсоединяем от камеры
        GameObject.Parent = originalParent;

        // Восстанавливаем позицию/ротацию в мир
        GameObject.WorldPosition = originalPosition;
        GameObject.WorldRotation = originalRotation;

        // Включаем физику и бросаем немного вперёд
        if ( rb == null )
            rb = GameObject.Components.Get<Rigidbody>();

        if ( rb != null )
        {
            rb.Enabled = true;
            rb.Gravity = true;
            var forward = picker?.WorldRotation.Forward ?? Scene.Camera?.GameObject.WorldRotation.Forward ?? Vector3.Forward;
            rb.Velocity = forward * 300f;
            if ( rb.PhysicsBody != null ) rb.PhysicsBody.Sleeping = false;
        }

        owner = null;
        isEquipped = false;

        // Включим обратно ранее отключённые коллайдеры
        foreach ( var c in disabledColliders )
        {
            if ( c != null ) c.Enabled = true;
        }
        disabledColliders.Clear();

        // Восстановим позицию рук, если применяли
        if ( armsPoseApplied && armsObject != null )
        {
            armsObject.LocalPosition = armsOriginalLocalPos;
            armsObject.LocalRotation = armsOriginalLocalRot;
            armsPoseApplied = false;
            armsObject = null;
        }

        Log.Info( $"Оружие {GameObject.Name} отброшено игроком {picker?.Name ?? "(неизвестный)"}" );
    }

    public bool IsEquipped => isEquipped;

    [Property]
    public float BulletRange { get; set; } = 3000f;

    [Property]
    public float BulletForce { get; set; } = 100f;

    [Property]
    public string FireSound { get; set; } = "sounds/weapons/pistol_fire.sound";

    [Property]
    public bool PlayFireSound { get; set; } = false;

    [Property]
    public string ShellModel { get; set; } = "models/dev/box.vmdl";

    [Property]
    public string MuzzleName { get; set; } = "muzzle";

    [Property]
    public Vector3 MuzzleFallbackOffset { get; set; } = new Vector3( 0.6f, 0f, 0f );

    public void Fire()
    {
        if ( owner == null ) return;

        var cam = owner.Components.Get<UseButton>()?.CameraObject ?? Scene.Camera?.GameObject ?? owner;

        // Prefer a named muzzle point on the weapon model if present
        var muzzle = FindChildByName( GameObject, MuzzleName ) ?? FindChildByName( GameObject, "muzzle_point" ) ?? FindChildByName( GameObject, "muzzle_world" );
        var start = muzzle != null ? muzzle.WorldPosition : GameObject.WorldPosition + GameObject.WorldRotation.Forward * MuzzleFallbackOffset.x + GameObject.WorldRotation.Right * MuzzleFallbackOffset.y + GameObject.WorldRotation.Up * MuzzleFallbackOffset.z;
        var dir = muzzle != null ? muzzle.WorldRotation.Forward : cam.WorldRotation.Forward;
        var rayStart = start + dir * 0.1f; // move start slightly forward to avoid self-hit on muzzle
        var end = rayStart + dir * BulletRange;

        // Debug: log muzzle detection and start position
        Log.Info( $"[Fire] Muzzle found: {(muzzle != null ? "YES (" + muzzle.Name + ")" : "NO - using fallback")}, Start pos: {start}, RayStart: {rayStart}, Dir: {dir}" );
        Log.Info( $"[Fire] Weapon on: {GameObject.Name}, Weapon parent: {GameObject.Parent?.Name}, Weapon children: {GameObject.Children.Count()}" );
        
        // Debug marker at muzzle (visible for ~0.2s)
        if ( muzzle != null )
        {
            var dbgMarker = new GameObject();
            dbgMarker.Name = "DEBUG_muzzle_point";
            var dbgRend = dbgMarker.Components.Create<ModelRenderer>();
            dbgRend.Model = Model.Load( "models/dev/source/sphere.vmdl" );
            dbgMarker.WorldPosition = muzzle.WorldPosition;
            dbgMarker.WorldScale = new Vector3( 0.05f, 0.05f, 0.05f );
            var dbgLife = dbgMarker.Components.Create<TemporaryLife>();
            dbgLife.LifeSeconds = 0.2f;
        }

        var hits = Scene.Trace.Ray( rayStart, end )
            .IgnoreGameObjectHierarchy( owner )
            .RunAll()
            .ToList();

        if ( hits.Count == 0 )
        {
            Log.Info( "Выстрел: ничего не попало" );
            // still spawn shell and tracer
        }

        var hit = hits.Count > 0 ? hits[0] : default;
        var hitObject = hit.Collider?.GameObject ?? hit.GameObject;

        // Debug: log hit info
        Log.Info( $"[Fire] Rays fired from {start} in dir {dir}, hits: {hits.Count}" );

        // Determine accurate hit position and normal when available
        Vector3 hitPos;
        Vector3 hitNormal;
        if ( hits.Count > 0 )
        {
            // Try dynamic access to common fields on the trace result
            try
            {
                dynamic dh = hit;
                if ( dh.Position != null ) { hitPos = (Vector3)dh.Position; }
                else if ( dh.HitPosition != null ) { hitPos = (Vector3)dh.HitPosition; }
                else if ( dh.Point != null ) { hitPos = (Vector3)dh.Point; }
                else hitPos = end;
            }
            catch
            {
                hitPos = end;
            }

            try
            {
                dynamic dhn = hit;
                hitNormal = dhn.Normal != null ? (Vector3)dhn.Normal : (hitPos - start).Normal;
            }
            catch
            {
                hitNormal = (hitPos - start).Normal;
            }
        }
        else
        {
            hitPos = end;
            hitNormal = -dir;
        }

        Log.Info( $"Попадание: {hitObject?.Name ?? "(мир)"} в точке {hitPos} нормаль {hitNormal}" );

        // Трейсер убрали — только следы от попаданий


        // Отметка попадания: видимая сфера, расположенная немного перед поверхностью
        if ( hits.Count > 0 )
        {
            var mark = new GameObject();
            mark.Name = "bullet_impact";
            var mr = mark.Components.Create<ModelRenderer>();
            mr.Model = Model.Load( "models/dev/source/sphere.vmdl" );

            var markOffset = hitNormal * 0.15f;
            mark.WorldPosition = hitPos + markOffset;
            mark.WorldScale = new Vector3( 0.2f, 0.2f, 0.2f );
            mark.WorldRotation = Rotation.Identity;

            var t2 = mark.Components.Create<TemporaryLife>();
            t2.LifeSeconds = 8f;

            Log.Info( $"[Impact VIS] spawned at {mark.WorldPosition}, hitPos={hitPos}, offset={markOffset}" );

            var hitRb = hitObject?.Components.Get<Rigidbody>();
            if ( hitRb != null )
            {
                hitRb.Velocity += -hitNormal * BulletForce;
            }
        }

        // Мюзлфлеш: огненная вспышка из дула
        var flash = new GameObject();
        flash.Name = "muzzle_flash";
            var fr = flash.Components.Create<ModelRenderer>();
            fr.Model = Model.Load( "models/dev/source/sphere.vmdl" );
        flash.WorldPosition = start + dir * 0.15f;
        flash.WorldRotation = Rotation.LookAt( dir );
        flash.WorldScale = new Vector3( 0.12f, 0.12f, 0.28f );
        try { fr.Tint = Color.Orange; } catch { }
        var t3 = flash.Components.Create<TemporaryLife>();
        t3.LifeSeconds = 0.1f;

        var flashCore = new GameObject();
        flashCore.Name = "muzzle_flash_core";
        var fcr = flashCore.Components.Create<ModelRenderer>();
        fcr.Model = Model.Load( "models/dev/source/sphere.vmdl" );
        flashCore.WorldPosition = start + dir * 0.08f;
        flashCore.WorldScale = new Vector3( 0.08f, 0.08f, 0.08f );
        try { fcr.Tint = Color.Yellow; } catch { }
        var t5 = flashCore.Components.Create<TemporaryLife>();
        t5.LifeSeconds = 0.08f;

        // Пружение гильзы: создаём объект с физикой и выбрасываем в сторону от дула
        var shell = new GameObject();
        shell.Name = "shell_casing";
        var smr = shell.Components.Create<ModelRenderer>();
        smr.Model = Model.Load( ShellModel );
        // Position shell near muzzle, slightly to the right and up
        shell.WorldPosition = start + (muzzle != null ? muzzle.WorldRotation.Right : cam.WorldRotation.Right) * 0.12f + (muzzle != null ? muzzle.WorldRotation.Up : cam.WorldRotation.Up) * 0.06f + (muzzle != null ? muzzle.WorldRotation.Forward : cam.WorldRotation.Forward) * 0.03f;
        shell.WorldRotation = (muzzle != null ? muzzle.WorldRotation : cam.WorldRotation) * Rotation.From( 0, 0, 90 );
        shell.WorldScale = new Vector3( 0.035f, 0.012f, 0.012f );
        var srb = shell.Components.Create<Rigidbody>();
        srb.Enabled = true;
        srb.Gravity = true;
        var shellRight = (muzzle != null ? muzzle.WorldRotation.Right : cam.WorldRotation.Right);
        var shellUp = (muzzle != null ? muzzle.WorldRotation.Up : cam.WorldRotation.Up);
        srb.Velocity = shellRight * (120f + (float)(System.Random.Shared.NextDouble() * 60f)) + shellUp * (60f + (float)(System.Random.Shared.NextDouble() * 30f));
        srb.AngularVelocity = new Vector3( (float)(System.Random.Shared.NextDouble() * 20f - 10f), (float)(System.Random.Shared.NextDouble() * 20f - 10f), (float)(System.Random.Shared.NextDouble() * 20f - 10f) );
        var t4 = shell.Components.Create<TemporaryLife>();
        t4.LifeSeconds = 8f;

        // Sound: play fire sound at muzzle position (optional)
        if ( PlayFireSound )
        {
            try
            {
                Sound.Play( FireSound, cam.WorldPosition );
            }
            catch
            {
                // Sound API may differ; swallow errors to avoid crashing
            }
        }
    }
}


