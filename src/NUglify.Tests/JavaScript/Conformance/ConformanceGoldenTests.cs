using System.Collections.Generic;
using NUglify.JavaScript;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript.Conformance
{
    [TestFixture, Category("JavaScriptConformance")]
    internal sealed class ConformanceGoldenTests
    {
        // Local, reviewed expectations: these check emitted structure, not runtime assertions.
        public static IEnumerable<TestCaseData> Cases()
        {
            yield return Golden("ArrowBlockBeforeIife",
                "var f = () => { console.log(1) }\n(() => { console.log(2) })();",
                "var f=()=>{console.log(1)};(()=>{console.log(2)})()");
            yield return Golden("ArrowBlockBeforeCommentLineBreak",
                "window.foo = () => { console.log(1) }/*\n*/(() => { console.log(2) })();",
                "window.foo=()=>{console.log(1)};(()=>{console.log(2)})()");
            yield return Golden("ArrowExpressionContinuation",
                "var f = () => g\n(h)();", "var f=()=>g(h)()");
            yield return Golden("OptionalChainEndsBeforeMember",
                "console.log((o?.a).b);", "console.log((o?.a).b)");
            yield return Golden("OptionalChainEndsBeforeIndex",
                "console.log((o?.a)[key]);", "console.log((o?.a)[key])");
            yield return Golden("TaggedTemplateRawEscapes",
                @"tag`a\x41${value}\u0042`;", @"tag`a\x41${value}\u0042`");
            yield return Golden("TaggedTemplateLegacyEscape",
                @"tag`\01`;", @"tag`\01`");
            yield return Golden("EscapedStrictDirectiveRemainsEscaped",
                @"var f=(a=1)=>{'use\x20strict';return a;};", @"var f=(n=1)=>{'use\x20strict';return n}");
            yield return Golden("BlockStringCannotBecomeStrictDirective",
                "{'use strict';with(obj){use(obj);}}", "(\"use strict\");with(obj)use(obj)");
            yield return Golden("ConciseArrowStringIsNotADirective",
                "var f=()=>\"use strict\";", "var f=()=>\"use strict\"");
            yield return Golden("ComputedMethodKeyUsesOuterBinding",
                "let outer='method';class C{[outer](outer){return outer;}}", "let outer=\"method\";class C{[outer](n){return n}}");
            yield return Golden("TaggedTemplateInvalidEscapeSegments",
                @"tag`\u{}${value}\xG${other}\u{110000}`;", @"tag`\u{}${value}\xG${other}\u{110000}`");
            yield return Golden("NestedTaggedTemplateInvalidEscapes",
                @"tag`\u{}${nested`\xG`}\u0`;", @"tag`\u{}${nested`\xG`}\u0`");
            yield return Golden("TaggedTemplateBracedCodePoints",
                @"tag`\u{10ffff}\u{00000010ffff}`;", @"tag`\u{10ffff}\u{00000010ffff}`");
            yield return Golden("ForOfNestedMemberArrayTarget",
                "var obj={};for([obj.value]of items)use(obj);", "var obj={};for([obj.value]of items)use(obj)");
            yield return Golden("ForOfNestedMemberObjectTarget",
                "var obj={};for({value:obj.value}of items)use(obj);", "var obj={};for({value:obj.value}of items)use(obj)");
            yield return Golden("UnusedParameterDefaultSideEffect",
                "function f(a, b = log.push('default')) { log.push('body'); }",
                "function f(n,t=log.push(\"default\")){log.push(\"body\")}");
            yield return Golden("UnusedLabelPreservesLexicalScope",
                "function f(log) { L: { let first = 1; log.push(() => first); } let second = 2; log.push(() => second); return log.map(g => g()); }",
                "function f(n){{let t=1;n.push(()=>t)}let t=2;return n.push(()=>t),n.map(n=>n())}");
            yield return Golden("StaticBlocksHaveSeparateScopes",
                "class A { static { var value = 1; this.x = () => value; } static { let value = 2; this.y = () => value; } }",
                "class A{static{var n=1;this.x=()=>n}static{let n=2;this.y=()=>n}}");
            yield return Golden("CommaCallPreservesUnboundReceiver",
                "\"use strict\"; const o = { m() { return this === o; } }; console.log((0, o.m)());",
                "\"use strict\";const o={m(){return this===o}};console.log((0,o.m)())");
        }

        static TestCaseData Golden(string name, string source, string expected)
            => new TestCaseData(source, expected).SetName("ConformanceGolden." + name);

        [TestCaseSource(nameof(Cases))]
        public void ReviewedMinification(string source, string expected)
        {
            var result = Uglify.Js(source, ConformanceRunner.Settings(ConformanceProfile.Minify, JavaScriptSourceMode.Program));
            Assert.That(result.HasErrors, Is.False, () => ConformanceRunner.Describe(result));
            Assert.That(result.Code, Is.EqualTo(expected));
            var reparsed = ConformanceRunner.Parse(result.Code, ConformanceProfile.Minify, JavaScriptSourceMode.Program);
            Assert.That(reparsed.HasErrors, Is.False, () => ConformanceRunner.Describe(reparsed));
        }
    }
}
