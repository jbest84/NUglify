using NUnit.Framework;

namespace NUglify.Tests.Css
{
    [TestFixture]
    public class MathFunctions
    {
        [TestCase("abs(10px var(--operator) -1px)")]
        [TestCase("abs(10px var(--operator) +1px)")]
        [TestCase("abs(var(--empty,)-1px)")]
        [TestCase("abs(var(--empty,)+1px)")]
        [TestCase("abs(10px var(--operator) -.5px)")]
        public void PreservesSignedNumbersFollowingVariableOperators(string expression)
        {
            var result = Uglify.Css(".x { width: " + expression + "; }");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain(expression));
        }

        [TestCase("abs(var(--empty,)10px)")]
        [TestCase("min(var(--empty,)0px,1px)")]
        public void AllowsAdjacentOperandsAfterVariableSubstitution(string expression)
        {
            var result = Uglify.Css(".x { width: " + expression + "; }");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain(expression));
        }

        [Test]
        public void PreservesBinaryOperatorWhitespaceInVariableMath()
        {
            var result = Uglify.Css(".x { width: calc(var(--length) - 1px + 2px); }");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain("calc(var(--length) - 1px + 2px)"));
        }

        [Test]
        public void AllowsMathArgumentsCompletedByVariableSubstitution()
        {
            var result = Uglify.Css(".x { --empty: ; width: abs(var(--empty) 10px); }");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain("abs(var(--empty) 10px)"));
        }

        [Test]
        public void PreservesArbitraryCustomPropertyFunctionsAndCasing()
        {
            var result = Uglify.Css(".x { --payload: ROUND(\"anything\"); }");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain("ROUND(\"anything\")"));
        }

        [TestCase(".x{--x:CALC(\"anything\");}", "CALC(\"anything\")")]
        [TestCase(".x{--x:ROUND(FOO([1,2]));}", "ROUND(FOO([1,2]))")]
        [TestCase(".x{--x:calc(1px + 0px);}", "calc(1px + 0px)")]
        [TestCase(".x{--x:MIN(0px, 2px);width:0px;}", "MIN(0px, 2px)")]
        [TestCase(".x{--x:calc(1px + 0px);width:0px;}", "width:0}")]
        public void PreservesCustomPropertyMathTokens(string css, string expected)
        {
            var result = Uglify.Css(css);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain(expected));
        }

        [TestCase("abs(10px var(--empty))")]
        [TestCase("min(var(--empty) 0px, 2px)")]
        [TestCase("calc(var(--empty) 0px + 1px)")]
        [TestCase("round(nearest, var(--empty) 0px, 1px)")]
        [TestCase("calc(10px var(--operator) 1px)")]
        public void PreservesVariableDependentMath(string expression)
        {
            var result = Uglify.Css(".x{width:" + expression + "}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain("var(--"));
            Assert.That(result.Code, Does.Contain("0px").Or.Contain("10px"));
        }

        [Test]
        public void PreservesNestedMathAndBinaryOperatorWhitespace()
        {
            const string css = ".frame { font-size: min(var(--max), max(var(--fit), var(--min) - clamp(0px, (var(--fit) - var(--min)) * 1000, 1px) * 1000)); }";
            var result = Uglify.Css(css);
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain("clamp(0px,"));
            Assert.That(result.Code, Does.Contain("var(--min) - clamp("));
            Assert.That(result.Code, Does.Contain("var(--fit) - var(--min)"));
        }

        [TestCase("width:min(0px,1px)", "min(0px,1px)")]
        [TestCase("width:calc(min(1px,2px) + 0px)", " + 0px")]
        [TestCase("width:calc(calc(1px + 1px) + 0px)", " + 0px")]
        [TestCase("width:round(nearest, 0%, 1%)", "0%")]
        [TestCase("width:mod(0px, 1px)", "0px")]
        [TestCase("width:rem(0px, 1px)", "0px")]
        [TestCase("width:abs(0px)", "0px")]
        [TestCase("opacity:sign(0px)", "0px")]
        [TestCase("opacity:sin(0deg)", "0deg")]
        [TestCase("animation-delay:max(0s, 1s)", "0s")]
        [TestCase("width:round(1px + 0px, 1px)", "1px + 0px")]
        [TestCase("--length:0px; width:calc(var(--length) + 1px)", "--length:0px")]
        [TestCase("--length:/*!keep*/0px; width:calc(var(--length) + 1px)", "0px")]
        public void PreservesTypedMathValues(string declaration, string expected)
        {
            var result = Uglify.Css(".x{" + declaration + "}");
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Code, Does.Contain(expected));
        }
    }
}
