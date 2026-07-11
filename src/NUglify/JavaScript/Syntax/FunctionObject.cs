// functionobject.cs
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

using System.Collections.Generic;
using NUglify.Helpers;
using NUglify.JavaScript.Visitors;

namespace NUglify.JavaScript.Syntax
{
    public class FunctionObject : AstNode
    {
	    BindingIdentifier m_binding;
	    ObjectLiteralField m_literalName;
	    AstNodeList m_parameters;
	    BlockStatement m_body;

        public bool IsStatic { get; set; }

        public SourceContext StaticContext { get; set; }

        public BindingIdentifier Binding
        {
            get => m_binding;
            set => ReplaceNode(ref m_binding, value);
        }

        public ObjectLiteralField LiteralName
        {
            get => m_literalName;
            set => ReplaceNode(ref m_literalName, value);
        }

        public ArrayLiteral ComputedName { get; set; }

        public string NameGuess { get; set; }

        public AstNodeList ParameterDeclarations
        {
            get => m_parameters;
            set => ReplaceNode(ref m_parameters, value);
        }

        public BlockStatement Body
        {
            get => m_body;
            set => ReplaceNode(ref m_body, value);
        }

        public override bool IsDeclaration
        {
            get
            {
                // this is for determining whether statements after a return/break/continue
                // is a declaration and should be kept, or a statement that can be removed.
                // don't bother checking for getter/setter/method, which should only be inside
                // class delcarations or object literals.
                return FunctionType == FunctionType.Declaration;
            }
        }

        public FunctionType FunctionType { get; set; }

        public override bool IsExpression
        {
            get
            {
                // if this is a declaration or a method, then it's not an expression. 
                return FunctionType != FunctionType.Declaration
                    && FunctionType != FunctionType.Method;
            }
        }

        /// <summary>
        /// Gets or sets whether this function object is a generator
        /// </summary>
        public bool IsGenerator { get; set; }

        /// <summary>
        /// Gets or sets whether this function object is async
        /// </summary>
        public bool IsAsync { get; set; }

        // when parsed, this flag indicates that a function declaration is in the
        // proper source-element location
        public bool IsSourceElement { get; set; }

        public override OperatorPrecedence Precedence
        {
            get
            {
                // arow functions are assignment precedence, function expression are primary
                return FunctionType == FunctionType.ArrowFunction ? OperatorPrecedence.Assignment : OperatorPrecedence.Primary;
            }
        }

        public FunctionObject(SourceContext functionContext)
            : base(functionContext)
        {
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        /// <summary>
        /// Check to see if this function is referenced. Perform a cyclic check
        /// because being referenced by an unreferenced function is still unreferenced.
        /// </summary>
        public bool IsReferenced
        {
            get
            {
                // call the checking method with a new empty hashset so it doesn't
                // go in an endless circle
                return SafeIsReferenced(new HashSet<FunctionObject>());
            }
        }

        bool SafeIsReferenced(HashSet<FunctionObject> visited)
        {
            // if we've already been here, don't go in a circle
            if (!visited.Contains(this))
            {
                // add us to the visited list
                visited.Add(this);

                if (FunctionType == FunctionType.Declaration)
                {
                    // this is a function declaration, so it better have it's variable field set.
                    // if the variable (and therefore the function) is defined in the global scope,
                    // or if it's exported from inside a module, then consider it possibly referenced
                    // from outside code, whether or not it actually is from inside our own code.
                    // Binding will be null for computed property names, which can only exist on classes or object literals, which therefore wont be in the global scope
                    if (Binding?.VariableField?.FieldType == FieldType.Global || Binding?.VariableField?.IsExported == true)
                    {
                        return true;
                    }

                    // not defined in the global scope. Check its references.
                    // Binding will be null for computed property names, which we cant reference check
                    if(Binding != null)
                    {
	                    foreach (var reference in Binding.VariableField.References)
	                    {
		                    var referencingScope = reference.VariableScope;
		                    if (referencingScope == null || referencingScope is GlobalScope)
		                    {
			                    // referenced by a lookup in the global scope -- we're good to go.
			                    return true;
		                    }
		                    else
		                    {
			                    var functionObject = referencingScope.Owner as FunctionObject;
			                    if (functionObject != null && functionObject.SafeIsReferenced(visited))
			                    {
				                    // as soon as we find one that's referenced, we stop
				                    return true;
			                    }
		                    }
	                    }
                    }
                }
                else
                {
                    // expressions are always referenced
                    return true;
                }
            }

            // if we get here, we aren't referenced by anything that's referenced
            return false;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Binding, LiteralName, ParameterDeclarations, Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Binding == oldNode)
            {
                Binding = newNode as BindingIdentifier;
                return true;
            }
            else if (LiteralName == oldNode)
            {
                LiteralName = newNode as ObjectLiteralField;
                return true;
            }
            else if (Body == oldNode)
            {
                Body = ForceToBlock(newNode);
                return true;
            }
            else if (ParameterDeclarations == oldNode)
            {
                return (newNode as AstNodeList).IfNotNull(list =>
                    {
                        ParameterDeclarations = list;
                        return true;
                    });
            }

            return false;
        }
    }

    public enum FunctionType
    {
        Declaration,
        Expression,
        Getter,
        Setter,
        ArrowFunction,
        Method
    }
}
