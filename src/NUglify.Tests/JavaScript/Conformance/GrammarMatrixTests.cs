using System;
using System.Collections.Generic;
using System.Linq;
using NUglify.JavaScript;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    internal sealed class GrammarCase
    {
        public string Id { get; set; }
        public string Variant { get; set; }
        public string Source { get; set; }
        public JavaScriptSourceMode Mode { get; set; }
        public string Expectation { get; set; }
        public string NoOptimizationGolden { get; set; }
        public string MinifyGolden { get; set; }
    }

    [TestFixture, Category("JavaScriptConformance")]
    internal sealed class GrammarMatrixTests
    {
        static readonly (string Id, string Prefix, string Suffix, JavaScriptSourceMode Mode)[] Contexts =
        {
            ("script", "", "", JavaScriptSourceMode.Program),
            ("strict-script", "\"use strict\";\n", "", JavaScriptSourceMode.Program),
            ("module", "", "", JavaScriptSourceMode.Module),
            ("function", "function context() {\n", "\n}", JavaScriptSourceMode.Program),
            ("strict-function", "function context() {\n\"use strict\";\n", "\n}", JavaScriptSourceMode.Program),
            ("async-function", "async function context() {\n", "\n}", JavaScriptSourceMode.Program),
            ("generator-function", "function* context() {\n", "\n}", JavaScriptSourceMode.Program),
        };

        // Validity columns: script, strict-script, module, function, strict-function,
        // async-function, generator-function. Each outcome is explicit.
        static readonly (string Id, string Source, bool[] Valid)[] Snippets =
        {
            ("return", "return 1;", new[] { false, false, false, true, true, true, true }),
            ("yield", "yield 1;", new[] { false, false, false, false, false, false, true }),
            ("await", "await value;", new[] { false, false, true, false, false, true, false }),
            ("with", "with (obj) { use(obj); }", new[] { true, false, false, true, false, true, true }),
        };

        static readonly (string Id, string Text)[] Separators =
        {
            ("lf", "\n"),
            ("crlf", "\r\n"),
            ("cr", "\r"),
            ("line-separator", "\u2028"),
            ("paragraph-separator", "\u2029"),
            ("comment-lf", "/*\n*/"),
            ("comment-crlf", "/*\r\n*/"),
            ("comment-line-separator", "/*\u2028*/"),
            ("comment-paragraph-separator", "/*\u2029*/"),
        };

        static readonly (string Id, string Prefix, string Suffix, bool Valid,
            string NoOptimizationGolden, string MinifyGolden)[] LineCases =
        {
            ("return-asi", "function f(){return", "value;}", true, "function f(){return;value}", "function f(){return}"),
            ("throw-line-terminator", "function f(){throw", "value;}", false, null, null),
            ("arrow-block-asi", "var f = () => {}", "(() => {})();", true, "var f=()=>{};(()=>{})()", "var f=()=>{};(()=>{})()"),
            ("arrow-expression-continuation", "var f = () => value", "(arg);", true, "var f=()=>value(arg)", "var f=()=>value(arg)"),
            ("arrow-parameter-line-terminator", "var f = ()", "=> {};", false, null, null),
            ("postfix-line-terminator", "function f(){value", "++other;}", true, "function f(){value;++other}", "function f(){value;++other}"),
        };

        static readonly GrammarCase[] Inputs = Expand().ToArray();

        static IEnumerable<GrammarCase> Expand()
        {
            foreach (var snippet in Snippets)
                for (var i = 0; i < Contexts.Length; i++)
                {
                    var context = Contexts[i];
                    yield return new GrammarCase
                    {
                        Id = "context/" + snippet.Id, Variant = context.Id, Mode = context.Mode,
                        Source = context.Prefix + snippet.Source + context.Suffix,
                        Expectation = snippet.Valid[i] ? "accept" : "reject"
                    };
                }
            foreach (var line in LineCases)
                foreach (var separator in Separators)
                    yield return new GrammarCase
                    {
                        Id = "newline/" + line.Id, Variant = separator.Id, Mode = JavaScriptSourceMode.Program,
                        Source = line.Prefix + separator.Text + line.Suffix,
                        Expectation = line.Valid ? "accept" : "reject",
                        NoOptimizationGolden = line.NoOptimizationGolden, MinifyGolden = line.MinifyGolden
                    };
        }

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var test in Inputs)
                foreach (var profile in Enum.GetValues<ConformanceProfile>())
                {
                    var data = new TestCaseData(test.Id, test.Variant, profile).SetName($"Grammar.{test.Id}.{test.Variant}.{profile}");
                    yield return data;
                }
        }

        [OneTimeSetUp]
        public void ReportCoverage()
        {
            TestContext.Progress.WriteLine($"Grammar matrices: {Contexts.Length} contexts x {Snippets.Length} snippets; "
                + $"{Separators.Length} separator forms x {LineCases.Length} newline rules; "
                + $"{Inputs.Length * 2} profile cases.");
        }

        [TestCaseSource(nameof(Cases))]
        public void ContextAndLineTerminator(string id, string variant, ConformanceProfile profile)
        {
            var test = Inputs.Single(item => item.Id == id && item.Variant == variant);
            var input = ConformanceRunner.Parse(test.Source, profile, test.Mode, id + "." + variant);
            var context = $"{id}\nVariant: {variant}; profile: {profile}; mode: {test.Mode}\nSource:\n{test.Source}";
            ConformanceRunner.Verify(test.Expectation, input.HasErrors ? "reject" : "accept", input, context + "\nCheck: parse");
            if (test.Expectation == "reject") return;
            var emitted = Uglify.Js(test.Source, ConformanceRunner.Settings(profile, test.Mode));
            var output = emitted.HasErrors ? emitted : ConformanceRunner.Parse(emitted.Code, profile, test.Mode, id + ".output");
            ConformanceRunner.Verify("accept", emitted.HasErrors ? "emit-errors" : output.HasErrors ? "reject" : "accept",
                output, context + "\nCheck: output\nEmitted source:\n" + emitted.Code);
            var golden = profile == ConformanceProfile.NoOptimization ? test.NoOptimizationGolden : test.MinifyGolden;
            if (golden != null)
                Assert.That(emitted.Code, Is.EqualTo(golden), $"Golden output for {id}/{variant}/{profile} must preserve the line-terminator rule.");
        }
    }
}
