using Cultivation.Domain;
using Cultivation.AI;

namespace Cultivation.Simulation;

public static class BoardBattleScenario
{
    public static WorldState Create(string name = "行者", GameConfiguration? configuration = null)
    {
        var config = configuration ?? GameConfiguration.Find();
        var w = DemoWorld.Create(configuration: config);
        w.Characters = config.Read<List<CharacterState>>("characters/board-battle.json");
        int player = w.Characters.FindIndex(c => c.Id == "player");
        if (player < 0 || string.IsNullOrWhiteSpace(name) || name.Length > 24 || name.Any(char.IsControl)) throw new ArgumentException("Invalid player name");
        w.Characters[player] = w.Characters[player] with { Name = name.Trim() };
        w.BoardRules = config.Read<BoardBattleRules>("combat/board.json");
        w.BoardBattle = new() { Fighters = config.Read<BoardScenarioSettings>("combat/formation.json").Fighters };
        WorldIntegrity.Validate(w); return w;
    }
}

internal static class BoardBattleActions
{
    internal static bool Participant(WorldState w, string id) => w.BoardBattle is { Phase: BoardBattlePhase.Active } b && b.Fighters.Any(f => f.ActorId == id);
    internal static string? Validate(WorldState w, ActionIntent i)
    {
        if (w.BoardBattle is not { Phase: BoardBattlePhase.Active } b || i.BoardTurn is not { } order ||
            i.TargetId != b.Id || order.BattleId != b.Id || order.Round != b.Round + 1 || i.ResourceId != "board-turn" ||
            i.Quantity != 1 || i.DurationTicks != 0 || i.Plan is not null || i.MissionId is not null) return "Invalid board order or round";
        var actor = b.Fighters.SingleOrDefault(f => f.ActorId == i.ActorId);
        if (actor is null || b.Orders.Any(o => o.ActorId == i.ActorId)) return "Not a combatant or turn already submitted";
        if (order.MoveTo is { } cell && !BoardBattleGeometry.CanMove(actor, cell, b.Fighters)) return "Illegal move or occupied cell";
        if (order.AttackTargetId is { } target && !b.Fighters.Any(f => f.ActorId == target && f.Side != actor.Side && w.Characters.Single(c => c.Id == target).Health > 0)) return "Invalid enemy target";
        return null;
    }
    internal static string? ValidateAutoCombat(WorldState w, ActionIntent i) =>
        w.BoardBattle is not { Phase: BoardBattlePhase.Active } b || i.Source != DecisionSource.Player ||
        i.ActorId != w.ControlledCharacterId || !b.Fighters.Any(f => f.ActorId == i.ActorId) || i.TargetId != b.Id ||
        i.ResourceId != "auto-combat" || i.Quantity is < 0 or > 1 || i.DurationTicks != 0 || i.Plan is not null ||
        i.MissionId is not null || i.BoardTurn is not null ? "Invalid player auto combat setting" : null;
    internal static void QueuePlayerAuto(WorldState w)
    {
        if (w.BoardBattle is not { Phase: BoardBattlePhase.Active, PlayerAutoCombat: true } b ||
            b.Orders.Any(o => o.ActorId == w.ControlledCharacterId) ||
            !b.Fighters.Any(f => f.ActorId == w.ControlledCharacterId) || w.Characters.Single(c => c.Id == w.ControlledCharacterId).Health == 0) return;
        string id = $"board-player-auto:{b.Id}:{w.ControlledCharacterId}:{b.Round + 1}";
        var order = BoardBattlePlanner.Choose(b, w.ControlledCharacterId, w.Characters);
        var intent = new ActionIntent(id, w.ControlledCharacterId, b.Id, "board-turn", 1, 0, DecisionSource.Player,
            ActionKind.BoardTurn, BoardTurn: order);
        if (w.ProcessedIntentIds.Contains(id) || ActionValidator.Validate(w, intent) is not null) return;
        w.ProcessedIntentIds.Add(id); ActionExecutor.Execute(w, intent);
        ActionExecutor.Emit(w, id, "Decision", "玩家授權自動戰鬥：依可見站位選擇移動與普攻，手動當輪指令優先");
    }    internal static void QueueNpc(WorldState w)
    {
        if (w.BoardBattle is not { Phase: BoardBattlePhase.Active } b) return;
        foreach (var f in b.Fighters.Where(f => f.ActorId != w.ControlledCharacterId).OrderBy(f => f.ActorId, StringComparer.Ordinal))
        {
            if (w.Characters.Single(c => c.Id == f.ActorId).Health == 0 || b.Orders.Any(o => o.ActorId == f.ActorId) || w.Pending.Any(p => p.Intent.ActorId == f.ActorId)) continue;
            var order = BoardBattlePlanner.Choose(b, f.ActorId, w.Characters);
            var intent = new ActionIntent($"board-ai:{b.Id}:{f.ActorId}:{b.Round + 1}", f.ActorId, b.Id, "board-turn", 1, 0, DecisionSource.Npc,
                ActionKind.BoardTurn, "依可見站位與生命評估合法射程，先移動後攻擊", BoardTurn: order);
            w.Pending.Add(new(w.NextSequence++, intent));
        }
    }
    internal static void Advance(WorldState w)
    {
        if (w.BoardBattle is not { Phase: BoardBattlePhase.Active } b) return;
        CharacterState Actor(string id) => w.Characters.Single(c => c.Id == id);
        bool Finish(bool roundEnd = false)
        {
            if (b.Phase == BoardBattlePhase.Finished) return true;
            var sides = b.Fighters.Where(f => Actor(f.ActorId).Health > 0).Select(f => f.Side).Distinct().ToArray();
            if (sides.Length == 2 && (!roundEnd || b.Round < w.BoardRules!.MaximumRounds)) return false;
            b.Phase = BoardBattlePhase.Finished; b.Winner = sides.Length == 1 ? sides[0] : null;
            b.Orders.Clear(); ActionExecutor.Emit(w, b.Id, "BoardBattleEnded", b.Winner?.ToString() ?? "Draw"); return true;
        }
        if (Finish(true)) return;
        b.Round++;
        b.LastInitiative = b.Fighters.Where(f => Actor(f.ActorId).Health > 0).OrderByDescending(f => f.Speed)
            .ThenBy(f => f.ActorId, StringComparer.Ordinal).Select(f => f.ActorId).ToList();
        foreach (string id in b.LastInitiative)
        {
            if (Actor(id).Health == 0) continue;
            var actor = b.Fighters.Single(f => f.ActorId == id);
            var order = b.Orders.SingleOrDefault(o => o.ActorId == id)?.Order;
            ActionExecutor.Emit(w, b.Id, "BoardTurnStarted", $"{b.Round}:{id}");
            if (order?.MoveTo is { } cell)
            {
                if (BoardBattleGeometry.CanMove(actor, cell, b.Fighters))
                {
                    actor = actor with { Cell = cell }; b.Fighters[b.Fighters.FindIndex(f => f.ActorId == id)] = actor;
                    ActionExecutor.Emit(w, b.Id, "BoardMoved", $"{id}:{cell.Row},{cell.Depth}");
                }
                else ActionExecutor.Emit(w, b.Id, "BoardMoveRejected", id);
            }
            if (order?.AttackTargetId is { } targetId && w.LifeSystem?.CombatUses.Any(u => u.BattleId == b.Id && u.ActorId == id && u.Round == b.Round) != true)
            {
                var enemies = b.Fighters.Where(f => f.Side != actor.Side && Actor(f.ActorId).Health > 0).ToArray();
                var target = enemies.SingleOrDefault(f => f.ActorId == targetId);
                if (target is not null && BoardBattleGeometry.CanTarget(actor, target, enemies))
                {
                    var attack = CharacterProgression.Stats(Actor(id), w.Advancement); var defense = CharacterProgression.Stats(Actor(targetId), w.Advancement);
                    int damage = (int)Math.Clamp((long)attack.Attack - defense.Defense, 1, 100);
                    int index = w.Characters.FindIndex(c => c.Id == targetId);
                    w.Characters[index] = Actor(targetId) with { Health = Math.Max(0, Actor(targetId).Health - damage) };
                    EquipmentDurabilityActions.CombatHit(w,b.Id,id,targetId,damage);
                    ActionExecutor.Emit(w, b.Id, "BoardAttacked", $"{id}->{targetId}:{damage}");
                }
                else ActionExecutor.Emit(w, b.Id, "BoardAttackRejected", $"{id}->{targetId}:目標已失能或不符當下射程");
            }
            if (Finish()) break;
        }
        if (b.Phase == BoardBattlePhase.Active) LifeActions.Effects(w, b.Id, b.Round);
        Finish(true);
        b.Orders.Clear();
    }
}




