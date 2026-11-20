using System;
using System.Collections.Generic;
using System.Linq;
using NUglify.JavaScript.Syntax;

namespace NUglify.JavaScript.Visitors
{
    // Validate the original tree even when optimization is disabled. Diagnostics
    // are deferred so existing parser/analyzer diagnostics keep their identity.
    internal sealed class ContextValidationVisitor : TreeVisitor, IDisposable
    {
        struct Context
        {
            public bool Strict, Await, Yield, Return, NewTarget, SuperCall, SuperProperty, ForbidArguments;
        }

        sealed class Violation
        {
            public SourceContext Context;
            public JSError Error;
        }

        readonly JSParser parser;
        readonly List<Violation> violations = new List<Violation>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> requiredErrors = new HashSet<string>(StringComparer.Ordinal);
        Context context;
        bool parameterYield, parameterAwait;

        public ContextValidationVisitor(JSParser parser)
        {
            this.parser = parser;
            context.Strict = parser.Settings.StrictMode;
            parser.ContextValidator = this;
            parser.CompilerError += ObserveError;
        }

        void ObserveError(object sender, ContextErrorEventArgs args)
        {
            var error = args.Error;
            if (error.IsError)
                reported.Add(Key(error.ErrorNumber, error.File, error.StartLine, error.StartColumn));
        }

        static string Key(int error, string file, int startLine, int startColumn)
            => string.Join("|", error, file, startLine, startColumn);

        void Error(SourceContext source, JSError error = JSError.SyntaxError)
        {
            if (source != null)
            {
                violations.Add(new Violation { Context = source.Clone(), Error = error });
                requiredErrors.Add(Key((int)error, source.Document.FileContext, source.StartLineNumber, source.StartColumn + 1));
            }
        }

        public void PromoteDiagnostic(UglifyError error)
        {
            if (requiredErrors.Contains(Key(error.ErrorNumber, error.File, error.StartLine, error.StartColumn)))
                error.IsError = true;
        }

        public void Validate(BlockStatement tree, ActivationObject scope)
        {
            tree?.Accept(this);
            ValidateDeclarations(scope);
        }

        public void Report()
        {
            foreach (var violation in violations)
            {
                var source = violation.Context;
                var key = Key((int)violation.Error, source.Document.FileContext,
                    source.StartLineNumber, source.StartColumn + 1);
                if (!reported.Contains(key))
                    source.HandleError(violation.Error, true);
            }
        }

        public void Dispose()
        {
            parser.CompilerError -= ObserveError;
            parser.ContextValidator = null;
        }

        static bool HasStrictDirective(BlockStatement body)
            => body != null && body.TakeWhile(statement => statement is DirectivePrologue || statement is ImportantComment)
                .OfType<DirectivePrologue>().Any(directive => directive.UseStrict);

        public override void Visit(BlockStatement node)
        {
            if (node == null) return;
            var saved = context;
            context.Strict |= HasStrictDirective(node);
            base.Visit(node);
            context = saved;
        }

        public override void Visit(ModuleDeclaration node)
        {
            if (node == null) return;
            if (node.Body == null) { node.Binding?.Accept(this); return; }
            var saved = context;
            context = new Context { Strict = true, Await = true };
            node.Body.Accept(this);
            context = saved;
        }

        public override void Visit(ClassNode node)
        {
            if (node == null) return;
            var saved = context;
            context.Strict = true;
            node.Binding?.Accept(this);
            node.Heritage?.Accept(this);
            node.Elements?.Accept(this);
            context = saved;
        }

        public override void Visit(ClassField node)
        {
            if (node == null) return;
            node.ComputedName?.Accept(this);
            var saved = context;
            context = new Context { Strict = true, NewTarget = true, SuperProperty = true, ForbidArguments = true };
            node.Initializer?.Accept(this);
            context = saved;
        }

        public override void Visit(FunctionObject node)
        {
            if (node == null) return;
            // Computed keys execute in the surrounding context, before entering
            // the method's parameter/body environment.
            node.ComputedName?.Accept(this);
            var saved = context;
            var savedYield = parameterYield;
            var savedAwait = parameterAwait;
            bool arrow = node.FunctionType == FunctionType.ArrowFunction;
            bool staticBlock = node.FunctionType == FunctionType.StaticBlock;
            var ownerClass = node.Parent?.Parent as ClassNode;
            bool method = node.FunctionType == FunctionType.Method
                || node.FunctionType == FunctionType.Getter || node.FunctionType == FunctionType.Setter;
            bool ownDirective = HasStrictDirective(node.Body);
            parameterYield = node.IsGenerator;
            parameterAwait = node.IsAsync;
            context = new Context
            {
                Strict = saved.Strict || ownDirective || staticBlock,
                Return = !staticBlock,
                NewTarget = !arrow || saved.NewTarget,
                SuperCall = arrow ? saved.SuperCall : ownerClass?.Heritage != null && !node.IsStatic
                    && node.FunctionType == FunctionType.Method && node.ComputedName == null
                    && (node.Binding?.Name == "constructor" || node.LiteralName?.Value as string == "constructor"),
                SuperProperty = arrow ? saved.SuperProperty : method || staticBlock,
                ForbidArguments = staticBlock || (arrow && saved.ForbidArguments)
            };

            var parameters = node.ParameterDeclarations?.OfType<ParameterDeclaration>().ToArray()
                ?? new ParameterDeclaration[0];
            bool simple = parameters.All(parameter => !parameter.HasRest && parameter.Initializer == null
                && parameter.Binding is BindingIdentifier);
            if (!simple && ownDirective) Error(node.Body.Context);
            if (!method && !staticBlock) node.Binding?.Accept(this);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in BindingsVisitor.Bindings(node.ParameterDeclarations))
                if (!names.Add(binding.Name) && (context.Strict || !simple || arrow || method))
                    Error(binding.Context, JSError.StrictModeDuplicateArgument);

            parameterYield = node.IsGenerator;
            parameterAwait = node.IsAsync;
            node.ParameterDeclarations?.Accept(this);
            parameterYield = false;
            parameterAwait = false;
            context.Await = node.IsAsync && !staticBlock;
            context.Yield = node.IsGenerator && !staticBlock;
            node.Body?.Accept(this);

            parameterYield = savedYield;
            parameterAwait = savedAwait;
            context = saved;
        }

        public override void Visit(BindingIdentifier node)
        {
            if (node == null || node.Parent is ClassField) return;
            if (JSScanner.IsKeyword(node.Name, context.Strict)
                || (parameterYield && node.Name == "yield") || (parameterAwait && node.Name == "await"))
                Error(node.Context, JSError.KeywordUsedAsIdentifier);
            else if (context.Strict && (node.Name == "eval" || node.Name == "arguments"))
            {
                var parent = node.Parent;
                while (parent != null && !(parent is ParameterDeclaration) && !(parent is FunctionObject)
                    && !(parent is VariableDeclaration) && !(parent is TryStatement)) parent = parent.Parent;
                Error(node.Context, parent is ParameterDeclaration parameter && parameter.Parent is TryStatement ? JSError.StrictModeCatchName
                    : parent is ParameterDeclaration ? JSError.StrictModeArgumentName
                    : parent is FunctionObject ? JSError.StrictModeFunctionName
                    : parent is TryStatement ? JSError.StrictModeCatchName : JSError.StrictModeVariableName);
            }
        }

        public override void Visit(LookupExpression node)
        {
            if (node == null) return;
            if (context.ForbidArguments && node.Name == "arguments") Error(node.Context);
            if (node.Name == "super" && (node.Parent is MemberExpression
                || node.Parent is CallExpression index && index.InBrackets) && !context.SuperProperty) Error(node.Context);
            if (node.Name != "super" && node.Name != "new" && JSScanner.IsKeyword(node.Name, context.Strict))
                Error(node.Context, JSError.KeywordUsedAsIdentifier);
        }

        public override void Visit(MemberExpression node)
        {
            if (node?.Root is LookupExpression keyword && keyword.Name == "new" && node.Name == "target")
            {
                if (!context.NewTarget) Error(node.Context);
                return;
            }
            base.Visit(node);
        }

        public override void Visit(CallExpression node)
        {
            if (node?.Function is LookupExpression lookup && lookup.Name == "super" && !node.InBrackets
                && !context.SuperCall) Error(node.Context);
            base.Visit(node);
        }

        public override void Visit(UnaryExpression node)
        {
            if (node != null && ((node.OperatorToken == JSToken.Await && !context.Await)
                || (node.OperatorToken == JSToken.Yield && !context.Yield))) Error(node.Context);
            base.Visit(node);
        }

        public override void Visit(ReturnStatement node)
        {
            if (node != null && !context.Return) Error(node.Context, JSError.BadReturn);
            base.Visit(node);
        }

        public override void Visit(ContinueStatement node)
        {
            if (node == null) return;
            var parent = node.Parent;
            while (parent != null && !(parent is FunctionObject) && !(parent is IterationStatement)) parent = parent.Parent;
            if (!(parent is IterationStatement)) Error(node.Context, JSError.BadContinue);
        }

        public override void Visit(WithStatement node)
        {
            if (node != null && context.Strict) Error(node.Context, JSError.StrictModeNoWith);
            base.Visit(node);
        }

        public override void Visit(ForInStatement node)
        {
            if (node?.IsAwait == true && !context.Await) Error(node.Context);
            if (node != null && context.Strict && node.OperatorContext?.Is(JSToken.In) == true
                && node.Variable is VarDeclaration declaration && declaration.Any(item => item.Initializer != null))
                Error(node.Variable.Context);
            base.Visit(node);
        }

        void ValidateDeclarations(ActivationObject scope)
        {
            if (scope == null) return;
            var lexical = new Dictionary<string, INameDeclaration>(StringComparer.Ordinal);
            foreach (var binding in scope.LexicallyDeclaredNames)
            {
                // A malformed class binding may have been recovered as a pattern.
                // It does not introduce usable class declarations for conflict checks.
                var owner = binding.Parent;
                while (owner != null && !(owner is ClassNode) && !(owner is VariableDeclaration) && !(owner is FunctionObject)) owner = owner.Parent;
                if (owner is ClassNode recoveredClass && !(recoveredClass.Binding is BindingIdentifier)) continue;
                // Top-level ordinary function declarations are var-scoped.
                if (binding is AstNode node && node.Parent is FunctionObject
                    && !(scope is ModuleScope) && scope.ScopeType != ScopeType.Lexical) continue;
                if (lexical.ContainsKey(binding.Name)) Error(binding.Context, JSError.DuplicateLexicalDeclaration);
                else lexical.Add(binding.Name, binding);
            }
            foreach (var binding in scope.VarDeclaredNames)
                if (lexical.ContainsKey(binding.Name)
                    && !(scope is CatchScope catchScope && catchScope.CatchParameter?.Binding is BindingIdentifier catchBinding
                        && binding.Name == catchBinding.Name))
                    Error(binding.Context, JSError.DuplicateLexicalDeclaration);
            foreach (var child in scope.ChildScopes) ValidateDeclarations(child);
        }
    }
}
