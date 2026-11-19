// LexicalDeclarations.cs
//
// Copyright 2012 Microsoft Corporation
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
    /// Summary description for LexicalDeclarations
    /// </summary>
    [TestFixture]
    public class LexicalDeclarations
    {
        [Test]
        public void LexConst()
        {
            // we have one undeclared variable to make sure it doesn't resolve to the lexical scope
            TestHelper.Instance.RunErrorTest("-rename:all", JSError.UndeclaredVariable);
        }

        [Test]
        public void LexConst_mozilla()
        {
            TestHelper.Instance.RunErrorTest("-rename:all -const:moz");
        }

        [Test]
        public void LexConst_ES6()
        {
            TestHelper.Instance.RunErrorTest("-rename:all -const:ES6", JSError.UndeclaredVariable);
        }

        [Test]
        public void LexLet()
        {
            // we have one undeclared variable to make sure it doesn't resolve to the lexical scope
            TestHelper.Instance.RunErrorTest("-rename:all", JSError.UndeclaredVariable);
        }

        [Test]
        public void LexFor()
        {
            TestHelper.Instance.RunErrorTest("-rename:all");
        }

        [Test]
        public void LexForIn()
        {
            TestHelper.Instance.RunErrorTest("-rename:all");
        }

        [Test]
        public void LexSwitch()
        {
            TestHelper.Instance.RunErrorTest("-rename:all");
        }

        [Test]
        public void LexBlock()
        {
            TestHelper.Instance.RunErrorTest("-rename:all");
        }

        [Test]
        public void LexDuplicate()
        {
            // The loop body's const may shadow its loop-head let in a separate block.
            TestHelper.Instance.RunErrorTest("-rename:all", JSError.DuplicateLexicalDeclaration);
        }

        [Test]
        public void ConstAssign()
        {
            TestHelper.Instance.RunErrorTest("-rename:all", JSError.AssignmentToConstant, JSError.AssignmentToConstant);
        }

        [Test]
        public void LexVarCollision()
        {
            TestHelper.Instance.RunErrorTest("-rename:all", JSError.DuplicateLexicalDeclaration, JSError.DuplicateLexicalDeclaration);
        }
    }
}
