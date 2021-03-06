﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2015-2016 SonarSource SA
 * mailto:contact@sonarsource.com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.CSharp;
using SonarLint.Helpers;

namespace SonarLint.Rules.CSharp
{
    [ExportCodeFixProvider(LanguageNames.CSharp)]
    public class ConditionalSimplificationCodeFixProvider : CodeFixProvider
    {
        internal const string Title = "Simplify condition";

        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(ConditionalSimplification.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var syntax = root.FindNode(diagnosticSpan);
            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var conditional = syntax as ConditionalExpressionSyntax;
            if (conditional != null)
            {
                var condition = TernaryOperatorPointless.RemoveParentheses(conditional.Condition);
                var whenTrue = TernaryOperatorPointless.RemoveParentheses(conditional.WhenTrue);
                var whenFalse = TernaryOperatorPointless.RemoveParentheses(conditional.WhenFalse);

                ExpressionSyntax compared;
                bool comparedIsNullInTrue;
                ConditionalSimplification.TryGetExpressionComparedToNull(condition, out compared, out comparedIsNullInTrue);

                var annotation = new SyntaxAnnotation();
                var coalescing = GetNullCoalescing(whenTrue, whenFalse, compared, semanticModel, annotation);

                context.RegisterCodeFix(
                    GetActionToExecute(context, root, conditional, coalescing, annotation),
                    context.Diagnostics);
            }

            var ifStatement = syntax as IfStatementSyntax;
            if (ifStatement != null)
            {
                var whenTrue = ConditionalSimplification.ExtractSingleStatement(ifStatement.Statement);
                var whenFalse = ConditionalSimplification.ExtractSingleStatement(ifStatement.Else.Statement);

                ExpressionSyntax compared;
                bool comparedIsNullInTrue;
                ConditionalSimplification.TryGetExpressionComparedToNull(ifStatement.Condition, out compared, out comparedIsNullInTrue);
                var isNullCoalescing = bool.Parse(diagnostic.Properties[ConditionalSimplification.IsNullCoalescingKey]);

                var annotation = new SyntaxAnnotation();
                var simplified = GetSimplified(whenTrue, whenFalse, ifStatement.Condition, compared, isNullCoalescing, semanticModel, annotation);

                context.RegisterCodeFix(
                    GetActionToExecute(context, root, ifStatement, simplified, annotation),
                    context.Diagnostics);
            }
        }

        private static CodeAction GetActionToExecute(CodeFixContext context, SyntaxNode root,
            SyntaxNode nodeToChange, SyntaxNode nodeToAdd, SyntaxAnnotation annotation)
        {
            return CodeAction.Create(
                Title,
                c =>
                {
                    var nodeToAddWithoutAnnotation = RemoveAnnotation(nodeToAdd, annotation);

                    var newRoot = root.ReplaceNode(
                        nodeToChange,
                        nodeToAddWithoutAnnotation.WithTriviaFrom(nodeToChange).WithAdditionalAnnotations(Formatter.Annotation));
                    return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                });
        }

        private static T RemoveAnnotation<T>(T node, SyntaxAnnotation annotation) where T: SyntaxNode
        {
            var annotated = node.GetAnnotatedNodes(annotation).FirstOrDefault();
            if (annotated == null)
            {
                return node;
            }

            if (annotated == node)
            {
                return node.WithoutAnnotations(annotation);
            }

            return node.ReplaceNode(annotated, annotated.WithoutAnnotations(annotation));
        }

        private static StatementSyntax GetSimplified(StatementSyntax statement1, StatementSyntax statement2,
            ExpressionSyntax condition, ExpressionSyntax compared, bool isNullCoalescing, SemanticModel semanticModel,
            SyntaxAnnotation annotation)
        {
            var return1 = statement1 as ReturnStatementSyntax;
            var return2 = statement2 as ReturnStatementSyntax;

            if (return1 != null && return2 != null)
            {
                var retExpr1 = TernaryOperatorPointless.RemoveParentheses(return1.Expression);
                var retExpr2 = TernaryOperatorPointless.RemoveParentheses(return2.Expression);

                var createdExpression = isNullCoalescing
                    ? GetNullCoalescing(retExpr1, retExpr2, compared, semanticModel, annotation)
                    : SyntaxFactory.ConditionalExpression(
                            condition,
                            return1.Expression,
                            return2.Expression)
                            .WithAdditionalAnnotations(annotation);

                return SyntaxFactory.ReturnStatement(createdExpression);
            }

            var expressionStatement1 = statement1 as ExpressionStatementSyntax;
            var expressionStatement2 = statement2 as ExpressionStatementSyntax;

            var expression1 = TernaryOperatorPointless.RemoveParentheses(expressionStatement1.Expression);
            var expression2 = TernaryOperatorPointless.RemoveParentheses(expressionStatement2.Expression);

            var assignment = GetSimplifiedAssignment(expression1, expression2, condition, compared, isNullCoalescing, semanticModel, annotation);
            if (assignment != null)
            {
                return SyntaxFactory.ExpressionStatement(assignment);
            }

            var expression = GetSimplificationFromInvocations(expression1, expression2, condition, compared, isNullCoalescing, semanticModel, annotation);
            if (expression != null)
            {
                return SyntaxFactory.ExpressionStatement(expression);
            }
            return null;
        }

        private static ExpressionSyntax GetSimplifiedAssignment(ExpressionSyntax expression1, ExpressionSyntax expression2,
            ExpressionSyntax condition, ExpressionSyntax compared, bool isNullCoalescing, SemanticModel semanticModel,
            SyntaxAnnotation annotation)
        {
            var assignment1 = expression1 as AssignmentExpressionSyntax;
            var assignment2 = expression2 as AssignmentExpressionSyntax;
            var canBeSimplified =
                assignment1 != null &&
                assignment2 != null &&
                EquivalenceChecker.AreEquivalent(assignment1.Left, assignment2.Left) &&
                assignment1.Kind() == assignment2.Kind();

            if (!canBeSimplified)
            {
                return null;
            }

            var createdExpression = isNullCoalescing
                ? GetNullCoalescing(assignment1.Right, assignment2.Right, compared, semanticModel, annotation)
                : SyntaxFactory.ConditionalExpression(
                    condition,
                    assignment1.Right,
                    assignment2.Right)
                    .WithAdditionalAnnotations(annotation);

            return SyntaxFactory.AssignmentExpression(
                assignment1.Kind(),
                assignment1.Left,
                createdExpression);
        }

        private static ExpressionSyntax GetNullCoalescing(ExpressionSyntax whenTrue, ExpressionSyntax whenFalse,
            ExpressionSyntax compared, SemanticModel semanticModel, SyntaxAnnotation annotation)
        {
            if (EquivalenceChecker.AreEquivalent(whenTrue, compared))
            {
                var createdExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    compared,
                    whenFalse)
                    .WithAdditionalAnnotations(annotation);
                return createdExpression;
            }

            if (EquivalenceChecker.AreEquivalent(whenFalse, compared))
            {
                var createdExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    compared,
                    whenTrue)
                    .WithAdditionalAnnotations(annotation);
                return createdExpression;
            }

            return GetSimplificationFromInvocations(whenTrue, whenFalse, null, compared, true, semanticModel, annotation);
        }

        private static ExpressionSyntax GetSimplificationFromInvocations(ExpressionSyntax expression1, ExpressionSyntax expression2,
            ExpressionSyntax condition, ExpressionSyntax compared, bool isNullCoalescing, SemanticModel semanticModel,
            SyntaxAnnotation annotation)
        {
            var methodCall1 = expression1 as InvocationExpressionSyntax;
            var methodCall2 = expression2 as InvocationExpressionSyntax;
            if (methodCall1 == null ||
                methodCall2 == null)
            {
                return null;
            }

            var methodSymbol1 = semanticModel.GetSymbolInfo(methodCall1).Symbol;
            var methodSymbol2 = semanticModel.GetSymbolInfo(methodCall2).Symbol;
            if (methodSymbol1 == null ||
                methodSymbol2 == null ||
                !methodSymbol1.Equals(methodSymbol2))
            {
                return null;
            }

            var newArgumentList = SyntaxFactory.ArgumentList();

            for (int i = 0; i < methodCall1.ArgumentList.Arguments.Count; i++)
            {
                var arg1 = methodCall1.ArgumentList.Arguments[i];
                var arg2 = methodCall2.ArgumentList.Arguments[i];

                if (!EquivalenceChecker.AreEquivalent(arg1.Expression, arg2.Expression))
                {
                    ExpressionSyntax createdExpression;
                    if (isNullCoalescing)
                    {
                        var arg1IsCompared = EquivalenceChecker.AreEquivalent(arg1.Expression, compared);
                        var expression = arg1IsCompared ? arg2.Expression : arg1.Expression;

                        createdExpression = SyntaxFactory.BinaryExpression(
                                    SyntaxKind.CoalesceExpression,
                                    compared,
                                    expression);
                    }
                    else
                    {
                        createdExpression = SyntaxFactory.ConditionalExpression(
                                    condition,
                                    arg1.Expression,
                                    arg2.Expression);
                    }

                    newArgumentList = newArgumentList.AddArguments(
                            SyntaxFactory.Argument(
                                arg1.NameColon,
                                arg1.RefOrOutKeyword,
                                createdExpression.WithAdditionalAnnotations(annotation)));
                }
                else
                {
                    newArgumentList = newArgumentList.AddArguments(arg1);
                }
            }

            return methodCall1.WithArgumentList(newArgumentList);
        }
    }
}

