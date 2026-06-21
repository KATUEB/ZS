using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace ZS;

/// <summary>
/// Система взаимодействия с предметами: поднятие, перенос, вращение и закрепление в воздухе.
/// Перенос реализован через физику (через Velocity/AngularVelocity), а не через жёсткий телепорт
/// Transform.Position — поэтому предмет реально толкается, скользит и прокручивается об углы,
/// а не просто упирается и останавливается.
/// </summary>
public class UseButton : Component
{
	/// <summary>
	/// Максимальное расстояние для поднятия предмета
	/// </summary>
	[Property]
	public float PickupRange { get; set; } = 100f;

	/// <summary>
	/// Расстояние, на котором предмет держится от камеры
	/// </summary>
	[Property]
	public float CarryDistance { get; set; } = 50f;

	/// <summary>
	/// Базовая скорость передвижения с предметом (для CarrySpeedController, не влияет на сам перенос)
	/// </summary>
	[Property]
	public float BaseCarrySpeed { get; set; } = 200f;

	/// <summary>
	/// "Жёсткость" удержания: насколько резко предмет стремится к целевой точке.
	/// Больше = туже держит и резче бьётся об углы. Меньше = мягче, но может проседать.
	/// </summary>
	[Property]
	public float CarrySpeed { get; set; } = 15f;

	/// <summary>
	/// Ограничение скорости переноса, чтобы предмет не пробивал стены при большой ошибке позиции
	/// (например сразу после поднятия с большого расстояния)
	/// </summary>
	[Property]
	public float MaxCarryVelocity { get; set; } = 600f;

	/// <summary>
	/// Насколько сильно гасится вращение пока предмет в руках и ничего не касается.
	/// Высокое значение (10-20) = предмет быстро стабилизируется и держит одно направление.
	/// Низкое значение (1-3) = предмет долго крутится от любого касания.
	/// Столкновения со стенами/пропами всё равно передают импульс — он просто быстро затухает.
	/// </summary>
	[Property]
	public float AngularDamping { get; set; } = 15f;

	/// <summary>
	/// Порог угловой скорости (рад/сек), ниже которого вращение считается "паразитным" и гасится агрессивно.
	/// Выше порога (после удара) — предмет ещё немного покрутится прежде чем остановиться.
	/// </summary>
	[Property]
	public float AngularDampingThreshold { get; set; } = 2f;

	/// <summary>
	/// Скорость вращения поднятого предмета в режиме свободного вращения (градусы/сек на пиксель мыши)
	/// </summary>
	[Property]
	public float RotationSpeed { get; set; } = 180f;

	/// <summary>
	/// Чувствительность мыши для свободного вращения
	/// </summary>
	[Property]
	public float RotationSensitivity { get; set; } = 1f;

	/// <summary>
	/// Максимальный вес, который можно поднять
	/// </summary>
	[Property]
	public float MaxLiftWeight { get; set; } = 100f;

	/// <summary>
	/// Объект камеры (глаза игрока). Если не задан — берётся Scene.Camera.
	/// </summary>
	[Property]
	public GameObject CameraObject { get; set; }

	/// <summary>
	/// Имя Input Action для режима свободного вращения предмета (удержание).
	/// ЭТОГО действия нет в стандартной раскладке s&box — добавь его в
	/// Project Settings -> Input и впиши сюда то же имя, которое указал там.
	/// </summary>
	[Property]
	public string FreeRotateAction { get; set; } = "rotateitem";

	private Rigidbody carriedObject;
	private PickableItem carriedItem;
	private List<Rigidbody> carriedSubParts = new();
	private bool isCarrying => carriedObject != null;

	// Кешируем PlayerController чтобы не искать его каждый кадр
	private PlayerController playerController;

	/// <summary>
	/// True, пока игрок держит кнопку свободного вращения.
	/// </summary>
	public bool IsFreeRotating { get; private set; }

	protected override void OnAwake()
	{
		base.OnAwake();
		// Ищем PlayerController на этом же объекте или в иерархии
		playerController = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
	}

	private (Vector3 Position, Rotation Rotation) GetEyeTransform()
	{
		if ( CameraObject != null )
			return (CameraObject.Transform.Position, CameraObject.Transform.Rotation);

		if ( Scene.Camera != null )
			return (Scene.Camera.Transform.Position, Scene.Camera.Transform.Rotation);

		return (Transform.Position, Transform.Rotation);
	}

	/// <summary>
	/// Ищет PickableItem на объекте или у его родителей.
	/// </summary>
	private static PickableItem FindPickable( GameObject go )
	{
		var current = go;
		int safety = 5;

		while ( current != null && safety-- > 0 )
		{
			var p = current.Components.Get<PickableItem>();
			if ( p != null ) return p;
			current = current.Parent;
		}

		return null;
	}

	/// <summary>
	/// Собирает ВСЕ Rigidbody во всей иерархии пропа.
	/// </summary>
	private static void CollectRigidbodies( GameObject go, List<Rigidbody> result )
	{
		var rb = go.Components.Get<Rigidbody>();
		if ( rb != null )
			result.Add( rb );

		foreach ( var child in go.Children )
			CollectRigidbodies( child, result );
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy )
		{
			HandleInput();
			UpdateCarriedObject();
		}
	}

	private void HandleInput()
	{
		// Если объект пропал (уничтожен снаружи) — чистим состояние до обработки ввода
		if ( isCarrying && (carriedObject == null || !carriedObject.IsValid() || !carriedObject.GameObject.IsValid()) )
		{
			Log.Info( "Переносимый объект исчез — сброс состояния" );
			ForceReleaseCarriedObject();
		}

		if ( Input.Pressed( "use" ) )
		{
			Log.Info( $"Use нажата. Несу: {isCarrying}" );

			if ( isCarrying )
				DropObject();
			else
				TryPickupObject();
		}

		bool freeRotateHeld = false;
		try
		{
			freeRotateHeld = Input.Down( FreeRotateAction );
		}
		catch
		{
		}

		IsFreeRotating = isCarrying && freeRotateHeld;

		if ( IsFreeRotating )
			RotateCarriedObject();

		// Secondary Attack — закрепить предмет в воздухе
		if ( Input.Pressed( "attack2" ) && isCarrying )
			PinAndReleaseObject();
	}

	private void TryPickupObject()
	{
		var (eyePos, eyeRot) = GetEyeTransform();

		var forward = eyeRot.Forward;
		var startPos = eyePos;
		var endPos = startPos + forward * PickupRange;

		Log.Info( $"Ищу предметы от {startPos} до {endPos}" );

		var hits = Scene.Trace
			.Ray( startPos, endPos )
			.Radius( 20f )
			.IgnoreGameObjectHierarchy( GameObject )
			.RunAll()
			.ToList();

		Log.Info( $"Найдено hitов: {hits.Count}" );

		float closestDistance = float.MaxValue;
		Rigidbody closestRigidbody = null;
		PickableItem closestPickable = null;

		foreach ( var hit in hits )
		{
			var hitObject = hit.Collider?.GameObject ?? hit.GameObject;
			if ( hitObject == null ) continue;

			Log.Info( $"Проверяю: {hitObject.Name}" );

			var pickable = FindPickable( hitObject );
			if ( pickable == null )
			{
				Log.Info( $"  -> Нет PickableItem (ни на объекте, ни у родителей)" );
				continue;
			}

			if ( pickable.Weight > MaxLiftWeight )
			{
				Log.Info( $"  -> Вес {pickable.Weight} > {MaxLiftWeight}" );
				continue;
			}

			// FIX 1: Если предмет закреплён (Rigidbody выключен) — включаем его обратно перед подъёмом
			var rigidbody = pickable.GameObject.Components.Get<Rigidbody>();
			if ( rigidbody == null )
			{
				Log.Info( $"  -> На {pickable.GameObject.Name} нет Rigidbody" );
				continue;
			}

			if ( pickable.IsPinned )
			{
				Log.Info( $"  -> Предмет закреплён, открепляем перед подъёмом" );
				rigidbody.Enabled = true;
				pickable.SetPinned( false );
			}

			var distance = (pickable.GameObject.Transform.Position - startPos).Length;

			Log.Info( $"  -> Можно поднять! Расстояние: {distance}" );

			if ( distance < closestDistance )
			{
				closestDistance = distance;
				closestRigidbody = rigidbody;
				closestPickable = pickable;
			}
		}

		if ( closestRigidbody != null && closestPickable != null )
		{
			PickupObject( closestRigidbody, closestPickable );
		}
		else
		{
			Log.Info( "Не найдено подходящего предмета" );
		}
	}

	private void PickupObject( Rigidbody rigidbody, PickableItem pickable )
	{
		carriedObject = rigidbody;
		carriedItem = pickable;

		carriedObject.Enabled = true;
		carriedObject.Gravity = false;

		if ( carriedObject.PhysicsBody != null )
			carriedObject.PhysicsBody.Sleeping = false;

		// FIX 2: Сбрасываем угловую скорость при подъёме — предмет сразу стабилен
		carriedObject.AngularVelocity = Vector3.Zero;

		var allParts = new List<Rigidbody>();
		CollectRigidbodies( pickable.GameObject, allParts );
		carriedSubParts = allParts.Where( rb => rb != carriedObject ).ToList();

		foreach ( var part in carriedSubParts )
			part.Enabled = false;

		// FIX 4: Снижаем скорость ходьбы через PlayerController
		ApplyWeightSpeedPenalty( pickable.Weight );

		pickable.OnPickedUp();

		Log.Info( $"Поднят предмет: {carriedObject.GameObject.Name}, вес: {pickable.Weight}, доп. деталей: {carriedSubParts.Count}" );
	}

	private void DropObject()
	{
		if ( !isCarrying ) return;

		var (_, eyeRot) = GetEyeTransform();

		carriedObject.Enabled = true;
		carriedObject.Gravity = true;
		carriedObject.Velocity = eyeRot.Forward * 300f;
		carriedObject.AngularVelocity = Vector3.Zero;

		if ( carriedObject.PhysicsBody != null )
			carriedObject.PhysicsBody.Sleeping = false;

		foreach ( var part in carriedSubParts )
			part.Enabled = true;
		carriedSubParts.Clear();

		// FIX 4: Возвращаем нормальную скорость
		RestoreNormalSpeed();

		carriedItem?.OnDropped();

		Log.Info( $"Отпущен предмет: {carriedObject.GameObject.Name}" );

		carriedObject = null;
		carriedItem = null;
	}

	private void UpdateCarriedObject()
	{
		if ( !isCarrying || carriedObject == null ) return;

		// Проверяем, что объект ещё жив в s&box (не удалён, не деактивирован сценой).
		// Обычный null-check не ловит уничтоженные объекты — нужен IsValid().
		if ( !carriedObject.IsValid() || !carriedObject.GameObject.IsValid() )
		{
			Log.Info( "Переносимый объект был уничтожен — автоматически отпускаем" );
			ForceReleaseCarriedObject();
			return;
		}

		var (eyePos, eyeRot) = GetEyeTransform();
		var targetPos = eyePos + eyeRot.Forward * CarryDistance;

		var currentPos = carriedObject.Transform.Position;
		var posError = targetPos - currentPos;

		var desiredVelocity = posError * CarrySpeed;
		if ( desiredVelocity.Length > MaxCarryVelocity )
			desiredVelocity = desiredVelocity.Normal * MaxCarryVelocity;

		carriedObject.Velocity = desiredVelocity;

		// FIX 2: Умное гашение вращения.
		if ( !IsFreeRotating )
		{
			var angVel = carriedObject.AngularVelocity;
			float angSpeed = angVel.Length;

			float dampFactor;
			if ( angSpeed > AngularDampingThreshold )
			{
				dampFactor = MathX.Clamp( 1f - Time.Delta * (AngularDamping * 0.3f), 0f, 1f );
			}
			else
			{
				dampFactor = MathX.Clamp( 1f - Time.Delta * AngularDamping, 0f, 1f );
			}

			carriedObject.AngularVelocity = angVel * dampFactor;
		}
	}

	/// <summary>
	/// Принудительно сбрасывает состояние переноса без попытки обращаться к объекту.
	/// Используется когда объект был уничтожен снаружи пока был в руках.
	/// </summary>
	private void ForceReleaseCarriedObject()
	{
		carriedSubParts.Clear();
		RestoreNormalSpeed();
		carriedItem?.OnDropped();
		carriedObject = null;
		carriedItem = null;
		IsFreeRotating = false;
	}

	private void RotateCarriedObject()
	{
		if ( carriedObject == null ) return;

		var (_, eyeRot) = GetEyeTransform();
		var mouseDelta = Input.MouseDelta;

		var yawSpeed = -mouseDelta.x * RotationSpeed * RotationSensitivity * 0.02f;
		var pitchSpeed = -mouseDelta.y * RotationSpeed * RotationSensitivity * 0.02f;

		carriedObject.AngularVelocity = Vector3.Up * yawSpeed + eyeRot.Right * pitchSpeed;
	}

	/// <summary>
	/// Закрепляет предмет в воздухе и освобождает руки.
	/// FIX 1: Теперь помечаем предмет как IsPinned = true, чтобы при следующем Use
	/// TryPickupObject сначала его открепил, а потом поднял.
	/// </summary>
	private void PinAndReleaseObject()
	{
		var obj = carriedObject;
		var item = carriedItem;

		obj.Velocity = Vector3.Zero;
		obj.AngularVelocity = Vector3.Zero;
		obj.Gravity = false; // убрать гравитацию — предмет висит на месте
		obj.Enabled = false; // отключаем физику, предмет не движется

		// Помечаем как закреплённый — TryPickupObject включит его обратно
		item?.SetPinned( true );

		// Детали остаются отключены вместе с основным
		carriedSubParts.Clear();

		// FIX 4: Возвращаем скорость при закреплении
		RestoreNormalSpeed();

		item?.OnDropped();

		Log.Info( $"Предмет {obj.GameObject.Name} закреплён в воздухе (IsPinned=true, поднимется через Use)" );

		carriedObject = null;
		carriedItem = null;
	}

	// FIX 4: Применяем штраф к скорости через PlayerController
	private void ApplyWeightSpeedPenalty( float weight )
	{
		if ( playerController == null ) return;

		var multiplier = CalculateSpeedMultiplier( weight );
		playerController.WalkSpeed = BaseCarrySpeed * multiplier;

		Log.Info( $"Скорость ходьбы изменена на {playerController.WalkSpeed} (вес: {weight}, множитель: {multiplier:F2})" );
	}

	// FIX 4: Восстанавливаем скорость из BaseCarrySpeed (она равна нормальной скорости игрока)
	private void RestoreNormalSpeed()
	{
		if ( playerController == null ) return;

		playerController.WalkSpeed = BaseCarrySpeed;

		Log.Info( $"Скорость ходьбы восстановлена: {playerController.WalkSpeed}" );
	}

	private float CalculateSpeedMultiplier( float weight )
	{
		var normalizedWeight = MathX.Clamp( weight / MaxLiftWeight, 0f, 1f );
		return MathX.Lerp( 1f, 0.3f, normalizedWeight );
	}

	public float GetCurrentCarrySpeed()
	{
		if ( !isCarrying || carriedItem == null )
			return BaseCarrySpeed;

		return BaseCarrySpeed * CalculateSpeedMultiplier( carriedItem.Weight );
	}

	public bool IsCarrying => isCarrying;
	public bool IsPinned => false;
	public Rigidbody CarriedObject => carriedObject;
}
