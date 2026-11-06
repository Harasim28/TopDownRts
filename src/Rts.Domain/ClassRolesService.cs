namespace Rts.Domain;

/// <summary>
/// Спец-роли классов как необязательная политика полного хода (дефолт сессий — null, всё выключено):
/// — support лечит ЖИВЫХ союзников (включая себя) в радиусе SupportRadius: +SupportHealPerSecond
///   корпуса в секунду, выше MaxHull не лечит;
/// — supply дозаправляет живых союзников (включая себя) в радиусе SupplyRadius:
///   +SupplyFuelPerSecond топлива в секунду СВЕРХ обычного регена, выше бака не переливает;
/// — scout уклонение: ПОЛУЧАЕМЫЙ scout-ом урон выстрела × ScoutDamageMultiplier
///   (реализуется как IShotDamageModifier — модификатор урона в такте боя CombatService).
/// Ауры применяются сессией после движения. Мёртвые корабли ни лечат, ни заправляют; врагам
/// ауры не достаются. Несколько аур на одну цель — складываются. Модификатор уклонения не зависит
/// ни от класса стрелка, ни от дистанции — только от класса ПОЛУЧАЮЩЕЙ цели.
/// </summary>
public sealed class ClassRolesService : IClassRolesPolicy, IShotDamageModifier
{
    private readonly ClassRolesConfig _config;

    public ClassRolesService(ClassRolesConfig? config = null) => _config = config ?? ClassRolesConfig.Default();

    public double ShotMultiplierOf(PlayerId attacker, ShipUnit attackerShip, PlayerId target, ShipUnit targetShip) =>
        targetShip.Class == _config.ScoutClass ? _config.ScoutDamageMultiplier : 1.0;

    public ClassRolesTickResult Tick(ClassRolesTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(tick.Units);
        if (tick.Span <= TimeSpan.Zero || tick.Units.Count == 0)
        {
            return new ClassRolesTickResult([]); // пустой тик или пустой спавн — аур нет
        }

        var seconds = tick.Span.TotalSeconds;
        var units = tick.Units.ToDictionary(unit => new MoveKey(unit.Player, unit.Ship));

        // накопители эффектов по паре (владелец, корабль): несколько аур на одну цель складываются
        var effects = new Dictionary<MoveKey, (double Heal, double Refuel)>();
        foreach (var carrier in tick.Units.Where(unit => unit.Alive))
        {
            var isSupport = carrier.ShipClass == _config.SupportClass;
            var isSupply = carrier.ShipClass == _config.SupplyClass;
            if (!isSupport && !isSupply)
            {
                continue; // ауры только у поддержки и снабженца
            }

            foreach (var target in tick.Units.Where(unit => unit.Alive && unit.Team == carrier.Team))
            {
                var radius = isSupport ? _config.SupportRadius : _config.SupplyRadius;
                if (carrier.Position.DistanceTo(target.Position) > radius)
                {
                    continue; // вне радиуса ауры (враг отсеян выше — только союзники)
                }

                var key = new MoveKey(target.Player, target.Ship);
                var (heal, refuel) = effects.GetValueOrDefault(key);
                effects[key] = (heal + (isSupport ? _config.SupportHealPerSecond * seconds : 0),
                                refuel + (isSupply ? _config.SupplyFuelPerSecond * seconds : 0));
            }
        }

        var applied = new List<ClassRolesDelta>();
        foreach (var (key, (healSum, refuelSum)) in effects)
        {
            var unit = units[key];
            var healed = (int)Math.Round(Math.Min(healSum, unit.MaxHull - unit.Hull), MidpointRounding.AwayFromZero);
            var refueled = Math.Min(refuelSum, unit.FuelCapacity - unit.Fuel);
            if (healed > 0 || refueled > 0)
            {
                applied.Add(new ClassRolesDelta(key.Owner, key.Ship, Math.Max(0, healed), Math.Max(0, refueled)));
            }
        }

        return new ClassRolesTickResult(applied);
    }
}
