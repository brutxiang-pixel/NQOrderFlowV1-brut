using OPFStrategyV1.Strategy;

var cases = new[]
{
    ("short-zonebirth-score45", "Short", "ZoneBirthResearch", 45.00m, true),
    ("long-zonebirth-score45", "Long", "ZoneBirthResearch", 45.00m, false),
    ("short-otherpath-score45", "Short", "ObservationConfirm", 45.00m, false),
    ("short-zonebirth-score44_99", "Short", "ZoneBirthResearch", 44.99m, false),
    ("short-zonebirth-score45_01", "Short", "ZoneBirthResearch", 45.01m, false)
};

foreach (var testCase in cases)
{
    var actual = ZoneBirthResearchShortGate.ShouldDisable(testCase.Item2, testCase.Item3, testCase.Item4);
    if (actual != testCase.Item5)
        throw new InvalidOperationException($"Assertion failed [{testCase.Item1}]: expected={testCase.Item5} actual={actual}");
}

Console.WriteLine($"PASS ZoneBirthResearchShortGate cases={cases.Length}");
