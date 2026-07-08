// Literals.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using NUglify.JavaScript;
using NUglify.Tests.JavaScript.Common;
using NUnit.Framework;

namespace NUglify.Tests.JavaScript
{
    /// <summary>
    /// Summary description for Literals
    /// </summary>
    [TestFixture]
    public class Literals
    {
        [Test]
        public void Boolean()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Number()
        {
            TestHelper.Instance.RunErrorTest(JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.OctalLiteralsDeprecated, JSError.NumericMaximum, JSError.NumericMaximum);
        }

        [Test]
        public void Strings()
        {
            TestHelper.Instance.RunTest("-inline:N -enc:out ascii -unused:keep");
        }

        [Test]
        public void Strings_utf()
        {
            TestHelper.Instance.RunTest("-inline:F -enc:out utf-8 -unused:keep");
        }

        [Test]
        public void Strings_h()
        {
            TestHelper.Instance.RunTest("-inline:false -rename:all -enc:out ascii   -unused:keep");
        }

        [Test]
        public void Strings_k()
        {
            TestHelper.Instance.RunTest("-inline:true -enc:out ascii -unused:keep");
        }

        [Test]
        public void Combined_h()
        {
            TestHelper.Instance.RunTest("-rename:all");
        }

        [Test]
        public void Combined_hc()
        {
            TestHelper.Instance.RunTest("-rename:all -literals:combine");
        }

        [Test]
        public void NestedCombine()
        {
            TestHelper.Instance.RunTest("-rename:all -literals:combine");
        }

        [Test]
        public void ArrayLiteral()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void ObjectLiteral()
        {
            TestHelper.Instance.RunTest("-enc:out ascii");
        }

        [Test]
        public void ObjectLiteral_quote()
        {
            TestHelper.Instance.RunTest("-obj:quote");
        }

        [Test]
        public void InlineSafe()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void InlineSafe_no()
        {
            TestHelper.Instance.RunTest("-inline:no");
        }

        [Test]
        public void CombineNegs()
        {
            TestHelper.Instance.RunTest("-literals:combine -rename:all");
        }

        [Test]
        public void StrictEncoding()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Member()
        {
            TestHelper.Instance.RunTest("-ignore:JS1306");
        }

        [Test]
        public void Member_Preserve()
        {
            TestHelper.Instance.RunTest("-kill:0x200000 -ignore:JS1306");
        }

        [Test]
        public void GetterSetter()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void AspNetString()
        {
            TestHelper.Instance.RunTest("-aspnet");
        }

        [Test]
        public void InlineSafeErrors()
        {
            // default test - should have no errors
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void InlineSafeErrors_on()
        {
            // turn on the error checking -- should be two errors
            TestHelper.Instance.RunErrorTest("-inline:force", JSError.StringNotInlineSafe, JSError.StringNotInlineSafe);
        }

        [Test]
        public void NullCharacter()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Octal()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void RegExp()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Replace()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void TemplateLiterals()
        {
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void TemplateLiteralsEscaped()
        {
	        TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void TemplateLiterals_nomin()
        {
            // don't minify the string or template literals
            TestHelper.Instance.RunErrorTest("-kill:0x100000");
        }

        [Test]
        public void LoneHighSurrogate()
        {
            TestHelper.Instance.RunErrorTest(JSError.HighSurrogate);
        }

        [Test]
        public void LoneLowSurrogate()
        {
            TestHelper.Instance.RunErrorTest(JSError.LowSurrogate);
        }
    }
}
