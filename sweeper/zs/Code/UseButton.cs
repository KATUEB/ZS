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
	/// Насколько сильно гасится паразитное вращение от столкновений, когда предмет
	/// не вращается вручную. 0 = крутится от ударов сколько угодно (максимально "реалистично",
	/// но может бесконтрольно завертеться), большое значение = почти сразу стабилизируется.
	/// </summary>
	[Property]
	public float AngularDamping { get; set; } = 2f;

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

	/// <summary>
	/// True, пока игрок держит кнопку свободного вращения. Подключи проверку этого свойства
	/// в своём скрипте обзора камеры: если true — не крути камеру мышью, отдай дельту сюда.
	/// </summary>
	public bool IsFreeRotating { get; private set; }

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
	/// Нужно для составных пропов (например из облака), где луч попадает в child-коллайдер,
	/// а PickableItem/Rigidbody висят на корневом объекте модели.
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
	/// Собирает ВСЕ Rigidbody во всей иерархии пропа (сам объект + все дети рекурсивно).
	/// Нужно для составных моделей, у которых отдельные детали (например крышка ящика)
	/// имеют собственный Rigidbody - иначе при подъёме они остаются самостоятельными
	/// физическими телами и разлетаются от основного объекта.
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
		if ( Input.Pressed( "use" ) )
		{
			Log.Info( $"Use нажата. Несу: {isCarrying}" );

			if ( isCarrying )
				DropObject();
			else
				TryPickupObject();
		}

		// Режим свободного вращения - удерживаешь кнопку, камера должна "замереть",
		// а мышь крутит сам предмет.
		// Input.Down падает с исключением, если такого Input Action ещё нет в проекте -
		// оборачиваем в try/catch, чтобы это не валило весь Update, пока действие не добавлено
		// в Project Settings -> Input.
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

		// Secondary Attack - поставить предмет на месте навсегда и отпустить руки
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
			.IgnoreGameObjectHierarchy( GameObject ) // не цепляем самого игрока
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

			// Rigidbody берём с того же объекта, где PickableItem - так гарантированно
			// двигаем именно ту иерархию, на которой висит сам предмет (и его визуал),
			// а не случайный коллизионный кусок составной модели.
			var rigidbody = pickable.GameObject.Components.Get<Rigidbody>();
			if ( rigidbody == null )
			{
				Log.Info( $"  -> На {pickable.GameObject.Name} нет Rigidbody" );
				continue;
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

		// Физику НЕ выключаем - предмет остаётся динамическим, чтобы реально
		// толкаться/прокручиваться от столкновений. Убираем только гравитацию,
		// чтобы он не "проседал" пока висит перед камерой.
		carriedObject.Enabled = true; // защита на случай, если остался выключен от старого бага
		carriedObject.Gravity = false;

		// На случай, если тело "спит" - явно разбудим, иначе оно может зависнуть
		// и не реагировать ни на Velocity, ни на гравитацию после броска.
		if ( carriedObject.PhysicsBody != null )
			carriedObject.PhysicsBody.Sleeping = false;

		// Если у пропа есть отдельные физические детали (например крышка ящика,
		// у которой свой Rigidbody) - на время переноса отключаем их физику.
		// Раз они дети основного объекта, их Transform всё равно будет следовать
		// за родителем автоматически, и весь проп будет двигаться как единое целое.
		var allParts = new List<Rigidbody>();
		CollectRigidbodies( pickable.GameObject, allParts );
		carriedSubParts = allParts.Where( rb => rb != carriedObject ).ToList();

		foreach ( var part in carriedSubParts )
			part.Enabled = false;

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

		carriedItem?.OnDropped();

		Log.Info( $"Отпущен предмет: {carriedObject.GameObject.Name}" );

		carriedObject = null;
		carriedItem = null;
	}

	private void UpdateCarriedObject()
	{
		if ( !isCarrying || carriedObject == null ) return;

		var (eyePos, eyeRot) = GetEyeTransform();
		var targetPos = eyePos + eyeRot.Forward * CarryDistance;

		var currentPos = carriedObject.Transform.Position;
		var posError = targetPos - currentPos;

		// Двигаем предмет через скорость (а не телепортом Transform.Position) -
		// тогда реальные столкновения со стенами/углами решает физика сама,
		// и предмет скользит/прокручивается, а не просто упирается.
		var desiredVelocity = posError * CarrySpeed;
		if ( desiredVelocity.Length > MaxCarryVelocity )
			desiredVelocity = desiredVelocity.Normal * MaxCarryVelocity;

		carriedObject.Velocity = desiredVelocity;

		// Если не крутим вручную - просто слегка гасим вращение, не убивая его полностью,
		// чтобы удар об угол всё ещё мог провернуть предмет, но не закручивал его навечно.
		if ( !IsFreeRotating )
			carriedObject.AngularVelocity *= MathX.Clamp( 1f - Time.Delta * AngularDamping, 0f, 1f );
	}

	/// <summary>
	/// Свободное вращение предмета мышью (вызывается только пока зажата FreeRotateAction).
	/// Крутим через AngularVelocity, а не напрямую через Transform.Rotation,
	/// чтобы не конфликтовать с физическим решением столкновений.
	/// </summary>
	private void RotateCarriedObject()
	{
		if ( carriedObject == null ) return;

		var (_, eyeRot) = GetEyeTransform();
		var mouseDelta = Input.MouseDelta;

		var yawSpeed = -mouseDelta.x * RotationSpeed * RotationSensitivity * 0.02f;
		var pitchSpeed = -mouseDelta.y * RotationSpeed * RotationSensitivity * 0.02f;

		carriedObject.AngularVelocity = Vector3.Up * yawSpeed + eyeRot.Right * pitchSpeed;

		Log.Info( $"Вращаю предмет (свободно). Дельта мыши: {mouseDelta}" );
	}

	/// <summary>
	/// Ставит текущий предмет на месте навсегда и отпускает руки -
	/// предмет больше не привязан к UseButton, просто висит в мире с выключенной физикой
	/// до тех пор, пока кто-нибудь не подойдёт и не подберёт его снова через Use
	/// (TryPickupObject обработает это как обычный подбор).
	/// </summary>
	private void PinAndReleaseObject()
	{
		var obj = carriedObject;
		var item = carriedItem;

		obj.Velocity = Vector3.Zero;
		obj.AngularVelocity = Vector3.Zero;
		obj.Enabled = false; // полностью отключаем физику - больше никто его не двигает

		// Детали (например крышка) остаются отключены вместе с основным объектом -
		// весь проп так и висит как единое целое, пока его не подберут заново.
		carriedSubParts.Clear();

		item?.OnDropped();

		Log.Info( $"Предмет {obj.GameObject.Name} закреплен в воздухе (навсегда, до повторного подбора)" );

		// Освобождаем руки - можно сразу поднимать что-то другое,
		// закреплённый предмет от этого никуда не денется
		carriedObject = null;
		carriedItem = null;
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

	/// <summary>
	/// Закреплённый предмет больше не привязан к UseButton (руки свободны),
	/// поэтому пока что-то держишь в руках - оно никогда "запинено".
	/// Свойство оставлено для совместимости с CarrySpeedController.
	/// </summary>
	public bool IsPinned => false;
	public Rigidbody CarriedObject => carriedObject;
}
