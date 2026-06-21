using Sandbox;

namespace ZS;

/// <summary>
/// Вспомогательный компонент для управления скоростью игрока в зависимости от переносимого предмета.
/// Подключите этот компонент к игроку вместе с UseButton.
/// 
/// ВАЖНО (FIX 4): Теперь основная логика изменения WalkSpeed находится прямо в UseButton —
/// он напрямую меняет PlayerController.WalkSpeed при подъёме и возвращает при опускании.
/// Этот компонент оставлен для HUD и внешних запросов текущей скорости.
/// </summary>
public class CarrySpeedController : Component
{
	private UseButton useButton;

	/// <summary>
	/// Базовая скорость игрока (когда ничего не носит).
	/// Должна совпадать с BaseCarrySpeed в UseButton и Walk Speed в PlayerController.
	/// </summary>
	[Property]
	public float BasePlayerSpeed { get; set; } = 200f;

	protected override void OnAwake()
	{
		base.OnAwake();
		useButton = Components.Get<UseButton>();
	}

	/// <summary>
	/// Возвращает текущую скорость игрока с учетом веса переносимого предмета.
	/// Используйте это значение для HUD.
	/// </summary>
	public float GetCurrentSpeed()
	{
		if ( useButton == null )
			return BasePlayerSpeed;

		return useButton.GetCurrentCarrySpeed();
	}

	/// <summary>
	/// Возвращает процент от максимальной скорости (для HUD).
	/// </summary>
	public float GetSpeedPercent()
	{
		var currentSpeed = GetCurrentSpeed();
		return (currentSpeed / BasePlayerSpeed) * 100f;
	}

	/// <summary>
	/// Проверяет, несет ли игрок предмет
	/// </summary>
	public bool IsCarrying => useButton != null && useButton.IsCarrying;

	/// <summary>
	/// Проверяет, закреплен ли предмет (IsPinned в UseButton всегда false пока держишь,
	/// свойство оставлено для совместимости)
	/// </summary>
	public bool IsPinned => useButton != null && useButton.IsPinned;
}
