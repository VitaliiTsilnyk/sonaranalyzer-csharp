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
using SonarLint.Common;
using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace SonarLint.Rules.CSharp
{
    [ExportCodeFixProvider(LanguageNames.CSharp)]
    public class RedundantDeclarationCodeFixProvider : CodeFixProvider
    {
        public const string TitleRedundantArraySize = "Remove redundant array size";
        public const string TitleRedundantArrayType = "Remove redundant array type";
        public const string TitleRedundantLambdaParameterType = "Remove redundant type declaration";
        public const string TitleRedundantExplicitDelegate = "Remove redundant explicit delegate creation";
        public const string TitleRedundantExplicitNullable = "Remove redundant explicit nullable creation";
        public const string TitleRedundantObjectInitializer = "Remove redundant object initializer";
        public const string TitleRedundantDelegateParameterList = "Remove redundant parameter list";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(RedundantDeclaration.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() => DocumentBasedFixAllProvider.Instance;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var syntaxNode = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

            RedundantDeclaration.RedundancyType diagnosticType;
            if (!Enum.TryParse(diagnostic.Properties[RedundantDeclaration.DiagnosticTypeKey], out diagnosticType))
            {
                return;
            }

            CodeAction action = null;
            if (TryGetAction(syntaxNode, root, diagnosticType, context.Document, out action))
            {
                context.RegisterCodeFix(action, context.Diagnostics);
            }
        }

        private static bool TryGetRedundantLambdaParameterAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, out CodeAction action)
        {
            var parameterList = syntaxNode.Parent?.Parent as ParameterListSyntax;
            if (parameterList == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(TitleRedundantLambdaParameterType, c =>
            {
                var newParameterList = parameterList.WithParameters(
                    SyntaxFactory.SeparatedList(parameterList.Parameters.Select(p =>
                        SyntaxFactory.Parameter(p.Identifier).WithTriviaFrom(p))));
                var newRoot = root.ReplaceNode(parameterList, newParameterList);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, TitleRedundantLambdaParameterType);
            return true;
        }

        private static bool TryGetRedundantArraySizeAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, out CodeAction action)
        {
            var arrayRank = syntaxNode.Parent as ArrayRankSpecifierSyntax;
            if (arrayRank == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(TitleRedundantArraySize, c =>
            {
                var newArrayRankSpecifier = arrayRank.WithSizes(
                    SyntaxFactory.SeparatedList<ExpressionSyntax>(arrayRank.Sizes.Select(s =>
                        SyntaxFactory.OmittedArraySizeExpression())));
                var newRoot = root.ReplaceNode(arrayRank, newArrayRankSpecifier);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, TitleRedundantArraySize);
            return true;
        }

        private static bool TryGetRedundantArrayTypeAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, out CodeAction action)
        {
            var arrayTypeSyntax = syntaxNode as ArrayTypeSyntax;
            if (arrayTypeSyntax == null)
            {
                arrayTypeSyntax = syntaxNode.Parent as ArrayTypeSyntax;
            }

            var arrayCreation = arrayTypeSyntax?.Parent as ArrayCreationExpressionSyntax;
            if (arrayCreation == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(TitleRedundantArrayType, c =>
            {
                var implicitArrayCreation = SyntaxFactory.ImplicitArrayCreationExpression(arrayCreation.Initializer);
                var newRoot = root.ReplaceNode(arrayCreation, implicitArrayCreation);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, TitleRedundantArrayType);
            return true;
        }

        private static bool TryGetRedundantExplicitObjectCreationAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, RedundantDeclaration.RedundancyType diagnosticType,  out CodeAction action)
        {
            var title = diagnosticType == RedundantDeclaration.RedundancyType.ExplicitDelegate
                ? TitleRedundantExplicitDelegate
                : TitleRedundantExplicitNullable;

            var objectCreation = syntaxNode as ObjectCreationExpressionSyntax;
            if (objectCreation == null)
            {
                action = null;
                return false;
            }

            var newExpression = objectCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            if (newExpression == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(title, c =>
            {
                newExpression = newExpression.WithTriviaFrom(objectCreation);
                var newRoot = root.ReplaceNode(objectCreation, newExpression);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, title);
            return true;
        }

        private static bool TryGetRedundantObjectInitializerAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, out CodeAction action)
        {
            var objectCreation = syntaxNode.Parent as ObjectCreationExpressionSyntax;
            if (objectCreation == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(TitleRedundantObjectInitializer, c =>
            {
                var newObjectCreation = objectCreation.WithInitializer(null).WithAdditionalAnnotations(Formatter.Annotation);
                var newRoot = root.ReplaceNode(objectCreation, newObjectCreation);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, TitleRedundantObjectInitializer);
            return true;
        }

        private static bool TryGetRedundantParameterTypeAction(SyntaxNode syntaxNode, SyntaxNode root,
            Document document, out CodeAction action)
        {
            var anonymousMethod = syntaxNode.Parent as AnonymousMethodExpressionSyntax;
            if (anonymousMethod == null)
            {
                action = null;
                return false;
            }

            action = CodeAction.Create(TitleRedundantDelegateParameterList, c =>
            {
                var newAnonymousMethod = anonymousMethod.WithParameterList(null);
                var newRoot = root.ReplaceNode(anonymousMethod, newAnonymousMethod);
                return Task.FromResult(document.WithSyntaxRoot(newRoot));
            }, TitleRedundantDelegateParameterList);
            return true;
        }

        private static bool TryGetAction(SyntaxNode syntaxNode, SyntaxNode root, RedundantDeclaration.RedundancyType diagnosticType,
            Document document, out CodeAction action)
        {
            switch (diagnosticType)
            {
                case RedundantDeclaration.RedundancyType.LambdaParameterType:
                    return TryGetRedundantLambdaParameterAction(syntaxNode, root, document, out action);
                case RedundantDeclaration.RedundancyType.ArraySize:
                    return TryGetRedundantArraySizeAction(syntaxNode, root, document, out action);
                case RedundantDeclaration.RedundancyType.ArrayType:
                    return TryGetRedundantArrayTypeAction(syntaxNode, root, document, out action);
                case RedundantDeclaration.RedundancyType.ExplicitDelegate:
                case RedundantDeclaration.RedundancyType.ExplicitNullable:
                    return TryGetRedundantExplicitObjectCreationAction(syntaxNode, root, document, diagnosticType, out action);
                case RedundantDeclaration.RedundancyType.ObjectInitializer:
                    return TryGetRedundantObjectInitializerAction(syntaxNode, root, document, out action);
                case RedundantDeclaration.RedundancyType.DelegateParameterList:
                    return TryGetRedundantParameterTypeAction(syntaxNode, root, document, out action);
                default:
                    throw new NotSupportedException();
            }
        }
    }
}

