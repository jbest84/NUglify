using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    [TestFixture, Category("JavaScriptConformance")]
    internal sealed class BoundaryCorpusTests
    {
        static readonly Lazy<BoundaryCorpus> Corpus = new Lazy<BoundaryCorpus>(() => BoundaryCorpus.Load(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "JS", "Conformance")));

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var test in Corpus.Value.Cases)
                foreach (var profile in Enum.GetValues<ConformanceProfile>())
                {
                    var data = new TestCaseData(test.Id, profile).SetName($"Boundary.{test.Id}.{profile}");
                    yield return data;
                }
        }

        [OneTimeSetUp]
        public void ReportCoverage()
        {
            var corpus = Corpus.Value;
            TestContext.Progress.WriteLine($"Boundary corpus: {corpus.Cases.Length} source cases, "
                + $"{corpus.Cases.Length * 2} profile cases, {corpus.Cases.Count(test => test.PairPath != null)} reviewed output pairs. "
                + "JavaScript is not executed.");
        }

        [TestCaseSource(nameof(Cases))]
        public void SyntaxAndOutput(string id, ConformanceProfile profile)
        {
            var corpus = Corpus.Value;
            var test = corpus.Cases.Single(item => item.Id == id);
            var source = corpus.Text(test.Path);
            var input = ConformanceRunner.Parse(source, profile, test.Mode, test.Path);
            var context = $"{id}\nVariant: {test.Variant}; profile: {profile}; mode: {test.Mode}\nInput: {test.Path}";
            ConformanceRunner.Verify(test.Expectation, input.HasErrors ? "reject" : "accept", input, context + "\nCheck: parse");
            if (test.Expectation == "reject") return;
            var emitted = Uglify.Js(source, test.Path, ConformanceRunner.Settings(profile, test.Mode));
            var output = emitted.HasErrors ? emitted : ConformanceRunner.Parse(emitted.Code, profile, test.Mode, test.Path + ".output");
            ConformanceRunner.Verify("accept", emitted.HasErrors ? "emit-errors" : output.HasErrors ? "reject" : "accept",
                output, context + "\nCheck: output\nEmitted source:\n" + emitted.Code);
        }

        // These goldens preserve the original and explicit source grouping separately.
        // NoOptimization keeps constants, comma operands, and other expressions intact.
        [TestCase("0140c25a4177e5f7.module.js", "export default(1+2)", "export default(1)+(2)", TestName = "ParserGolden.DefaultExport")]
        [TestCase("0671ec3d0b8ded79.js", "(1,a)();(2,b.a)()", "((1),a)();((2),(b.a))()", TestName = "ParserGolden.UnboundCalls")]
        [TestCase("0c44152a1a3e2f90.js", "-(1-2-3)", "-(((1)-(2))-(3))", TestName = "ParserGolden.UnaryAndSubtraction")]
        [TestCase("129c95a57d234b7b.js", "a*b%c", "(a*b)%c", TestName = "ParserGolden.MultiplyRemainderAssociativity")]
        [TestCase("1972b64c4704a1eb.js", "((a))()", "a()", TestName = "ParserGolden.GroupedCall")]
        [TestCase("1c055d256ec34f17.js", "(a,b,c,d)+e", "(((a,b),c),d)+e", TestName = "ParserGolden.CommaBeforeAddition")]
        [TestCase("2072cb8131a4ae2b.js", "void((a)?b:1)", "void(a?b:(1))", TestName = "ParserGolden.VoidConditional")]
        [TestCase("33720cee3dabde0d.js", "a||b&&c", "a||(b&&c)", TestName = "ParserGolden.LogicalPrecedence")]
        public void ReviewedExpressionPairs(string file, string expected, string explicitExpected)
        {
            var corpus = Corpus.Value;
            var test = corpus.Cases.Single(item => item.Path == "ParserTests/pass/" + file);
            Assert.Multiple(() =>
            {
                foreach (var (path, golden) in new[] { (test.Path, expected), (test.PairPath, explicitExpected) })
                {
                    var result = Uglify.Js(corpus.Text(path), path,
                        ConformanceRunner.Settings(ConformanceProfile.NoOptimization, test.Mode));
                    Assert.That(result.HasErrors, Is.False, () => path + "\n" + ConformanceRunner.Describe(result));
                    Assert.That(result.Code, Is.EqualTo(golden), path + "\nCheck: reviewed output");
                }
            });
        }
    }
}
