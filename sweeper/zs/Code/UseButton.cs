using Sandbox;

namespace ZS;

/// <summary>
/// Система взаимодействия с предметами: поднятие, вращение, перетаскивание и закрепление в воздухе
/// </summary>
public class UseButton : Component
{
	/// <summary>
	/// Максимальное расстояние для поднятия предмета
	/// </summary>
	[Property]
	public float PickupRange { get; set; } = 100f;

	/// <summary>
	/// Расстояние, на котором предмет держится от игрока
	/// </summary>
	[Property]
	public float CarryDistance { get; set; } = 50f;

	/// <summary>
	/// Базовая скорость передвижения с предметом
	/// </summary>
	[Property]
	public float BaseCarrySpeed { get; set; } = 200f;

	/// <summary>
	/// Скорость вращения поднятого предмета (градусы в секунду)
	/// </summary>
	[Property]
	public float RotationSpeed { get; set; } = 180f;

	/// <summary>
	/// Чувствительность мыши для вращения
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
	/// Именно от него считается направление поднятия/удержания предмета,
	/// а не от поворота тела игрока (тело обычно не наклоняется по pitch).
	/// </summary>
	[Property]
	public GameObject CameraObject { get; set; }

	private Rigidbody carriedObject;
	private PickableItem carriedItem;
	private bool isPinned = false;
	private Vector3 pinnedPosition;
	private bool isCarrying => carriedObject != null;

	/// <summary>
	/// Позиция и поворот камеры/глаз игрока.
	/// Используется вместо Transform тела, чтобы предмет реально следовал за камерой (включая взгляд вверх/вниз).
	/// </summary>
	private (Vector3 Position, Rotation Rotation) GetEyeTransform()
	{
		if ( CameraObject != null )
			return (CameraObject.Transform.Position, CameraObject.Transform.Rotation);

		if ( Scene.Camera != null )
			return (Scene.Camera.Transform.Position, Scene.Camera.Transform.Rotation);

		// Фоллбэк, если камера не найдена — хотя бы тело игрока
		return (Transform.Position, Transform.Rotation);
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
		// Кнопка Use - поднять или отпустить предмет
		if ( Input.Pressed( "use" ) )
		{
			Log.Info( $"Use нажата. Несу: {isCarrying}" );
			
			if ( isCarrying )
			{
				DropObject();
			}
			else
			{
				TryPickupObject();
			}
		}

		// Вращение предмета мышью (если он поднят)
		if ( isCarrying && !isPinned )
		{
			RotateCarriedObject();
		}

		// Secondary Attack - закрепить предмет в воздухе
		if ( Input.Pressed( "attack2" ) && isCarrying )
		{
			TogglePinned();
		}
	}

	private void TryPickupObject()
	{
		var (eyePos, eyeRot) = GetEyeTransform();

		var forward = eyeRot.Forward;
		var startPos = eyePos;
		var endPos = startPos + forward * PickupRange;

		Log.Info( $"Ищу предметы от {startPos} до {endPos}" );

		// Sphere cast для поиска объектов (игнорируем самого игрока, иначе луч сразу упрётся в собственный коллайдер)
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

			var pickable = hitObject.Components.Get<PickableItem>();
			if ( pickable == null )
			{
				Log.Info( $"  -> Нет PickableItem" );
				continue;
			}

			if ( pickable.Weight > MaxLiftWeight )
			{
				Log.Info( $"  -> Вес {pickable.Weight} > {MaxLiftWeight}" );
				continue;
			}

			var rigidbody = hitObject.Components.Get<Rigidbody>();
			var distance = (hitObject.Transform.Position - startPos).Length;

			Log.Info( $"  -> Можно поднять! Расстояние: {distance}" );

			if ( rigidbody != null && distance < closestDistance )
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
		isPinned = false;

		// Отключаем физику для переносимого объекта
		rigidbody.Enabled = false;

		if ( pickable != null )
		{
			pickable.OnPickedUp();
		}

		Log.Info( $"Поднят предмет: {carriedObject.GameObject.Name}, вес: {pickable?.Weight ?? 0}" );
	}

	private void DropObject()
	{
		if ( !isCarrying ) return;

		// Включаем физику обратно
		carriedObject.Enabled = true;

		// Устанавливаем начальную скорость в направлении взгляда камеры
		var (_, eyeRot) = GetEyeTransform();
		carriedObject.Velocity = eyeRot.Forward * 300f;

		if ( carriedItem != null )
		{
			carriedItem.OnDropped();
		}

		Log.Info( $"Отпущен предмет: {carriedObject.GameObject.Name}" );

		carriedObject = null;
		carriedItem = null;
		isPinned = false;
	}

	private void UpdateCarriedObject()
	{
		if ( !isCarrying || carriedObject == null ) return;

		var (eyePos, eyeRot) = GetEyeTransform();

		// Позиция перед камерой
		var targetPos = eyePos + eyeRot.Forward * CarryDistance;

		if ( isPinned )
		{
			// Закреплено - удерживаем на месте
			carriedObject.Transform.Position = pinnedPosition;
			carriedObject.Velocity = Vector3.Zero;
		}
		else
		{
			// Проверяем столкновения на пути к целевой позиции
			var currentPos = carriedObject.Transform.Position;
			var direction = (targetPos - currentPos).Normal;
			var distance = (targetPos - currentPos).Length;

			// Трейс от текущей позиции к целевой
			if ( distance > 0.1f )
			{
				var trace = Scene.Trace
					.Ray( currentPos, targetPos )
					.Radius( 20f ) // Размер объекта
					.IgnoreGameObjectHierarchy( carriedObject.GameObject ) // иначе луч сразу бьёт в собственный коллайдер предмета
					.IgnoreGameObjectHierarchy( GameObject ) // и в коллайдер игрока
					.Run();

				if ( trace.Hit && trace.Distance < distance )
				{
					// Есть препятствие - останавливаем перед ним
					var safeDistance = trace.Distance > 5f ? trace.Distance - 5f : 0f;
					targetPos = currentPos + direction * safeDistance;
				}
			}

			// Обновляем позицию ПРЯМО в каждом кадре
			carriedObject.Transform.Position = targetPos;
			carriedObject.Velocity = Vector3.Zero;
		}
	}

	private void RotateCarriedObject()
	{
		if ( !isCarrying || carriedObject == null ) return;

		var mouseDelta = Input.MouseDelta;
		var rotationAmount = RotationSpeed * Time.Delta * RotationSensitivity;

		// Вращение по оси Y (горизонтальное)
		var yawRotation = Rotation.FromAxis( Vector3.Up, -mouseDelta.x * rotationAmount * 0.01f );
		
		// Вращение по оси X (вертикальное)
		var pitchRotation = Rotation.FromAxis( Vector3.Right, -mouseDelta.y * rotationAmount * 0.01f );

		carriedObject.Transform.Rotation = carriedObject.Transform.Rotation * yawRotation * pitchRotation;

		Log.Info( $"Вращаю предмет. Дельта мыши: {mouseDelta}" );
	}

	private void TogglePinned()
	{
		if ( !isCarrying ) return;

		isPinned = !isPinned;

		if ( isPinned )
		{
			pinnedPosition = carriedObject.Transform.Position;
			Log.Info( "Предмет закреплен в воздухе" );
		}
		else
		{
			Log.Info( "Предмет освобожден" );
		}
	}

	/// <summary>
	/// Рассчитывает множитель скорости в зависимости от веса предмета
	/// Легкие предметы = быстрое движение
	/// Тяжелые предметы = медленное движение
	/// </summary>
	private float CalculateSpeedMultiplier( float weight )
	{
		// Вес нормализуется от 0 до MaxLiftWeight
		// Максимальная скорость при весе 0 = 1.0x
		// Минимальная скорость при весе MaxLiftWeight = 0.3x
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
	public bool IsPinned => isPinned;
	public Rigidbody CarriedObject => carriedObject;
}
