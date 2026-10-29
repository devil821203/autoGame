using Godot;
using Cultivation.Domain;
using Cultivation.Simulation;

namespace Cultivation.Presentation;

public partial class MortalCultivationPanel : VBoxContainer
{
    public Func<WorldState>? ReadWorld { get; set; }
    public event Action<ActionIntent>? Requested;
    private readonly OptionButton targets = new();
    private readonly SpinBox repetitions = new() { MinValue = 1, MaxValue = 1, Step = 1, Value = 1 };
    private readonly Label batchInfo = new();
    private readonly OptionButton methodSkills = new();
    private readonly Button methodTrain = new() { Text = "修練選定功法（主角）" };
    private string[] skillIds = [];
    private readonly RichTextLabel details = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 280) };
    private readonly Button train = new() { Text = "修練選定屬性（主角）" };
    private readonly Button meditate = new() { Text = "打坐（主角）" };
    private readonly MortalAttribute[] choices = Enum.GetValues<MortalAttribute>().Where(a => MortalCultivationCalculator.Aspect(a) != CultivationAspect.Qi).ToArray();
    public string DetailsText => details.Text;
    public int Repetitions { get => (int)repetitions.Value; set => repetitions.Value = value; }
    public string BatchText => batchInfo.Text;
    public static string AttributeName(MortalAttribute a) => a switch {
        MortalAttribute.Intelligence => "智力", MortalAttribute.MentalStrength => "精神", MortalAttribute.DivineSense => "神識", MortalAttribute.Willpower => "意志",
        MortalAttribute.Strength => "力量", MortalAttribute.Agility => "敏捷", MortalAttribute.Constitution => "體質", MortalAttribute.Sensitivity => "感應",
        MortalAttribute.QiSense => "氣感", MortalAttribute.Comprehension => "悟性", _ => "氣運" };
    public override void _Ready()
    {
        AddChild(new Label { Text = "凡人修為｜手動指令優先，無指令時主角可自主排程" });
        var batchRow = new HBoxContainer(); AddChild(batchRow); batchRow.AddChild(new Label { Text = "連續修練次數" }); batchRow.AddChild(repetitions); batchRow.AddChild(batchInfo);
        repetitions.ValueChanged += _ => { if (ReadWorld?.Invoke() is { } world) UpdateBatchInfo(world); };
        var row = new HBoxContainer(); AddChild(row); row.AddChild(targets);
        foreach (var a in choices) targets.AddItem(AttributeName(a));
        row.AddChild(train); row.AddChild(meditate); AddChild(details);
        var methodRow = new HBoxContainer(); AddChild(methodRow); methodRow.AddChild(methodSkills); methodRow.AddChild(methodTrain);
        methodTrain.Pressed += () => {
            if (ReadWorld?.Invoke() is not { } w || methodSkills.Selected < 0 || methodSkills.Selected >= skillIds.Length) return;
            var skill = w.Methods!.Catalog.Methods.SelectMany(m => m.Skills).Single(s => s.Id == skillIds[methodSkills.Selected]);
            Requested?.Invoke(new($"method-player:{w.NextSequence}", w.ControlledCharacterId, w.ControlledCharacterId, "method:" + skill.Id, (long)repetitions.Value, 0,
                DecisionSource.Player, skill.CultivationTarget is null ? ActionKind.TrainMortalAttribute : ActionKind.MeditateQi));
        };
        train.Pressed += () => Request(false); meditate.Pressed += () => Request(true);
    }
    public void Request(bool meditation)
    {
        if (ReadWorld?.Invoke() is not { MortalActionRules: not null } w) return;
        var a = w.ControlledCharacterId;
        Requested?.Invoke(new($"mortal-player:{w.NextSequence}", a, a, meditation ? "qi" : choices[targets.Selected].ToString(), (long)repetitions.Value, 0,
            DecisionSource.Player, meditation ? ActionKind.MeditateQi : ActionKind.TrainMortalAttribute));
    }
    public static string Describe(WorldState w, CharacterState a)
    {
        if (a.MortalCultivation is not { } s || w.MortalTrainingRules is not { } r) return "未啟用凡人修為";
        var lines = new List<string> { $"{a.Name}｜凡人｜體力 {a.Stamina?.Current}｜感炁：{s.Awakening switch { QiAwakeningStage.Unaware => "未感炁", QiAwakeningStage.Eligible => "等待得炁", _ => "已得炁" }}",
            $"入門 {s.QiInitiationExperience}/{w.MortalActionRules?.InitiationExperienceRequired}｜得炁嘗試 {s.AwakeningAttempts}｜正式炁功經驗 {s.QiExperience}" };
        foreach (var aspect in Enum.GetValues<CultivationAspect>())
        {
            long level = MortalCultivationCalculator.Level(s, aspect);
            lines.Add($"{aspect switch { CultivationAspect.Spirit => "性功", CultivationAspect.Body => "命功", _ => "炁功" }}：{level}級｜{MortalCultivationCalculator.CompletedLayers(level, r)}重｜下一級 {MortalCultivationCalculator.Threshold(level, r)}經驗");
        }
        foreach (var attribute in Enum.GetValues<MortalAttribute>())
            lines.Add($"{AttributeName(attribute)}：{CurrentAttributeCalculator.Value(a, attribute)}（基礎{s.BaseAttributes.GetValueOrDefault(attribute)}、修練+{s.AttributeGrowth.GetValueOrDefault(attribute)}）｜經驗 {s.Experience.GetValueOrDefault(attribute)}");
        var task = w.MortalTrainingWork.SingleOrDefault(t => t.Intent.ActorId == a.Id);
        long unitDuration = task is not null && MethodTraining.IsMethod(task.Intent) ? w.Methods!.Catalog.TrainingRules.DurationTicks : w.MortalActionRules!.DurationTicks;
        lines.Add(task is null ? "目前未修練" : $"修練中：{task.Intent.ResourceId}，剩{task.RemainingIterations}次／{GameTimeDisplay.Duration(w, task.DueTick - w.Clock.Tick + (task.RemainingIterations - 1) * unitDuration)}");
        if (w.Methods is { } methods && methods.Learners.TryGetValue(a.Id, out var learned))
            foreach (var method in methods.Catalog.Methods)
            foreach (var skill in method.Skills.Where(s => learned.ContainsKey(s.Id)))
                lines.Add($"{method.Name}／{skill.Name}：熟練{learned[skill.Id]}，第{MethodTraining.Layer(w, a.Id, skill.Id)}層");
        return string.Join("\n", lines);
    }
    public void Refresh(WorldState w)
    {
        repetitions.MaxValue = w.Time?.ActionDriven?.TrainingBatchLimit ?? 1;
        UpdateBatchInfo(w);
        train.Disabled = meditate.Disabled = w.MortalActionRules is null;
        details.Text = Describe(w, w.Characters.Single(a => a.Id == w.ControlledCharacterId));
        string[] next = w.Methods?.Learners.GetValueOrDefault(w.ControlledCharacterId)?.Keys.ToArray() ?? [];
        if (!next.SequenceEqual(skillIds)) {
            skillIds = next; methodSkills.Clear();
            foreach (string id in skillIds) methodSkills.AddItem(w.Methods!.Catalog.Methods.SelectMany(m => m.Skills).Single(s => s.Id == id).Name);
        }
        methodTrain.Disabled = skillIds.Length == 0;
    }
    private void UpdateBatchInfo(WorldState w)
    {
        long count = (long)repetitions.Value;
        var normal = w.MortalActionRules;
        batchInfo.Text = normal is null ? "本情境未啟用修練" : $"屬性／打坐：{GameTimeDisplay.Duration(w, count * normal.DurationTicks)}／{count * normal.ActionPoints}AP";
        if (w.Methods is { } methods) batchInfo.Text += $"；功法：{GameTimeDisplay.Duration(w, count * methods.Catalog.TrainingRules.DurationTicks)}／{count * methods.Catalog.TrainingRules.ActionPointCost}AP";
        batchInfo.TooltipText = "開始一次扣總AP，每段結算體力與經驗；中斷不退已付AP。";
    }
    public override void _ExitTree()
    {
        // Release shaped text before Godot tears down its font/rendering services.
        details.Clear();
    }
}
