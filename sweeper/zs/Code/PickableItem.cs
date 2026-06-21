using Sandbox;
using System.Linq;

namespace ZS;

/// <summary>
/// Компонент для предметов, которые можно поднять и переносить.
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

	/// <summary>
	/// FIX 3: Автоматически добавлять PickableItem дочерним объектам с Rigidbody.
	/// Нужно для составных пропов — при распаде дочерние объекты станут подъёмными сами по себе.
	/// Вес дочерних объектов будет Weight * ChildWeightFraction.
	/// </summary>
	[Property]
	public bool PropagateToChildren { get; set; } = true;

	/// <summary>
	/// Доля веса для дочерних объектов при распаде (например 0.5 = половина от веса родителя).
	/// </summary>
	[Property]
	public float ChildWeightFraction { get; set; } = 0.5f;

	private bool isCarried = false;

	// FIX 1: Флаг закреплённости в воздухе.
	// Когда IsPinned = true — Rigidbody выключен, объект висит неподвижно.
	// TryPickupObject видит этот флаг и сначала включает Rigidbody обратно, потом поднимает.
	private bool isPinned = false;

	public bool IsPinned => isPinned;

	public void SetPinned( bool value )
	{
		isPinned = value;
	}

	protected override void OnAwake()
	{
		base.OnAwake();

		var rigidbody = Components.Get<Rigidbody>();
		if ( rigidbody == null )
		{
			rigidbody = Components.Create<Rigidbody>();
		}

		var colliders = Components.GetAll<Collider>().ToList();
		if ( colliders.Count == 0 )
		{
			Log.Warning( $"У предмета {GameObject.Name} нет коллайдеров! Добавьте BoxCollider или CapsuleCollider." );
		}

		// FIX 3: Добавляем PickableItem дочерним объектам с Rigidbody, если их нет
		if ( PropagateToChildren )
		{
			EnsureChildrenHavePickable( GameObject );
		}
	}

	/// <summary>
	/// FIX 3: Рекурсивно обходит дочерние объекты.
	/// Если у дочернего объекта есть Rigidbody, но нет PickableItem — добавляет его.
	/// Это гарантирует, что при распаде составного пропа на части каждая часть
	/// останется подъёмной, не теряя компонент.
	/// Вызывается только на дочерних объектах (не на самом себе — чтобы не зациклиться).
	/// </summary>
	private void EnsureChildrenHavePickable( GameObject root )
	{
		foreach ( var child in root.Children )
		{
			var childRb = child.Components.Get<Rigidbody>();
			if ( childRb != null )
			{
				var existingPickable = child.Components.Get<PickableItem>();
				if ( existingPickable == null )
				{
					var newPickable = child.Components.Create<PickableItem>();
					newPickable.Weight = Weight * ChildWeightFraction;
					newPickable.UseGlowEffectWhenCarried = UseGlowEffectWhenCarried;
					// Отключаем рекурсию у дочерних, чтобы не плодить бесконечно
					newPickable.PropagateToChildren = false;

					Log.Info( $"PickableItem добавлен дочернему объекту: {child.Name} (вес: {newPickable.Weight})" );
				}
			}

			// Спускаемся глубже — для пропов с несколькими уровнями иерархии
			EnsureChildrenHavePickable( child );
		}
	}

	public void OnPickedUp( GameObject picker )
	{
		isCarried = true;
		isPinned = false; // FIX 1: При подъёме сбрасываем флаг закреплённости

		if ( UseGlowEffectWhenCarried )
		{
			ApplyCarryEffect();
		}

		Log.Info( $"Предмет {GameObject.Name} поднят игроком {picker?.Name ?? "(неизвестный)"} (вес: {Weight})" );
	}

	public void OnDropped( GameObject picker )
	{
		isCarried = false;

		if ( UseGlowEffectWhenCarried )
		{
			RemoveCarryEffect();
		}

		Log.Info( $"Предмет {GameObject.Name} отпущен игроком {picker?.Name ?? "(неизвестный)"}" );
	}

	private void ApplyCarryEffect()
	{
		// Здесь можно добавить визуальные эффекты: свечение, изменение материала и т.д.
	}

	private void RemoveCarryEffect()
	{
		// Убираем визуальные эффекты
	}

	public float GetWeightPercent( float maxWeight )
	{
		return (Weight / maxWeight) * 100f;
	}

	public bool IsCarried => isCarried;
}
