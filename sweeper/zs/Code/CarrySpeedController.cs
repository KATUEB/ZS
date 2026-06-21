using Sandbox;

namespace ZS;

/// <summary>
/// Вспомогательный компонент для управления скоростью игрока в зависимости от переносимого предмета
/// Подключите этот компонент к игроку вместе с UseButton
/// </summary>
public class CarrySpeedController : Component
{
	private UseButton useButton;

	/// <summary>
	/// Базовая скорость игрока (когда ничего не носит)
	/// </summary>
	[Property]
	public float BasePlayerSpeed { get; set; } = 200f;

	protected override void OnAwake()
	{
		base.OnAwake();
		useButton = Components.Get<UseButton>();
	}

	/// <summary>
	/// Возвращает текущую скорость игрока с учетом веса переносимого предмета
	/// Используйте это значение для ограничения скорости передвижения
	/// </summary>
	public float GetCurrentSpeed()
	{
		if ( useButton == null )
			return BasePlayerSpeed;

		return useButton.GetCurrentCarrySpeed();
	}

	/// <summary>
	/// Возвращает процент от максимальной скорости
	/// Для отображения на HUD
	/// </summary>
	public float GetSpeedPercent()
	{
		var currentSpeed = GetCurrentSpeed();
		return (currentSpeed / (BasePlayerSpeed * 1.2f)) * 100f; // 120% - максимум
	}

	/// <summary>
	/// Проверяет, несет ли игрок предмет
	/// </summary>
	public bool IsCarrying => useButton != null && useButton.IsCarrying;

	/// <summary>
	/// Проверяет, закреплен ли переносимый предмет
	/// </summary>
	public bool IsPinned => useButton != null && useButton.IsPinned;
}
