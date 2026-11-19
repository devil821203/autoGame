using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class WorldPeriodEvents
{
	// Only explicitly enabled seconds-based worlds use independent periodic events.
	internal static bool Enabled(WorldState w) => w.Time?.DiscreteEvents?.IndependentWorldEvents == true;

	internal static void ProjectClock(WorldState w)
	{
		if (Enabled(w))
		{
			w.Clock = w.Clock with
			{
				Tick = w.Time!.DiscreteEvents!.WorldTime / DiscreteTime.SecondsPerStep(w)
			};
			TimeActions.Refresh(w);
			w.Memories.RemoveAll((ActionMemory m) => m.Tick < w.Clock.Tick - w.Rules.MemoryTicks);
		}
	}

	internal static void EnableFresh(WorldState w)
	{
		DiscreteEventState discreteEvents = w.Time!.DiscreteEvents!;
		if (discreteEvents.WorldTime != 0)
		{
			throw new InvalidOperationException("World periods require a fresh world");
		}
		discreteEvents.Pending.RemoveAll((SimulationEvent e) => e.Kind == SimulationEventKind.CompatibilityStep);
		discreteEvents.Versions.Remove("$compatibility");
		discreteEvents.IndependentWorldEvents = true;
		Sync(w, new EventScheduler(discreteEvents));
	}

	internal static void Sync(WorldState w, EventScheduler queue)
	{
		if (!Enabled(w))
		{
			return;
		}
		DiscreteEventState events = w.Time!.DiscreteEvents!;
		long step = DiscreteTime.SecondsPerStep(w);
		checked
		{
			long interval = w.Time!.Rules.TicksPerDay * step;
			Register("$calendar-day", SimulationEventKind.CalendarDay, Next(interval));
			Register("$settlement-day", SimulationEventKind.SettlementSettlement, Next(interval));
			SettlementOperationRules? settlementOperationRules = w.SettlementOperationRules;
			if ((settlementOperationRules is null || !settlementOperationRules.FixedDailySettlement) && w.SettlementRules is not null)
			{
				long[] array = w.Settlements.SelectMany((SettlementState s) => from b in s.Buildings.Where((SettlementBuilding b) => b.EmployeeCount > 0 || SettlementOperations.Automatic(w, s, b)).Where((SettlementBuilding b) =>
					{
						bool requiresEmployees = w.SettlementBuildingRules!.Single((SettlementBuildingRule r) => r.Kind == b.Kind).RequiresEmployees;
						bool flag = requiresEmployees;
						if (flag)
						{
							SettlementBuildingKind kind = b.Kind;
							bool flag2 = unchecked((uint)(kind - 5)) <= 1u;
							flag = !flag2;
						}
						return flag;
					})
					select (b.PaidThroughTick + w.Time!.Rules.TicksPerDay) * step).ToArray();
				if (array.Length != 0)
				{
					long num = Math.Max(events.WorldTime + step, array.Min());
					if (events.PeriodicDeadlines.TryGetValue("$settlement-work", out var value) && num < value)
					{
						queue.Cancel("$settlement-work");
						events.PeriodicDeadlines.Remove("$settlement-work");
					}
					Register("$settlement-work", SimulationEventKind.SettlementSettlement, num);
				}
				else if (events.PeriodicDeadlines.Remove("$settlement-work"))
				{
					queue.Cancel("$settlement-work");
				}
			}
			var testProcurement = w.TestProcurement;
			if (testProcurement is not null)
			{
				Register("$procurement-purchase", SimulationEventKind.ProcurementPurchase, Next(step));
				Register("$procurement-review", SimulationEventKind.ProcurementReview, Next(testProcurement.IntervalTicks * step));
			}
			var sectDuty = w.SectDuty;
			if (sectDuty is not null)
			{
				SectDuties.Advance(w, review: false, publish: false);
				Register("$sect-publication", SimulationEventKind.SectPublication, Next(sectDuty.MissionIntervalTicks * step));
				HashSet<string> keys = w.MemberDuties.Select((MemberDuty d) => "$member-review:" + d.ActorId).ToHashSet(StringComparer.Ordinal);
				string[] array2 = events.PeriodicDeadlines.Keys.Where((string k) => k.StartsWith("$member-review:", StringComparison.Ordinal) && !keys.Contains(k)).ToArray();
				foreach (string text in array2)
				{
					queue.Cancel(text);
					events.PeriodicDeadlines.Remove(text);
				}
				foreach (MemberDuty item in w.MemberDuties.OrderBy((MemberDuty d) => d.ActorId, StringComparer.Ordinal))
				{
					Register("$member-review:" + item.ActorId, SimulationEventKind.MemberReview, events.WorldTime + sectDuty.ReviewIntervalTicks * step, item.ActorId);
				}
			}
			var huntRules = w.HuntRules;
			if (huntRules is not null)
			{
				Register("$hunt-publication", SimulationEventKind.HuntPublication, (events.WorldTime == 0L) ? step : Next(huntRules.IntervalTicks * step));
			}
			PartyActions.Advance(w, expire: false);
			foreach (PartyInvitation item2 in w.PartyInvitations.Where((PartyInvitation v) => v.Status == PartyInviteStatus.Pending))
			{
				string text2 = "$party-expiry:" + item2.Id;
				if (!Live(text2))
				{
					queue.Schedule(item2.ExpiresAtSeconds ?? (item2.ExpiresTick * step), SimulationEventKind.PartyExpiry, text2, null, item2.Id);
				}
			}
			SimulationEvent[] array3 = events.Pending.Where((SimulationEvent e) => e.Kind == SimulationEventKind.PartyExpiry && e.Version == events.Versions[e.CorrelationId] && !w.PartyInvitations.Any((PartyInvitation v) => v.Id == e.Detail && v.Status == PartyInviteStatus.Pending)).ToArray();
			foreach (SimulationEvent simulationEvent in array3)
			{
				queue.Cancel(simulationEvent.CorrelationId);
			}
			foreach (BattleTimeReceipt item3 in w.Time!.BattleTime.Where((BattleTimeReceipt b) => b.RemainingTicks > 0))
			{
				string text3 = "$battle-catchup:" + item3.BattleId;
				if (!Live(text3))
				{
					queue.Schedule(events.WorldTime + item3.RemainingTicks * step, SimulationEventKind.BattleCatchup, text3, null, item3.BattleId);
				}
			}
			foreach (KeyValuePair<string, long> battleReleaseDeadline in events.BattleReleaseDeadlines)
			{
				string text4 = "$battle-release:" + battleReleaseDeadline.Key;
				if (!Live(text4))
				{
					queue.Schedule(battleReleaseDeadline.Value, SimulationEventKind.BattleRelease, text4, battleReleaseDeadline.Key);
				}
			}
		}
		bool Live(string key)
		{
			return events.Pending.Any((SimulationEvent e) => e.CorrelationId == key && e.Version == events.Versions[key]);
		}
		long Next(long num4)
		{
			checked
			{
				return (unchecked(events.WorldTime / num4) + 1) * num4;
			}
		}
		void Register(string key, SimulationEventKind kind, long first, string? actor = null)
		{
			if (!events.PeriodicDeadlines.TryGetValue(key, out var value2))
			{
				events.PeriodicDeadlines.Add(key, value2 = first);
			}
			if (!Live(key))
			{
				queue.Schedule(value2, kind, key, actor);
			}
		}
	}

	internal static bool Handles(SimulationEventKind kind)
	{
		if ((uint)(kind - 16) <= 7u || (uint)(kind - 25) <= 1u)
		{
			return true;
		}
		return false;
	}

	internal static void Resolve(WorldState w, SimulationEvent item, EventScheduler queue)
	{
		DiscreteEventState discreteEvents = w.Time!.DiscreteEvents!;
		long num = DiscreteTime.SecondsPerStep(w);
		long num2 = 0L;
		checked
		{
			switch (item.Kind)
			{
			case SimulationEventKind.CalendarDay:
				w.Memories.RemoveAll((ActionMemory m) => m.Tick < unchecked(w.Clock.Tick - w.Rules.MemoryTicks));
				ActionExecutor.Emit(w, item.CorrelationId, "DayStarted", CalendarCalculator.Date(w.Clock.Tick, w.Time!.Rules).ToString());
				num2 = w.Time!.Rules.TicksPerDay * num;
				break;
			case SimulationEventKind.SettlementSettlement:
				SettlementActions.Advance(w);
				if (item.CorrelationId == "$settlement-day")
				{
					num2 = w.Time!.Rules.TicksPerDay * num;
				}
				else
				{
					discreteEvents.PeriodicDeadlines.Remove(item.CorrelationId);
				}
				break;
			case SimulationEventKind.ProcurementPurchase:
				TestProcurement.Advance(w, purchase: true, review: false);
				num2 = num;
				break;
			case SimulationEventKind.ProcurementReview:
				TestProcurement.Advance(w, purchase: false);
				num2 = (w.TestProcurement ?? throw new InvalidDataException("Missing procurement rules")).IntervalTicks * num;
				break;
			case SimulationEventKind.SectPublication:
				SectDuties.Advance(w, review: false);
				num2 = (w.SectDuty ?? throw new InvalidDataException("Missing sect duty rules")).MissionIntervalTicks * num;
				break;
			case SimulationEventKind.MemberReview:
				SectDuties.Advance(w, review: true, publish: false, item.ActorId);
				EventDecisions.Request(w, item.ActorId ?? throw new InvalidDataException("Missing event actor"), DecisionTrigger.StateChanged);
				if (w.MemberDuties.Any((MemberDuty d) => d.ActorId == item.ActorId))
				{
					num2 = (w.SectDuty ?? throw new InvalidDataException("Missing sect duty rules")).ReviewIntervalTicks * num;
				}
				else
				{
					discreteEvents.PeriodicDeadlines.Remove(item.CorrelationId);
				}
				break;
			case SimulationEventKind.HuntPublication:
				HuntActions.Advance(w);
				num2 = ((w.HuntRules ?? throw new InvalidDataException("Missing hunt rules")).IntervalTicks - unchecked(w.Clock.Tick % (w.HuntRules ?? throw new InvalidDataException("Missing hunt rules")).IntervalTicks)) * num;
				break;
			case SimulationEventKind.PartyExpiry:
			{
				PartyActions.Advance(w, expire: true, item.Detail);
				break;
			}
			case SimulationEventKind.BattleCatchup:
			{
				int num4 = w.Time!.BattleTime.FindIndex((BattleTimeReceipt b) => b.BattleId == item.Detail);
				if (num4 >= 0)
				{
					w.Time!.BattleTime[num4] = w.Time!.BattleTime[num4] with
					{
						RemainingTicks = 0
					};
				}
				ActionExecutor.Emit(w, item.Detail ?? throw new InvalidDataException("Missing battle id"), "BattleWorldTimeReplayed", $"{item.DueAt}秒");
				break;
			}
			case SimulationEventKind.BattleRelease:
			{
				discreteEvents.BattleReleaseDeadlines.Remove(item.ActorId ?? throw new InvalidDataException("Missing battle actor"));
				int num3 = w.Time!.Budgets.FindIndex((ActionPointBudget b) => b.ActorId == item.ActorId);
				if (num3 >= 0)
				{
					w.Time!.Budgets[num3] = w.Time!.Budgets[num3] with
					{
						BusyThroughTick = w.Clock.Tick
					};
				}
				EventDecisions.Request(w, item.ActorId ?? throw new InvalidDataException("Missing event actor"), DecisionTrigger.ActionCompleted);
				break;
			}
			}
			if (num2 > 0)
			{
				long num5 = item.DueAt + num2;
				discreteEvents.PeriodicDeadlines[item.CorrelationId] = num5;
				queue.Schedule(num5, item.Kind, item.CorrelationId, item.ActorId);
			}
		}
	}

	internal static void Validate(WorldState w)
	{
		if (!Enabled(w))
		{
			return;
		}
		DiscreteEventState events = w.Time!.DiscreteEvents!;
		if (!w.Time!.IndependentScheduledEvents || !w.Time!.IndependentLifeEvents || !w.Time!.IndependentRecoveryEvents || !w.Time!.IndependentMissionExpiryEvents || !w.Time!.IndependentSocialExpiryEvents || !w.Time!.IndependentResourceEvents || !w.Time!.IndependentPayrollEvents)
		{
			throw new InvalidDataException("World events require all independent R1 resolvers");
		}
		if (events.Pending.Any((SimulationEvent e) => e.Kind == SimulationEventKind.CompatibilityStep))
		{
			throw new InvalidDataException("R1 world events cannot contain a Tick bridge");
		}
		List<string> list = new List<string> { "$calendar-day", "$settlement-day" };
		if (w.TestProcurement is not null)
		{
			list.AddRange(new string[] { "$procurement-purchase", "$procurement-review" });
		}
		if (w.HuntRules is not null)
		{
			list.Add("$hunt-publication");
		}
		if (w.SectDuty is not null)
		{
			list.Add("$sect-publication");
		}
		list.AddRange(w.MemberDuties.Select((MemberDuty d) => "$member-review:" + d.ActorId));
		if (list.Any((string k) => !events.PeriodicDeadlines.ContainsKey(k)))
		{
			throw new InvalidDataException("Missing world period deadline");
		}
		foreach (KeyValuePair<string, long> entry in events.PeriodicDeadlines)
		{
			if (entry.Value < events.WorldTime || Enumerable.Count(events.Pending, (SimulationEvent e) => e.CorrelationId == entry.Key && e.DueAt == entry.Value && e.Version == events.Versions[e.CorrelationId]) != 1)
			{
				throw new InvalidDataException("World period deadline/event mismatch: " + entry.Key);
			}
		}
		foreach (PartyInvitation invitation in w.PartyInvitations.Where((PartyInvitation v) => v.Status == PartyInviteStatus.Pending))
		{
			long? expiresAtSeconds = invitation.ExpiresAtSeconds;
			if (expiresAtSeconds.HasValue)
			{
				long due = expiresAtSeconds.GetValueOrDefault();
				if (due >= events.WorldTime && Enumerable.Count(events.Pending, (SimulationEvent e) => e.Kind == SimulationEventKind.PartyExpiry && e.Detail == invitation.Id && e.DueAt == due && e.Version == events.Versions[e.CorrelationId]) == 1)
				{
					continue;
				}
			}
			throw new InvalidDataException("Party invitation requires one exact expiry event");
		}
		foreach (KeyValuePair<string, long> release in events.BattleReleaseDeadlines)
		{
			if (release.Value < events.WorldTime || Enumerable.Count(events.Pending, (SimulationEvent e) => e.Kind == SimulationEventKind.BattleRelease && e.ActorId == release.Key && e.DueAt == release.Value && e.Version == events.Versions[e.CorrelationId]) != 1)
			{
				throw new InvalidDataException("Battle release deadline/event mismatch");
			}
		}
	}
}
