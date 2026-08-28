namespace UrlShortener.TestHarness;

/// <summary>
/// A deliberately tiny hand-rolled test runner. This exists ONLY because the
/// sandbox this prototype was built in has no NuGet access and therefore
/// cannot restore xUnit (see docs/06-testing-validation.md). It executes the
/// same behavioral assertions as tests/UrlShortener.UnitTests against real
/// (non-mocked) implementations, so its passing output is genuine evidence
/// the logic works — not a replacement for the xUnit suite, which is the
/// intended test project for normal development.
/// </summary>
public static class MiniTest
{
    private static int _passed;
    private static int _failed;
    private static readonly List<string> Failures = [];

    public static async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Failures.Add($"{name}: {ex.Message}");
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.Message}");
        }
    }

    public static void Run(string name, Action test) =>
        RunAsync(name, () => { test(); return Task.CompletedTask; }).GetAwaiter().GetResult();

    public static void True(bool condition, string message)
    {
        if (!condition) throw new Exception($"Expected true: {message}");
    }

    public static void False(bool condition, string message)
    {
        if (condition) throw new Exception($"Expected false: {message}");
    }

    public static void Equal<T>(T expected, T actual, string label)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{label}: expected '{expected}' but got '{actual}'");
    }

    public static int Summarize()
    {
        Console.WriteLine();
        Console.WriteLine($"====== {_passed} passed, {_failed} failed ======");
        if (_failed > 0)
        {
            Console.WriteLine("Failures:");
            foreach (var f in Failures) Console.WriteLine($"  - {f}");
        }
        return _failed == 0 ? 0 : 1;
    }
}
