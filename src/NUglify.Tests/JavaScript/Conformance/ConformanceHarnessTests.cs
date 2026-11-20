using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUglify.JavaScript;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    [TestFixture, Category("JavaScriptConformance")]
    internal sealed class ConformanceHarnessTests
    {
        static CorpusCase Metadata(params string[] flags) => new CorpusCase
        {
            Path = "test/language/example.js", Group = "harness", Flags = flags, ParseExpectation = "accept"
        };

        [TestCase("", "non-strict,strict")]
        [TestCase("onlyStrict", "strict")]
        [TestCase("noStrict", "non-strict")]
        [TestCase("module", "module")]
        [TestCase("raw", "raw")]
        [TestCase("async", "non-strict,strict")]
        public void MetadataSelectsTheRequiredVariants(string flag, string expected)
        {
            var test = Metadata(flag.Length == 0 ? Array.Empty<string>() : new[] { flag });
            ConformanceCorpus.ValidateMetadata(test);
            Assert.That(string.Join(",", test.Variants()), Is.EqualTo(expected));
            Assert.That(test.SourceMode, Is.EqualTo(flag == "module" ? JavaScriptSourceMode.Module : JavaScriptSourceMode.Program));
        }

        [Test]
        public void RawSourceIsNotPrefixedOrNormalized()
        {
            var test = Metadata("raw");
            const string source = "#! comment\r\nvar x = '\u2028';\r\n";
            Assert.That(test.Expand(source, "raw"), Is.EqualTo(source));
            Assert.That(Metadata().Expand(source, "strict"), Is.EqualTo("\"use strict\";\n" + source));
            Assert.Throws<InvalidDataException>(() => test.Expand(source, "strict"));
        }

        [Test]
        public void DependencyFixturesNeverBecomeStandaloneTests()
        {
            var test = Metadata("module");
            test.Path = "test/language/example_FIXTURE.js";
            Assert.That(test.Variants(), Is.Empty);
            Assert.Throws<InvalidDataException>(() => ConformanceCorpus.ValidateMetadata(test));
        }

        [TestCase("runtime")]
        [TestCase("resolution")]
        public void NonParseNegativeMetadataStillRequiresValidSyntax(string phase)
        {
            var test = Metadata();
            test.NegativePhase = phase;
            test.NegativeType = "SyntaxError";
            ConformanceCorpus.ValidateMetadata(test);
            test.ParseExpectation = "reject";
            Assert.Throws<InvalidDataException>(() => ConformanceCorpus.ValidateMetadata(test));
        }

        [TestCase(ConformanceProfile.NoOptimization)]
        [TestCase(ConformanceProfile.Minify)]
        public void SyntaxChecksDoNotExecuteRuntimeNegativeCode(ConformanceProfile profile)
        {
            var result = ConformanceRunner.Parse("throw new Error('must not execute');", profile, JavaScriptSourceMode.Program);
            Assert.That(result.HasErrors, Is.False, () => ConformanceRunner.Describe(result));
        }

        [Test]
        public void ProfilesUseFreshSettingsAndDisablePreprocessing()
        {
            var plain = ConformanceRunner.Settings(ConformanceProfile.NoOptimization, JavaScriptSourceMode.Program);
            var minify = ConformanceRunner.Settings(ConformanceProfile.Minify, JavaScriptSourceMode.Module);
            Assert.Multiple(() =>
            {
                Assert.That(plain.MinifyCode, Is.False);
                Assert.That(plain.LocalRenaming, Is.EqualTo(LocalRenaming.KeepAll));
                Assert.That(plain.EvalLiteralExpressions, Is.False);
                Assert.That(minify.MinifyCode, Is.True);
                Assert.That(minify.SourceMode, Is.EqualTo(JavaScriptSourceMode.Module));
                Assert.That(plain.IgnorePreprocessorDefines && minify.IgnorePreprocessorDefines, Is.True);
                Assert.That(plain.IgnoreConditionalCompilation && minify.IgnoreConditionalCompilation, Is.True);
                Assert.That(plain.StripDebugStatements || minify.StripDebugStatements, Is.False);
                Assert.That(plain.StrictMode || minify.StrictMode, Is.False, "The harness must not mask module/directive bugs by forcing strict mode.");
            });
            plain.StrictMode = true;
            Assert.That(ConformanceRunner.Settings(ConformanceProfile.NoOptimization, JavaScriptSourceMode.Program).StrictMode, Is.False);
        }

        [TestCase(ConformanceProfile.NoOptimization)]
        [TestCase(ConformanceProfile.Minify)]
        public void MalformedOutputFailsTheOutputCheck(ConformanceProfile profile)
        {
            var result = ConformanceRunner.Parse("function f() {", profile, JavaScriptSourceMode.Program);
            Assert.That(result.HasErrors, Is.True);
            Assert.Throws<AssertionException>(() => ConformanceRunner.Verify("accept", result.HasErrors ? "reject" : "accept",
                result, "Check: output; malformed emitted source"));
        }

        [TestCase(ConformanceProfile.NoOptimization)]
        [TestCase(ConformanceProfile.Minify)]
        public void WarningsDoNotCountAsSyntaxRejection(ConformanceProfile profile)
        {
            var warnings = ConformanceRunner.Parse("unresolved();", profile, JavaScriptSourceMode.Program);
            Assert.That(warnings.Errors.Any(error => !error.IsError), Is.True, "Exercise a real compiler warning.");
            Assert.That(warnings.HasErrors, Is.False);
            ConformanceRunner.Verify("accept", warnings.HasErrors ? "reject" : "accept", warnings, "warning only");
        }

        [TestCase(ConformanceProfile.NoOptimization)]
        [TestCase(ConformanceProfile.Minify)]
        public void TemplateCodePointsAllowSixDigitsAndLeadingZeroes(ConformanceProfile profile)
        {
            var result = ConformanceRunner.Parse(@"consume(`\u{10ffff}\u{00000010ffff}\uD800`);", profile, JavaScriptSourceMode.Program);
            Assert.That(result.HasErrors, Is.False, () => ConformanceRunner.Describe(result));
        }

        [TestCase(@"`\u{}`;")]
        [TestCase(@"`\u{110000}`;")]
        [TestCase(@"`\u{000000110000}`;")]
        [TestCase(@"tag`\xG${`\u{}`}`;")]
        public void UntaggedTemplateErrorsSurviveNestedTaggedTemplates(string source)
        {
            foreach (var profile in Enum.GetValues<ConformanceProfile>())
            {
                var result = ConformanceRunner.Parse(source, profile, JavaScriptSourceMode.Program);
                Assert.That(result.HasErrors, Is.True, $"Expected an invalid untagged template in {source} under {profile}.");
            }
        }

        [TestCase("a ?? b || c;", false)]
        [TestCase("a || b ?? c;", false)]
        [TestCase("a ?? b && c;", false)]
        [TestCase("a && b ?? c;", false)]
        [TestCase("consume((a ?? b) || c);", true)]
        [TestCase("consume(a || (b ?? c));", true)]
        [TestCase("consume((a ?? b) && c);", true)]
        [TestCase("consume(a && (b ?? c));", true)]
        [TestCase("(obj?.value).field = value;", true)]
        [TestCase("(obj?.value).field++;", true)]
        [TestCase("(obj?.value) = value;", false)]
        [TestCase("obj?.value.field = value;", false)]
        [TestCase("obj?.value().field++;", false)]
        [TestCase("getObject(obj?.value).field = value;", true)]
        [TestCase("obj[other?.key] = value;", true)]
        [TestCase("(obj?.tag)`text`;", true)]
        [TestCase("obj?.tag.member`text`;", false)]
        [TestCase("(obj?.tag).member`text`;", true)]
        [TestCase("obj[other?.key]`text`;", true)]
        [TestCase("for (var value = 0 of items) use(value);", false)]
        [TestCase("for (var value = 0 in items) use(value);", true)]
        [TestCase("for (let value = 0 in items) use(value);", false)]
        [TestCase("for (var value of first, second) use(value);", false)]
        [TestCase("for (var value in first, second) use(value);", true)]
        [TestCase("function f(){throw;}", false)]
        public void GrammarRestrictionsPreserveExpressionBoundaries(string source, bool valid)
        {
            foreach (var profile in Enum.GetValues<ConformanceProfile>())
            {
                var input = ConformanceRunner.Parse(source, profile, JavaScriptSourceMode.Program);
                Assert.That(input.HasErrors, Is.EqualTo(!valid),
                    () => $"{source}\nProfile: {profile}\n{ConformanceRunner.Describe(input)}");
                if (valid)
                {
                    var emitted = Uglify.Js(source, ConformanceRunner.Settings(profile, JavaScriptSourceMode.Program));
                    Assert.That(emitted.HasErrors, Is.False, () => ConformanceRunner.Describe(emitted));
                    var output = ConformanceRunner.Parse(emitted.Code, profile, JavaScriptSourceMode.Program);
                    Assert.That(output.HasErrors, Is.False, () => emitted.Code + "\n" + ConformanceRunner.Describe(output));
                }
            }
        }

        [TestCase("let x=1;function f(){var yield=fn;return yield(x);}", true)]
        [TestCase("var await=fn;await(value);", true)]
        [TestCase("function* g(){yield 1;function f(){return yield(1);}}", true)]
        [TestCase("async function f(){await value;function g(){return await(value);}}", true)]
        [TestCase("function* g(a=yield 1){}", false)]
        [TestCase("async function f(a=await value){}", false)]
        [TestCase("function* g(a=function(){return yield(1);}){yield a;}", true)]
        [TestCase("async function f(a=function(){return await(value);}){await a;}", true)]
        [TestCase("function* g(){return ()=>yield 1;}", false)]
        [TestCase("async function f(){return ()=>await value;}", false)]
        [TestCase("var f=async()=>await value;", true)]
        [TestCase("for await(var x of items){use(x);}", false)]
        [TestCase("async function f(){for await(var x of items){use(x);}}", true)]
        [TestCase("new.target;", false)]
        [TestCase("(()=>new.target)();", false)]
        [TestCase("function f(){return ()=>new.target;}", true)]
        [TestCase("class C { value=new.target;static {use(new.target);} }", true)]
        [TestCase("class C {[new.target](){}}", false)]
        [TestCase("function f(){return class {[new.target](){}};}", true)]
        [TestCase("class C {static {class D {[arguments](){}}}}", false)]
        [TestCase("class C {static {class D {[arguments]=1;}}}", false)]
        [TestCase("class C {static {class D {m(){return arguments;}}}}", true)]
        [TestCase("class C {static {var f=()=>arguments;}}", false)]
        [TestCase("class C {static {function f(){return arguments;}}}", true)]
        [TestCase("class C {static {class D extends pick(arguments){}}}", false)]
        [TestCase("class C extends B {'constructor'(){super();}}", true)]
        [TestCase("class C extends B {['constructor'](){super();}}", false)]
        [TestCase("class C extends B {constructor(){var f=()=>super();f();}}", true)]
        [TestCase("class C extends B {constructor(){function f(){super();}}}", false)]
        [TestCase("class C extends B {value=super.value;static {use(super.value);}}", true)]
        [TestCase("class C extends B {m(){function f(){return super.value;}}}", false)]
        [TestCase("var obj={m(){return super.value;}};", true)]
        [TestCase("'use strict';for(var x=0 in obj)use(x);", false)]
        [TestCase("function f(){'use strict';for(var x=0 in obj)use(x);}", false)]
        [TestCase("'use strict';for(var x=0;x<1;x++)use(x);", true)]
        [TestCase("var f=(a=1)=>{'use\\x20strict';return a;};", true)]
        [TestCase("var f=(a=1)=>{'use strictfoo';return a;};", true)]
        [TestCase("{'use strict';with(obj){use(obj);}}", true)]
        [TestCase("var f=(a=1)=>{/*! comment */'use strict';return a;};", false)]
        [TestCase("function f(a,a){return a;}", true)]
        [TestCase("function f(a,a){'use strict';return a;}", false)]
        [TestCase("try{run();}catch(e){var e=1;use(e);}", true)]
        [TestCase("try{run();}catch(e){let e=1;use(e);}", false)]
        [TestCase("try{run();}catch(e){let x;var x;}", false)]
        [TestCase("try{run();}catch({e}){var e;}", false)]
        [TestCase("for(let x of items){let x;use(x);}", true)]
        [TestCase("for(let x of items){var x;use(x);}", false)]
        [TestCase("for(let x=0;x<1;++x){let x;use(x);}", true)]
        [TestCase("for(let x in obj){let x;use(x);}", true)]
        public void ContextValidationRespectsLexicalBoundaries(string source, bool valid)
        {
            foreach (var profile in Enum.GetValues<ConformanceProfile>())
            {
                var input = ConformanceRunner.Parse(source, profile, JavaScriptSourceMode.Program);
                Assert.That(input.HasErrors, Is.EqualTo(!valid),
                    () => $"{source}\nProfile: {profile}\n{ConformanceRunner.Describe(input)}");
                if (valid)
                {
                    var emitted = Uglify.Js(source, ConformanceRunner.Settings(profile, JavaScriptSourceMode.Program));
                    Assert.That(emitted.HasErrors, Is.False, () => ConformanceRunner.Describe(emitted));
                    var output = ConformanceRunner.Parse(emitted.Code, profile, JavaScriptSourceMode.Program);
                    Assert.That(output.HasErrors, Is.False, () => emitted.Code + "\n" + ConformanceRunner.Describe(output));
                }
            }
        }

        [TestCase("await value;export {};", true)]
        [TestCase("for await(var x of items){use(x);}export {};", true)]
        [TestCase("for(var x=0 in obj)use(x);export {};", false)]
        [TestCase("new.target;export {};", false)]
        public void ModuleContextsAreStrict(string source, bool valid)
        {
            foreach (var profile in Enum.GetValues<ConformanceProfile>())
            {
                var result = ConformanceRunner.Parse(source, profile, JavaScriptSourceMode.Module);
                Assert.That(result.HasErrors, Is.EqualTo(!valid), () => ConformanceRunner.Describe(result));
            }
        }

        [Test]
        public void ReusingAParserResetsValidationAndOperatorContexts()
        {
            var parser = new JSParser();
            var errors = new List<UglifyError>();
            parser.CompilerError += (sender, args) => { if (args.Error.IsError) errors.Add(args.Error); };
            var settings = ConformanceRunner.Settings(ConformanceProfile.NoOptimization, JavaScriptSourceMode.Program);
            parser.Parse("function* g(){yield 1;} await value;", settings);
            Assert.That(errors, Is.Not.Empty);
            errors.Clear();
            parser.Parse("let x=1;yield(x);", settings);
            Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
        }
    }
}
