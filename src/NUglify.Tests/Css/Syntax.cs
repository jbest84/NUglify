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

using NUglify.Tests.Css.Common;
using NUnit.Framework;

namespace NUglify.Tests.Css
{
    /// <summary>
    /// Summary description for Syntax
    /// </summary>
    [TestFixture]
    public class Syntax
    {
        [Test]
        public void Strings()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Function()
        {
            TestHelper.Instance.RunTest("-colors:strict");
        }

        [Test]
        public void URI()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Important()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Term()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Expression()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void CDO()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Expr()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void UnicodeRange()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void Escapes()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void AlphaHash()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void FontNames()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void PointZeroEms()
        {
            TestHelper.Instance.RunTest();
        }

        [Test]
        public void RgbWithSpace()
        {
            var retValue = TestHelper.Instance.RunTest();
            Assert.That(retValue == 0, "shouldn't have any errors");
        }


        [Test]
        public void RgbWithFunc()
        {
            var retValue = TestHelper.Instance.RunTest();
            Assert.That(retValue == 0, "shouldn't have any errors");
        }
    }
}
