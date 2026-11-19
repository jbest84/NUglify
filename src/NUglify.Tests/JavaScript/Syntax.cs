// Syntax.cs
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
    /// Summary description for Syntax
    /// </summary>
    [TestFixture]
    public class Syntax
    {
        [Test]
        public void NestedBlocks()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void BOM()
        {
            TestHelper.Instance.RunTest("-term");
        }

        [Test]
        public void SlashSpacing()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void EmptyStatement()
        {
            // don't change if-statements to expressions
            TestHelper.Instance.RunTest("-kill:0x800001000");
        }

        [Test]
        public void EmptyStatement_P()
        {
            TestHelper.Instance.RunTest("-P");
        }

        [Test]
        public void ES6()
        {
            // no errors
            TestHelper.Instance.RunErrorTest("-rename:none");
        }

        [Test]
        public void ES6_h()
        {
            // no errors
            TestHelper.Instance.RunErrorTest();
        }

        [Test]
        public void NonES6Yield()
        {
            // Sloppy identifiers remain valid, but the obsolete generator without '*' is rejected.
            TestHelper.Instance.RunErrorTest(JSError.SyntaxError);
        }

        [Test]
        public void NonES6Yield_norename()
        {
            TestHelper.Instance.RunErrorTest("-rename:none", JSError.SyntaxError);
        }

        [Test]
        public void BindingPatterns()
        {
            // no errors other than unreferenced arguments
            TestHelper.Instance.RunErrorTest("-ignore:JS1270,JS1268", JSError.BindingPatternRequiresInitializer);
        }

        [Test]
        public void MissingMemberRoot()
        {
            // ignore the undefined variable, but make sure there's an expected-expression error
            TestHelper.Instance.RunErrorTest("-ignore:js1135", JSError.ExpressionExpected);
        }
    }
}
