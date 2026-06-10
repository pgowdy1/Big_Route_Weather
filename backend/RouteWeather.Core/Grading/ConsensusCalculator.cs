using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.Core.Grading;

public record ConsensusInput(SourceSnapshot Source, double Weight);

public record EnsembleResult(WeatherSnapshot? Blended, ConsensusReport? Consensus);

public class ConsensusCalculator
{
    private const double WindSpreadFloorMph = 5.0;
    private const double TempSpreadFloorF = 5.0;
    private const double PrecipSpreadFloorPct = 20.0;
    private const double GustSpreadFloorMph = 8.0;
    private const double CapeSpreadFloorJkg = 200.0;

    private readonly double _highMaxCv;
    private readonly double _mediumMaxCv;

    public ConsensusCalculator(double highMaxCv = 0.25, double mediumMaxCv = 0.50)
    {
        _highMaxCv = highMaxCv;
        _mediumMaxCv = mediumMaxCv;
    }

    public EnsembleResult Compute(IReadOnlyList<ConsensusInput> inputs, int sourcesAttempted)
    {
        if (inputs.Count == 0)
        {
            return new EnsembleResult(null, null);
        }

        var blended = BlendSnapshots(inputs);
        var cvByFactor = ComputeCvByFactor(inputs);
        var (level, worst) = ResolveLevel(inputs, cvByFactor);
        var report = new ConsensusReport(level, worst, cvByFactor, inputs.Count, sourcesAttempted);
        return new EnsembleResult(blended, report);
    }

    private static WeatherSnapshot BlendSnapshots(IReadOnlyList<ConsensusInput> inputs)
    {
        var windInputs = Active(inputs, ForecastFactors.Wind);
        var tempInputs = Active(inputs, ForecastFactors.Temperature);
        var precipInputs = Active(inputs, ForecastFactors.Precipitation);

        var wind = WeightedMean(windInputs, s => s.WindMph);
        var temp = WeightedMean(tempInputs, s => s.TempF);
        var precip = WeightedMean(precipInputs, s => s.PrecipitationProbabilityPct);

        var baseline = inputs.OrderByDescending(i => i.Weight).First().Source.Snapshot;
        var blendedHours = BlendHourly(inputs, baseline.Next48Hours);

        // Headline new-fields are derived from the blended hourly series for consistency with
        // WindowGradeCalculator.Aggregate: max gust, max CAPE, summed precip amount.
        var blendedGusts = blendedHours.Where(h => h.GustMph.HasValue).Select(h => h.GustMph!.Value).ToList();
        var blendedCapes = blendedHours.Where(h => h.CapeJkg.HasValue).Select(h => h.CapeJkg!.Value).ToList();
        var blendedAmounts = blendedHours.Where(h => h.PrecipitationIn.HasValue).Select(h => h.PrecipitationIn!.Value).ToList();

        return new WeatherSnapshot(
            WindMph: Math.Round(wind),
            TempF: Math.Round(temp),
            PrecipitationProbabilityPct: (int)Math.Round(precip),
            Next48Hours: blendedHours,
            MaxGustMph: blendedGusts.Count == 0 ? null : blendedGusts.Max(),
            MaxCapeJkg: blendedCapes.Count == 0 ? null : blendedCapes.Max(),
            PrecipAmountIn: blendedAmounts.Count == 0 ? null : blendedAmounts.Sum());
    }

    private static IReadOnlyList<ConsensusInput> Active(IReadOnlyList<ConsensusInput> inputs, string factor) =>
        inputs.Where(i => i.Source.ActiveFactors.Contains(factor)).ToList();

    private static double WeightedMean(IReadOnlyList<ConsensusInput> inputs, Func<WeatherSnapshot, double> select)
    {
        if (inputs.Count == 0) return 0;
        var totalWeight = inputs.Sum(i => i.Weight);
        if (totalWeight <= 0) totalWeight = inputs.Count;
        return inputs.Sum(i => select(i.Source.Snapshot) * i.Weight) / totalWeight;
    }

    private static IReadOnlyList<HourlyForecast> BlendHourly(
        IReadOnlyList<ConsensusInput> inputs,
        IReadOnlyList<HourlyForecast> baseline)
    {
        var windInputs = Active(inputs, ForecastFactors.Wind);
        var tempInputs = Active(inputs, ForecastFactors.Temperature);
        var precipInputs = Active(inputs, ForecastFactors.Precipitation);

        var result = new List<HourlyForecast>(baseline.Count);
        for (var i = 0; i < baseline.Count; i++)
        {
            var hour = baseline[i].Time;
            var temp = HourlyMean(tempInputs, hour, h => h.TempF, baseline[i].TempF);
            var wind = HourlyMean(windInputs, hour, h => h.WindMph, baseline[i].WindMph);
            var precip = HourlyMean(precipInputs, hour, h => h.PrecipitationProbabilityPct, baseline[i].PrecipitationProbabilityPct);

            // New fields blend by value presence across ALL inputs (not ActiveFactors-gated):
            // a source that doesn't report a field contributes nothing to that field's mean.
            var gustH = HourlyMeanNullable(inputs, hour, h => h.GustMph);
            var capeH = HourlyMeanNullable(inputs, hour, h => h.CapeJkg);
            var amountH = HourlyMeanNullable(inputs, hour, h => h.PrecipitationIn);
            var cloudH = HourlyMeanNullable(inputs, hour, h => (double?)h.CloudCoverPct);
            var visH = HourlyMeanNullable(inputs, hour, h => h.VisibilityMiles);
            var apparentH = HourlyMeanNullable(inputs, hour, h => h.ApparentTempF);

            result.Add(new HourlyForecast(
                Time: hour,
                TempF: Math.Round(temp),
                WindMph: Math.Round(wind),
                PrecipitationProbabilityPct: (int)Math.Round(precip),
                ShortForecast: baseline[i].ShortForecast,
                GustMph: gustH is null ? null : Math.Round(gustH.Value),
                CapeJkg: capeH is null ? null : Math.Round(capeH.Value),
                PrecipitationIn: amountH,
                CloudCoverPct: cloudH is null ? null : (int)Math.Round(cloudH.Value),
                VisibilityMiles: visH,
                ApparentTempF: apparentH is null ? null : Math.Round(apparentH.Value)));
        }
        return result;
    }

    private static double HourlyMean(
        IReadOnlyList<ConsensusInput> inputs,
        DateTimeOffset target,
        Func<HourlyForecast, double> select,
        double fallback)
    {
        var sum = 0.0;
        var weight = 0.0;
        foreach (var input in inputs)
        {
            var match = FindNearestHour(input.Source.Snapshot.Next48Hours, target);
            if (match is null) continue;
            sum += select(match) * input.Weight;
            weight += input.Weight;
        }
        return weight <= 0 ? fallback : sum / weight;
    }

    private static double? HourlyMeanNullable(
        IReadOnlyList<ConsensusInput> inputs,
        DateTimeOffset target,
        Func<HourlyForecast, double?> select)
    {
        var sum = 0.0;
        var weight = 0.0;
        foreach (var input in inputs)
        {
            var match = FindNearestHour(input.Source.Snapshot.Next48Hours, target);
            var v = match is null ? null : select(match);
            if (v is null) continue;
            sum += v.Value * input.Weight;
            weight += input.Weight;
        }
        return weight <= 0 ? null : sum / weight;
    }

    private static HourlyForecast? FindNearestHour(IReadOnlyList<HourlyForecast> series, DateTimeOffset target)
    {
        HourlyForecast? best = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var h in series)
        {
            var delta = (h.Time - target).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = h;
            }
        }
        return bestDelta <= TimeSpan.FromMinutes(60) ? best : null;
    }

    private static IReadOnlyDictionary<string, double> ComputeCvByFactor(IReadOnlyList<ConsensusInput> inputs)
    {
        // Entries exist ONLY when >=2 sources report that factor. Legacy factors keep
        // ActiveFactors gating for WHO counts as a reporter; new factors gate on value presence.
        var cv = new Dictionary<string, double>();
        AddCv(cv, ForecastFactors.Wind, Active(inputs, ForecastFactors.Wind).Select(i => i.Source.Snapshot.WindMph), WindSpreadFloorMph);
        AddCv(cv, ForecastFactors.Temperature, Active(inputs, ForecastFactors.Temperature).Select(i => (double)i.Source.Snapshot.TempF), TempSpreadFloorF);
        AddCv(cv, ForecastFactors.Precipitation, Active(inputs, ForecastFactors.Precipitation).Select(i => (double)i.Source.Snapshot.PrecipitationProbabilityPct), PrecipSpreadFloorPct);
        AddCv(cv, ForecastFactors.Gust, inputs.Where(i => i.Source.Snapshot.MaxGustMph.HasValue).Select(i => i.Source.Snapshot.MaxGustMph!.Value), GustSpreadFloorMph);
        AddCv(cv, ForecastFactors.Cape, inputs.Where(i => i.Source.Snapshot.MaxCapeJkg.HasValue).Select(i => i.Source.Snapshot.MaxCapeJkg!.Value), CapeSpreadFloorJkg);
        return cv;
    }

    private static void AddCv(Dictionary<string, double> cv, string factor, IEnumerable<double> values, double floor)
    {
        var list = values.ToList();
        if (list.Count < 2) return;
        cv[factor] = Cv(list, floor);
    }

    private static double Cv(IEnumerable<double> values, double spreadFloor)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
        // Absolute-spread floor: small disagreements (e.g. 8 vs 10 mph wind) shouldn't
        // count as a "disagreement" even when CV is technically large because the mean is small.
        if (list.Max() - list.Min() < spreadFloor) return 0;
        var mean = list.Average();
        var sumSq = list.Sum(v => (v - mean) * (v - mean));
        var stddev = Math.Sqrt(sumSq / list.Count);
        var absMean = Math.Abs(mean);
        if (absMean < 1.0)
        {
            return stddev > 1.0 ? stddev / 10.0 : 0;
        }
        return stddev / absMean;
    }

    private (ConsensusLevel Level, string? WorstFactor) ResolveLevel(
        IReadOnlyList<ConsensusInput> inputs,
        IReadOnlyDictionary<string, double> cv)
    {
        // The >=2-reporter filter now lives in AddCv, so every key in the dict already
        // qualifies; behavior for the legacy three factors is unchanged.
        var factorsWithEnoughSources = cv.Keys.ToList();

        if (factorsWithEnoughSources.Count == 0) return (ConsensusLevel.High, null);

        // Mean CV across factors so a single noisy factor doesn't tank the rating; worst
        // factor is still reported for context when the average is high enough to demote.
        var meanCv = factorsWithEnoughSources.Average(f => cv[f]);
        var worstFactor = factorsWithEnoughSources
            .OrderByDescending(f => cv[f])
            .First();

        var level = meanCv <= _highMaxCv ? ConsensusLevel.High
                  : meanCv <= _mediumMaxCv ? ConsensusLevel.Medium
                  : ConsensusLevel.Low;
        return (level, level == ConsensusLevel.High ? null : worstFactor);
    }
}
