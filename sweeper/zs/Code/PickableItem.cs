using Sandbox;
using System.Linq;

namespace ZS;

/// <summary>
/// Компонент для предметов, которые можно поднять и переносить
/// </summary>
public class PickableItem : Component
{
	/// <summary>
	/// Вес предмета (влияет на скорость передвижения и возможность поднятия)
	/// </summary>
	[Property]
	public float Weight { get; set; } = 10f;

	/// <summary>
	/// Визуальный эффект при поднятии
	/// </summary>
	[Property]
	public bool UseGlowEffectWhenCarried { get; set; } = true;

	private bool isCarried = false;

	protected override void OnAwake()
	{
		base.OnAwake();

		// Убедимся, что у предмета есть Rigidbody
		var rigidbody = Components.Get<Rigidbody>();
		if ( rigidbody == null )
		{
			rigidbody = Components.Create<Rigidbody>();
		}

		// Убедимся, что есть коллайдер для физики
		var colliders = Components.GetAll<Collider>().ToList();
		if ( colliders.Count == 0 )
		{
			Log.Warning( $"У предмета {GameObject.Name} нет коллайдеров! Добавьте BoxCollider или CapsuleCollider." );
		}
	}

	public void OnPickedUp( GameObject picker )
	{
		isCarried = true;

		// Применяем визуальный эффект
		if ( UseGlowEffectWhenCarried )
		{
			ApplyCarryEffect();
		}

		Log.Info( $"Предмет {GameObject.Name} поднят игроком {picker?.Name ?? "(неизвестный)"} (вес: {Weight})" );
	}

	public void OnDropped( GameObject picker )
	{
		isCarried = false;

		// Убираем визуальный эффект
		if ( UseGlowEffectWhenCarried )
		{
			RemoveCarryEffect();
		}

		Log.Info( $"Предмет {GameObject.Name} отпущен игроком {picker?.Name ?? "(неизвестный)"}" );
	}

	private void ApplyCarryEffect()
	{
		// Здесь можно добавить визуальные эффекты: свечение, изменение материала и т.д.
		// Для S&Box добавьте свои эффекты здесь
	}

	private void RemoveCarryEffect()
	{
		// Убираем визуальные эффекты
	}

	/// <summary>
	/// Возвращает процент веса от максимально допустимого
	/// </summary>
	public float GetWeightPercent( float maxWeight )
	{
		return (Weight / maxWeight) * 100f;
	}

	public bool IsCarried => isCarried;
}
