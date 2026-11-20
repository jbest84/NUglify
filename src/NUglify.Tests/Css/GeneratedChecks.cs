using System;
using System.IO;
using FsCheck;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    // Shared replay/reporting for the existing nesting properties and boundary generators.
    internal static class GeneratedChecks
    {
        internal const int Samples = 100;
        const int SecondSeed = 20261120;

        internal static void Run(Property property, int seed, Func<string> counterexample = null,
            int startSize = 0, int endSize = 32)
        {
            var replay = Environment.GetEnvironmentVariable("NUGLIFY_CSS_SEED");
            if (replay != null && (!int.TryParse(replay, out seed) || seed <= 0))
                throw new ArgumentException("NUGLIFY_CSS_SEED must be a positive integer.");
            var config = Configuration.QuickThrowOnFailure;
            config.Replay = FsCheck.Random.StdGen.NewStdGen(seed, SecondSeed);
            config.MaxNbOfTest = Samples;
            config.StartSize = startSize;
            config.EndSize = endSize;
            config.QuietOnSuccess = true;
            try
            {
                property.Check(config);
            }
            catch (Exception error)
            {
                var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestResults", "Conformance");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "CssGenerated-" + TestContext.CurrentContext.Test.ID + ".txt");
                var report = $"{TestContext.CurrentContext.Test.FullName}\nSeed: ({seed}, {SecondSeed}); samples: {Samples}; size: {startSize}..{endSize}\n"
                    + $"Set NUGLIFY_CSS_SEED={seed} and rerun with --filter FullyQualifiedName~{TestContext.CurrentContext.Test.MethodName}\n\n"
                    + counterexample?.Invoke() + "\n\nFsCheck counterexample and shrink result:\n" + error;
                File.WriteAllText(path, report);
                TestContext.AddTestAttachment(path, "Replay seed and minimized CSS counterexample");
                TestContext.Error.WriteLine(report);
                throw;
            }
            TestContext.Progress.WriteLine($"CSS generated: seed ({seed}, {SecondSeed}), {Samples} samples, size {startSize}..{endSize}.");
        }
    }
}
