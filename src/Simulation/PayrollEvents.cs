using Cultivation.Domain;

namespace Cultivation.Simulation;

internal static class PayrollEvents
{
    private const string Key = "$sect-payroll";
    private static long Interval(WorldState w)=>checked(w.SectPayroll!.IntervalTicks*DiscreteTime.SecondsPerStep(w));
    internal static void EnableFresh(WorldState w)
    {
        if(w.Time?.DiscreteEvents is not {WorldTime:0} events)throw new InvalidOperationException("Payroll events require a fresh seconds world");
        w.Time.IndependentPayrollEvents=true;
        if(w.SectPayroll is null)return;
        w.Time.SectPayrollDueSeconds=Interval(w);
        new EventScheduler(events).Schedule(Interval(w),SimulationEventKind.SectPayroll,Key);
    }
    internal static void Resolve(WorldState w,SimulationEvent item,EventScheduler queue)
    {
        if(w.Time?.IndependentPayrollEvents!=true||item.CorrelationId!=Key||item.DueAt!=w.Time.SectPayrollDueSeconds)
            throw new InvalidDataException("Unexpected payroll event");
        // Duties/procurement still use the bridge. Preserve their same-time precedence over payroll.
        // Requeue at the SAME second, behind that already queued bridge; never advance an extra interval.
        if(w.Clock.Tick<item.DueAt/DiscreteTime.SecondsPerStep(w)){
            if(!w.Time.DiscreteEvents!.Pending.Any(e=>e.Kind==SimulationEventKind.CompatibilityStep&&e.DueAt==item.DueAt&&e.Version==w.Time.DiscreteEvents.Versions[e.CorrelationId]))
                throw new InvalidDataException("Payroll boundary missing prerequisite bridge");
            queue.Schedule(item.DueAt,SimulationEventKind.SectPayroll,Key);return;
        }
        SectPayroll.Advance(w);
        ActionExecutor.Emit(w,Key,"SectPayrollResolved",$"{item.DueAt}秒");
        long next=checked(item.DueAt+Interval(w));w.Time.SectPayrollDueSeconds=next;
        queue.Schedule(next,SimulationEventKind.SectPayroll,Key);
    }
    internal static void Validate(WorldState w)
    {
        var t=w.Time;
        var live=t?.DiscreteEvents?.Pending.Where(e=>e.Kind==SimulationEventKind.SectPayroll&&e.Version==t.DiscreteEvents.Versions[e.CorrelationId]).ToArray()??[];
        if(t?.IndependentPayrollEvents!=true){
            if(t?.SectPayrollDueSeconds is not null||live.Length!=0)throw new InvalidDataException("Payroll event requires seconds mode");return;
        }
        if(t.DiscreteEvents is not {} events)throw new InvalidDataException("Missing payroll clock");
        if(w.SectPayroll is null){
            if(t.SectPayrollDueSeconds is not null||live.Length!=0)throw new InvalidDataException("Payroll event without rules");return;
        }
        if(w.SectPayroll.IntervalTicks<=0||t.SectPayrollDueSeconds is not {} due||due<events.WorldTime||due<=0||due%Interval(w)!=0||due-events.WorldTime>Interval(w)||
            live.Length!=1||live[0].DueAt!=due||live[0].CorrelationId!=Key)throw new InvalidDataException("Payroll deadline/event mismatch");
    }
}
