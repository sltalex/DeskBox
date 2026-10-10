namespace DeskBox.Services;

/// <summary>One machine's aggregate tree state. No individual key or text is retained.</summary>
public sealed class TreeProgress
{
    public int DailyGoal { get; set; } = 12000;
    public bool Paused { get; set; }
    public string LocalDay { get; set; } = string.Empty;
    public long TodayKeys { get; set; }
    public long TotalEnergy { get; set; }
    public int HighestStage { get; set; } = 1;
    public long FruitEnergy { get; set; }
    public long FruitInventory { get; set; }
    public long HarvestedFruit { get; set; }

    public long MaturityThreshold => 30L * DailyGoal;
    public int FruitCost => (DailyGoal + 3) / 4;
    public int Stage => Math.Max(HighestStage, StageFor(TotalEnergy, DailyGoal));

    public void RollDay(DateTime localNow)
    {
        string day = localNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (LocalDay != day) { LocalDay = day; TodayKeys = 0; }
    }

    public static int StageFor(long energy, int goal) => energy >= 30L * goal ? 5
        : energy >= 18L * goal ? 4
        : energy >= 9L * goal ? 3
        : energy >= 3L * goal ? 2 : 1;

    public void AddEnergy(int amount, DateTime localNow)
    {
        if (amount <= 0 || Paused) return;
        RollDay(localNow);
        long before = TotalEnergy;
        bool wasMature = HighestStage == 5 || StageFor(before, DailyGoal) == 5;
        TotalEnergy = checked(TotalEnergy + amount);
        TodayKeys = checked(TodayKeys + amount);
        if (StageFor(TotalEnergy, DailyGoal) > HighestStage)
            HighestStage = StageFor(TotalEnergy, DailyGoal);
        if (HighestStage == 5)
        {
            long fruitGain = wasMature ? amount : Math.Max(0, TotalEnergy - MaturityThreshold);
            FruitEnergy = checked(FruitEnergy + fruitGain);
            FruitInventory = checked(FruitInventory + FruitEnergy / FruitCost);
            FruitEnergy %= FruitCost;
        }
    }

    public void SetGoal(int goal)
    {
        if (goal < 1 || goal > 1000000) throw new ArgumentOutOfRangeException(nameof(goal));
        DailyGoal = goal;
        HighestStage = Math.Max(HighestStage, StageFor(TotalEnergy, goal));
        if (HighestStage == 5)
        {
            FruitInventory = checked(FruitInventory + FruitEnergy / FruitCost);
            FruitEnergy %= FruitCost;
        }
    }

    public long Harvest()
    {
        long picked = FruitInventory;
        HarvestedFruit = checked(HarvestedFruit + picked);
        FruitInventory = 0;
        return picked;
    }
}
