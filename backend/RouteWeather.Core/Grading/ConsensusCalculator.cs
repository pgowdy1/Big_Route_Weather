using RouteWeather.Core.Models;
using RouteWeather.Core.Sources;

namespace RouteWeather.Core.Grading;

public record ConsensusInput(SourceSnapshot Source, double Weight);

public record EnsembleResult(WeatherSnapshot? Blended, ConsensusReport? Consensus);

public class ConsensusCalculator
{
    private readonly double _highMaxCv;
    private readonly double _mediumMaxCv;

    public ConsensusCalculator(double highMaxCv = 0.15, double mediumMaxCv = 0.35)
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

        return new WeatherSnapshot(
            WindMph: Math.Round(wind),
            TempF: Math.Round(temp),
            PrecipitationProbabilityPct: (int)Math.Round(precip),
            Next48Hours: blendedHours);
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

            result.Add(new HourlyForecast(
                Time: hour,
                TempF: Math.Round(temp),
                WindMph: Math.Round(wind),
                PrecipitationProbabilityPct: (int)Math.Round(precip),
                ShortForecast: baseline[i].ShortForecast));
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
        return new Dictionary<string, double>
        {
            [ForecastFactors.Wind] = Cv(Active(inputs, ForecastFactors.Wind).Select(i => i.Source.Snapshot.WindMph)),
            [ForecastFactors.Temperature] = Cv(Active(inputs, ForecastFactors.Temperature).Select(i => (double)i.Source.Snapshot.TempF)),
            [ForecastFactors.Precipitation] = Cv(Active(inputs, ForecastFactors.Precipitation).Select(i => (double)i.Source.Snapshot.PrecipitationProbabilityPct)),
        };
    }

    private static double Cv(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0;
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
        var factorsWithEnoughSources = cv.Keys
            .Where(f => Active(inputs, f).Count >= 2)
            .ToList();

        if (factorsWithEnoughSources.Count == 0) return (ConsensusLevel.High, null);

        var worst = factorsWithEnoughSources
            .Select(f => (Factor: f, Cv: cv[f]))
            .OrderByDescending(t => t.Cv)
            .First();

        var level = worst.Cv <= _highMaxCv ? ConsensusLevel.High
                  : worst.Cv <= _mediumMaxCv ? ConsensusLevel.Medium
                  : ConsensusLevel.Low;
        return (level, level == ConsensusLevel.High ? null : worst.Factor);
    }
}
