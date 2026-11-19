using Cultivation.Domain;

namespace Cultivation.AI;

// Explicitly supplied observations, not a global WorldState reference.
public sealed record ActorObservation(string ActorId, long Tick, long AvailableStones);
public interface IDecisionSource
{
    ActionIntent? Decide(ActorObservation observation);
}

// Phase-0 fixture only: exercises the shared pipeline, not Utility AI.
public sealed class TransferProbe(string recipient) : IDecisionSource
{
    public ActionIntent? Decide(ActorObservation observation) =>
        observation.Tick == 0 && observation.AvailableStones >= 10
            ? new("probe:" + observation.ActorId, observation.ActorId, recipient,
                "spirit-stone", 10, 2, DecisionSource.Npc)
            : null;
}
