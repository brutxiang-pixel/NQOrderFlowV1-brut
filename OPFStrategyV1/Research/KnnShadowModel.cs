using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OPFStrategyV1.Research;

public sealed class KnnShadowModel
{
    private const string ExpectedSha256 = "A378B7478A0A4022A525CB66887BDC1D9C540E2A3A7D9C94FBCFA7735D8483D3";
    private readonly ModelFile _model;
    private readonly Dictionary<string, int>[] _categoryCodes;
    private readonly Dictionary<string, List<ModelGroup>> _groupsByPath;

    private KnnShadowModel(ModelFile model, string sha256)
    {
        _model = model;
        Sha256 = sha256;
        _categoryCodes = model.CategoricalFeatures
            .Select(feature => model.Categories[feature]
                .Select((value, index) => (value, index))
                .ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal))
            .ToArray();
        _groupsByPath = model.Groups
            .GroupBy(x => GroupKey(x.Side, x.ResearchPath), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
    }

    public string ModelVersion => _model.ModelVersion;
    public string Sha256 { get; }
    public IReadOnlyList<string> NumericFeatures => _model.NumericFeatures;
    public IReadOnlyList<string> CategoricalFeatures => _model.CategoricalFeatures;

    public static KnnShadowModel LoadEmbedded()
    {
        var assembly = typeof(KnnShadowModel).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("opf_v197_knn_shadow_model.json.gz", StringComparison.Ordinal));
        using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded KNN model resource not found: {resourceName}");
        using var compressed = new MemoryStream();
        resource.CopyTo(compressed);
        var bytes = compressed.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha256, ExpectedSha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"Embedded KNN model SHA256 mismatch: expected={ExpectedSha256} actual={sha256}");
        compressed.Position = 0;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        var model = JsonSerializer.Deserialize<ModelFile>(gzip, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Embedded KNN model is empty or invalid.");
        return new KnnShadowModel(model, sha256);
    }

    public KnnShadowDecision Score(string side, string researchPath, double[] numeric, string[] categorical)
    {
        if (numeric.Length != _model.NumericFeatures.Length)
            throw new ArgumentException("KNN numeric feature count mismatch.", nameof(numeric));
        if (categorical.Length != _model.CategoricalFeatures.Length)
            throw new ArgumentException("KNN categorical feature count mismatch.", nameof(categorical));
        if (!_groupsByPath.TryGetValue(GroupKey(side, researchPath), out var groups))
            return KnnShadowDecision.Unavailable(ModelVersion, Sha256, "PathMissing");

        var categoryValues = categorical
            .Select((value, index) => _categoryCodes[index].TryGetValue(value, out var code) ? code : -1)
            .ToArray();
        KnnShadowPolicyScore? observedBest = null;
        KnnShadowPolicyScore? conservativeBest = null;
        foreach (var group in groups)
        {
            var score = ScoreGroup(group, numeric, categoryValues);
            if (observedBest is null || score.ObservedR > observedBest.ScoreR)
                observedBest = new KnnShadowPolicyScore(group.ExitPolicy, score.ObservedR, score.ReferenceCount);
            if (conservativeBest is null || score.ConservativeR > conservativeBest.ScoreR)
                conservativeBest = new KnnShadowPolicyScore(group.ExitPolicy, score.ConservativeR, score.ReferenceCount);
        }

        return new KnnShadowDecision(
            ModelVersion,
            Sha256,
            observedBest!,
            conservativeBest!,
            observedBest!.ScoreR >= _model.GateThresholdR,
            conservativeBest!.ScoreR >= _model.GateThresholdR,
            string.Empty);
    }

    private GroupScore ScoreGroup(ModelGroup group, double[] numeric, int[] categories)
    {
        var standardized = new double[numeric.Length];
        for (var i = 0; i < numeric.Length; i++)
        {
            var std = group.Stds[i] == 0 ? 1 : group.Stds[i];
            standardized[i] = Math.Clamp((numeric[i] - group.Means[i]) / std, -5, 5);
        }

        var numericCount = _model.NumericFeatures.Length;
        var categoricalCount = _model.CategoricalFeatures.Length;
        var distances = new List<(double Distance, double ObservedR, double ConservativeR)>(group.References.Length);
        foreach (var reference in group.References)
        {
            var distance = 0d;
            for (var i = 0; i < numericCount; i++)
            {
                var delta = standardized[i] - reference[i];
                distance += delta * delta;
            }
            var mismatches = 0;
            for (var i = 0; i < categoricalCount; i++)
            {
                if (categories[i] != (int)reference[numericCount + i])
                    mismatches++;
            }
            distance += _model.CategoryWeight * _model.CategoryWeight * 2d * mismatches;
            distances.Add((
                distance,
                reference[numericCount + categoricalCount],
                reference[numericCount + categoricalCount + 1]));
        }

        var neighbors = distances.OrderBy(x => x.Distance).Take(Math.Min(_model.K, distances.Count)).ToArray();
        var weightSum = 0d;
        var observedWeighted = 0d;
        var conservativeWeighted = 0d;
        foreach (var neighbor in neighbors)
        {
            var weight = 1d / (Math.Sqrt(neighbor.Distance) + 0.5d);
            weightSum += weight;
            observedWeighted += weight * neighbor.ObservedR;
            conservativeWeighted += weight * neighbor.ConservativeR;
        }

        var observed = (observedWeighted + _model.ObservedShrinkage * _model.ObservedGlobalMeanR)
            / (weightSum + _model.ObservedShrinkage);
        var conservative = (conservativeWeighted + _model.ConservativeShrinkage * _model.ConservativeGlobalMeanR)
            / (weightSum + _model.ConservativeShrinkage);
        return new GroupScore(observed, conservative, neighbors.Length);
    }

    private static string GroupKey(string side, string researchPath) => $"{side}|{researchPath}";

    private sealed record GroupScore(double ObservedR, double ConservativeR, int ReferenceCount);

    private sealed class ModelFile
    {
        public string ModelVersion { get; set; } = string.Empty;
        public int K { get; set; }
        public double CategoryWeight { get; set; }
        public double GateThresholdR { get; set; }
        public double ObservedShrinkage { get; set; }
        public double ConservativeShrinkage { get; set; }
        public double ObservedGlobalMeanR { get; set; }
        public double ConservativeGlobalMeanR { get; set; }
        public string[] NumericFeatures { get; set; } = Array.Empty<string>();
        public string[] CategoricalFeatures { get; set; } = Array.Empty<string>();
        public Dictionary<string, string[]> Categories { get; set; } = new(StringComparer.Ordinal);
        public ModelGroup[] Groups { get; set; } = Array.Empty<ModelGroup>();
    }

    private sealed class ModelGroup
    {
        public string Side { get; set; } = string.Empty;
        public string ResearchPath { get; set; } = string.Empty;
        public string ExitPolicy { get; set; } = string.Empty;
        public double[] Means { get; set; } = Array.Empty<double>();
        public double[] Stds { get; set; } = Array.Empty<double>();
        public double[][] References { get; set; } = Array.Empty<double[]>();
    }
}

public sealed record KnnShadowPolicyScore(string Policy, double ScoreR, int ReferenceCount);

public sealed record KnnShadowDecision(
    string ModelVersion,
    string ModelSha256,
    KnnShadowPolicyScore Observed,
    KnnShadowPolicyScore Conservative,
    bool ObservedGatePassed,
    bool ConservativeGatePassed,
    string UnavailableReason)
{
    public bool Available => string.IsNullOrEmpty(UnavailableReason);

    public static KnnShadowDecision Unavailable(string modelVersion, string sha256, string reason)
    {
        var unavailable = new KnnShadowPolicyScore(string.Empty, 0d, 0);
        return new KnnShadowDecision(modelVersion, sha256, unavailable, unavailable, false, false, reason);
    }
}
