using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    [TestFixture, Category("JavaScriptConformance")]
    internal sealed class ConformanceTests
    {
        static readonly Lazy<ConformanceCorpus> Corpus = new Lazy<ConformanceCorpus>(() => ConformanceCorpus.Load(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "JS", "Conformance")));

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var test in Corpus.Value.Cases)
                foreach (var variant in test.Variants())
                    foreach (var profile in Enum.GetValues<ConformanceProfile>())
                    {
                        var data = new TestCaseData(test.Path, variant, profile)
                            .SetName($"Test262.{test.Path}.{variant}.{profile}")
                            .SetCategory(test.Group);
                        yield return data;
                    }
        }

        [OneTimeSetUp]
        public void ReportCoverage()
        {
            var corpus = Corpus.Value;
            var variants = corpus.Cases.Sum(test => test.Variants().Count());
            TestContext.Progress.WriteLine($"Test262: {corpus.Cases.Length} source tests, "
                + $"{variants} variants, {variants * 2} profile cases. Syntax only; JavaScript is not executed.");
            // The imported _FIXTURE.js is a module dependency, not a standalone test.
        }

        [TestCaseSource(nameof(Cases))]
        public void Test262(string path, string variant, ConformanceProfile profile)
        {
            var corpus = Corpus.Value;
            var test = corpus.Case(path);
            var source = test.Expand(corpus.Source(test), variant);
            var context = $"{path}\nVariant: {variant}; profile: {profile}; mode: {test.SourceMode}";
            var input = ConformanceRunner.Parse(source, profile, test.SourceMode, path);
            ConformanceRunner.Verify(test.ParseExpectation, input.HasErrors ? "reject" : "accept", input, context + "\nCheck: parse");
            if (test.ParseExpectation == "reject") return;

            var emitted = Uglify.Js(source, path, ConformanceRunner.Settings(profile, test.SourceMode));
            var output = emitted.HasErrors ? emitted : ConformanceRunner.Parse(emitted.Code, profile, test.SourceMode, path + ".output", variant == "strict");
            var actual = emitted.HasErrors ? "emit-errors" : output.HasErrors ? "reject" : "accept";
            ConformanceRunner.Verify("accept", actual, output, context + "\nCheck: output\nEmitted source:\n" + emitted.Code);
        }
    }
}
